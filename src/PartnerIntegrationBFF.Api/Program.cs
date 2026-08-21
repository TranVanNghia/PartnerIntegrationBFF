using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using PartnerIntegrationBFF.Api.ErrorHandling;
using PartnerIntegrationBFF.Api.Messaging;
using PartnerIntegrationBFF.Api.Models;
using PartnerIntegrationBFF.Api.Security;
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

// Global exception handler (Bonus) — catches anything that isn't already handled closer to where
// it happened, and formats it as a consistent ProblemDetails response.
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

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

// Security (Bonus): JWT issuance + a global "is authentication required at all" toggle.
// See docs/architecture/bonus.md for the full design and its simplifications.
builder.Services
    .AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.SigningKey) && o.SigningKey.Length >= 32,
        $"{JwtOptions.SectionName}:SigningKey must be at least 32 characters (HMAC-SHA256).")
    .ValidateOnStart();
builder.Services
    .AddOptions<SecurityOptions>()
    .Bind(builder.Configuration.GetSection(SecurityOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.ClientSecret), $"{SecurityOptions.SectionName}:ClientSecret is required.")
    .ValidateOnStart();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<PartnerAuthorizationService>();

// The JwtOptions binding above already validates SigningKey is non-null/long-enough on startup,
// but that validation hasn't run yet at this point in Program.cs — so options are read directly
// from configuration here rather than via the validated IOptions<JwtOptions>.
var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwtSection["Issuer"],
            ValidAudience = jwtSection["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["SigningKey"] ?? string.Empty)),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.

// Registered first so it wraps every other middleware below.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();

// Bonus: enforces authentication across the whole API when Security:RequireAuthentication is on
// (default off — see SecurityOptions). Must run after UseAuthentication (needs context.User
// populated) and before UseAuthorization/MapControllers.
app.UseMiddleware<RequireAuthenticationMiddleware>();

app.UseAuthorization();

app.MapControllers();

app.Run();
