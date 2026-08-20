using System.Globalization;

namespace PartnerIntegrationBFF.Api.Validation;

public static class CurrencyCodeProvider
{
    public static readonly HashSet<string> ValidIso4217Codes = CultureInfo
        .GetCultures(CultureTypes.SpecificCultures)
        .Select(TryGetIsoCurrencySymbol)
        .Where(code => code is not null)
        .Select(code => code!)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string? TryGetIsoCurrencySymbol(CultureInfo culture)
    {
        try
        {
            return new RegionInfo(culture.Name).ISOCurrencySymbol;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public static bool IsValid(string? currencyCode) =>
        !string.IsNullOrWhiteSpace(currencyCode) && ValidIso4217Codes.Contains(currencyCode);
}
