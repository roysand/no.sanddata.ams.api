using Domain.Common.Entities;
using Features.NorgesPris.Queries;

namespace Features.NorgesPris.Mappers;

public static class NorgesPrisMapper
{
    public const string Superseded = "Superseded";
    public const string InEffect = "InEffect";
    public const string Upcoming = "Upcoming";

    /// <param name="ratesOrderedByValidFrom">Every rate, oldest start date first.</param>
    /// <param name="today">Today's date in Norway.</param>
    public static IReadOnlyList<NorgesPrisRateResponse> ToResponses(
        IReadOnlyList<NorgesPrisRate> ratesOrderedByValidFrom, DateOnly today)
    {
        NorgesPrisRate? inEffect = ratesOrderedByValidFrom.LastOrDefault(r => r.ValidFrom <= today);

        return ratesOrderedByValidFrom
            .Select(r => new NorgesPrisRateResponse(
                r.Id, r.ValidFrom, r.RatePerKwh, r.TaxPerKwh, r.RatePerKwh + r.TaxPerKwh,
                r.ValidFrom > today ? Upcoming : r.Id == inEffect?.Id ? InEffect : Superseded))
            .ToList();
    }
}
