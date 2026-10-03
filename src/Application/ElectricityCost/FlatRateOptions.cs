namespace Application.ElectricityCost;

/// <summary>Flat government-set rate (Norgespris) - configuration, not data (research.md §8).</summary>
public class FlatRateOptions
{
    public const string SectionName = "NorgesPris";

    public decimal RatePerKwh { get; init; } = 0.40m;
    public decimal TaxPerKwh { get; init; }
}
