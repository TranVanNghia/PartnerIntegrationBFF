using FluentValidation;
using PartnerIntegrationBFF.Api.Messaging;
using PartnerIntegrationBFF.Api.Models;
using PartnerIntegrationBFF.Api.Services;
using PartnerIntegrationBFF.Api.Validation;
using Polly;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Local-only overrides (e.g. a hosted broker for testing without Docker) — gitignored, takes
// precedence over appsettings.json/appsettings.{Environment}.json. See appsettings.Local.json.example.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Logging: Serilog to console + a daily rolling file under logs/ (see Serilog:* in appsettings.json).
builder.Host.UseSerilog((context, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(context.HostingEnvironment.ContentRootPath, "logs", "log-.txt"),
        rollingInterval: RollingInterval.Day));

// Add services to the container.

// MVC + request validation (Step 1).
builder.Services.AddControllers();
builder.Services.AddScoped<IValidator<PartnerTransactionRequest>, PartnerTransactionRequestValidator>();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Partner verification client + resilience pipeline (Step 2).
// IHttpContextAccessor lets PartnerVerificationClient read the current request's scheme/host, since
// the "external" verification API is actually simulated in this same project (no separate base URL).
builder.Services.AddHttpContextAccessor();

// .Validate(...).ValidateOnStart() means a missing/blank RelativePath fails fast at app startup
// with a clear error, instead of surfacing as a confusing NullReferenceException on the first request.
builder.Services
    .AddOptions<PartnerVerificationApiOptions>()
    .Bind(builder.Configuration.GetSection(PartnerVerificationApiOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.RelativePath), $"{PartnerVerificationApiOptions.SectionName}:RelativePath is required.")
    .ValidateOnStart();

// AddHttpClient<TInterface, TImplementation> registers PartnerVerificationClient as a "typed client":
// DI injects a pre-configured HttpClient into its constructor, and every request made through it
// flows through the resilience handler configured below.
builder.Services.AddHttpClient<IPartnerVerificationClient, PartnerVerificationClient>()
    .AddStandardResilienceHandler(options =>
    {
        // Retry up to 3 times with exponential backoff + jitter — jitter spreads retries out so a
        // burst of failing requests doesn't all retry at the exact same moment and hammer the API.
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.Delay = TimeSpan.FromMilliseconds(200);
        options.Retry.BackoffType = DelayBackoffType.Exponential;
        options.Retry.UseJitter = true;
        // Give up on a single attempt after 2s; give up on the whole call (all retries) after 10s —
        // caps how long a caller can be kept waiting even in the worst case.
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(4);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
    });

// Transaction queue publisher (Step 3) — RabbitMQ connection/queue settings + the publisher itself.
// Same fail-fast-on-startup pattern as PartnerVerificationApiOptions above.
builder.Services
    .AddOptions<RabbitMqOptions>()
    .Bind(builder.Configuration.GetSection(RabbitMqOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.QueueName), $"{RabbitMqOptions.SectionName}:QueueName is required.")
    .ValidateOnStart();

// Singleton, not scoped: RabbitMqTransactionQueuePublisher owns one long-lived broker connection
// meant to be shared across all requests for the lifetime of the app (see its own comments for why).
builder.Services.AddSingleton<ITransactionQueuePublisher, RabbitMqTransactionQueuePublisher>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
