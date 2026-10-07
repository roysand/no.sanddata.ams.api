namespace Domain.Common;

/// <summary>
/// The Norwegian price zones a location may belong to. These are the zones the spot price source supports;
/// the ENTSO-E client keeps its own map of the same codes (Domain must not depend on Infrastructure).
/// </summary>
public static class PriceZones
{
    public static readonly IReadOnlyList<string> All = ["NO1", "NO2", "NO3", "NO4", "NO5"];

    public static bool IsValid(string? zone) => zone is not null && All.Contains(zone);
}
