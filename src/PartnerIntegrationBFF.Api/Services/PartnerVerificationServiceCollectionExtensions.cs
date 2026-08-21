using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;

namespace PartnerIntegrationBFF.Api.Services;

/// <summary>
/// Registers <see cref="IPartnerVerificationClient"/> and its resilience pipeline. Pulled out of
/// Program.cs so tests can build the exact same HttpClient + retry/timeout/circuit-breaker pipeline
/// against a fake handler, instead of duplicating (and risking drift from) this configuration.
/// </summary>
public static class PartnerVerificationServiceCollectionExtensions
{
    public static IServiceCollection AddPartnerVerificationClient(this IServiceCollection services, IConfiguration configuration)
    {
        // IHttpContextAccessor lets PartnerVerificationClient read the current request's scheme/host,
        // since the "external" verification API is actually simulated in this same project.
        services.AddHttpContextAccessor();

        // .Validate(...).ValidateOnStart() means a missing/blank RelativePath fails fast at app
        // startup with a clear error, instead of surfacing as a confusing NullReferenceException.
        services
            .AddOptions<PartnerVerificationApiOptions>()
            .Bind(configuration.GetSection(PartnerVerificationApiOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.RelativePath), $"{PartnerVerificationApiOptions.SectionName}:RelativePath is required.")
            .ValidateOnStart();

        // AddHttpClient<TInterface, TImplementation> registers PartnerVerificationClient as a "typed
        // client": DI injects a pre-configured HttpClient into its constructor, and every request made
        // through it flows through the resilience handler configured below.
        services.AddHttpClient<IPartnerVerificationClient, PartnerVerificationClient>()
            .AddStandardResilienceHandler(options =>
            {
                // Retry up to 3 times with exponential backoff + jitter — jitter spreads retries out
                // so a burst of failing requests doesn't all retry at the exact same moment.
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.Delay = TimeSpan.FromMilliseconds(200);
                options.Retry.BackoffType = DelayBackoffType.Exponential;
                options.Retry.UseJitter = true;
                // Give up on a single attempt after 2s; give up on the whole call (all retries) after
                // 10s — caps how long a caller can be kept waiting even in the worst case.
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(4);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
            });

        return services;
    }
}
