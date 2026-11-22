using Features.ElectricityCost.Queries;

namespace Features.ElectricityCost.Mappers;

public static class NorgesPrisStatusMapper
{
    /// <summary>
    /// One status entry. Over the limit means at or above it, as when the next kWh is priced on spot terms (spec 018,
    /// research R8); without a limit a location is never over.
    /// </summary>
    public static NorgesPrisStatusItem ToItem(
        Guid locationId,
        DateOnly month,
        decimal? limitKwh,
        decimal kwhSoFar,
        decimal? projectedKwh,
        decimal? norgesPrisPricePerKwh,
        DateTime resetsAtUtc)
    {
        bool over = limitKwh is { } limit && kwhSoFar >= limit;
        return new NorgesPrisStatusItem(
            locationId,
            month.ToString("yyyy-MM"),
            limitKwh,
            kwhSoFar,
            projectedKwh,
            over,
            over ? kwhSoFar - limitKwh!.Value : 0m,
            norgesPrisPricePerKwh,
            resetsAtUtc);
    }
}
