using FluentValidation;
using PartnerIntegrationBFF.Api.Models;
using PartnerIntegrationBFF.Api.Services;
using PartnerIntegrationBFF.Api.Validation;
using Polly;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(context.HostingEnvironment.ContentRootPath, "logs", "log-.txt"),
        rollingInterval: RollingInterval.Day));

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddScoped<IValidator<PartnerTransactionRequest>, PartnerTransactionRequestValidator>();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHttpContextAccessor();
builder.Services
    .AddOptions<PartnerVerificationApiOptions>()
    .Bind(builder.Configuration.GetSection(PartnerVerificationApiOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.RelativePath), $"{PartnerVerificationApiOptions.SectionName}:RelativePath is required.")
    .ValidateOnStart();
builder.Services.AddHttpClient<IPartnerVerificationClient, PartnerVerificationClient>()
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.Delay = TimeSpan.FromMilliseconds(200);
        options.Retry.BackoffType = DelayBackoffType.Exponential;
        options.Retry.UseJitter = true;
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(4);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
    });

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
