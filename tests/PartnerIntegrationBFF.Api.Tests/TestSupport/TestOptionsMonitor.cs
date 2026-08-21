using Microsoft.Extensions.Options;

namespace PartnerIntegrationBFF.Api.Tests.TestSupport;

/// <summary>Minimal IOptionsMonitor&lt;T&gt; fake — just exposes a settable CurrentValue.</summary>
public class TestOptionsMonitor<T> : IOptionsMonitor<T>
{
    public TestOptionsMonitor(T currentValue)
    {
        CurrentValue = currentValue;
    }

    public T CurrentValue { get; set; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
