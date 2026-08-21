using FluentValidation;
using PartnerIntegrationBFF.Api.Messaging;
using PartnerIntegrationBFF.Api.Models;
using PartnerIntegrationBFF.Api.Services;
using PartnerIntegrationBFF.Api.Validation;
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

// Partner verification client + resilience pipeline (Step 2). Extracted into an extension method
// (Services/PartnerVerificationServiceCollectionExtensions.cs) so tests can build the exact same
// pipeline against a fake handler instead of duplicating this configuration.
builder.Services.AddPartnerVerificationClient(builder.Configuration);

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
