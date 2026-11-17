using Domain.Common;
using Domain.Common.Entities;
using Features.Pricing.Queries;

namespace Features.Pricing.Mappers;

public static class PricingMapper
{
    public const string Superseded = "Superseded";
    public const string InEffect = "InEffect";
    public const string Upcoming = "Upcoming";

    /// <summary>
    /// Reads a kind from the text of a request. The API does not write enums as text, so the requests carry strings;
    /// a number such as "1" is refused, only the names count.
    /// </summary>
    public static bool TryParseKind(string? text, out PriceRateKind kind)
    {
        kind = default;
        return !string.IsNullOrWhiteSpace(text)
            && !int.TryParse(text, out _)
            && Enum.TryParse(text.Trim(), ignoreCase: true, out kind)
            && Enum.IsDefined(kind);
    }

    /// <summary>The rows (any order) with their status; the row in effect is the latest one started, per kind.</summary>
    public static IReadOnlyList<PriceRateResponse> ToPriceRateResponses(
        IEnumerable<PriceRate> rates, DateOnly today, PriceRateKind? kind = null)
    {
        var responses = new List<PriceRateResponse>();
        foreach (IGrouping<PriceRateKind, PriceRate> group in rates
                     .Where(r => kind is null || r.Kind == kind)
                     .GroupBy(r => r.Kind)
                     .OrderBy(g => g.Key))
        {
            DateOnly? inEffect = LatestStarted(group.Select(r => r.ValidFrom), today);
            responses.AddRange(group
                .OrderBy(r => r.ValidFrom)
                .Select(r => new PriceRateResponse(
                    r.Id, r.Kind.ToString(), r.ValidFrom, r.Value, StatusOf(r.ValidFrom, today, inEffect))));
        }

        return responses;
    }

    public static GridTariffResponse ToGridTariffResponse(
        GridTariff tariff, IEnumerable<GridTariffRate> rates, DateOnly today)
    {
        GridTariffRate[] ordered = rates.OrderBy(r => r.ValidFrom).ToArray();
        DateOnly? inEffect = LatestStarted(ordered.Select(r => r.ValidFrom), today);

        return new GridTariffResponse(
            tariff.Id,
            tariff.Name,
            ordered.Select(r => ToGridTariffRateResponse(r, today, inEffect)).ToList());
    }

    /// <param name="allRatesOfTheTariff">Every rate of the tariff, so the status of one is judged against its siblings.</param>
    public static GridTariffRateResponse ToGridTariffRateResponse(
        GridTariffRate rate, IEnumerable<GridTariffRate> allRatesOfTheTariff, DateOnly today) =>
        ToGridTariffRateResponse(rate, today, LatestStarted(allRatesOfTheTariff.Select(r => r.ValidFrom), today));

    public static LocationPricingResponse ToLocationPricingResponse(Location location, string? gridTariffName) =>
        new(location.Id, location.Name, location.GridTariffId, gridTariffName, location.SupplierMarkupPerKwh,
            location.ElectricityTaxExempt, location.VatExempt, location.IsHolidayHome, location.GridTariffId is not null);

    private static GridTariffRateResponse ToGridTariffRateResponse(
        GridTariffRate rate, DateOnly today, DateOnly? inEffect) =>
        new(rate.Id, rate.ValidFrom, rate.DayRatePerKwh, rate.NightRatePerKwh, StatusOf(rate.ValidFrom, today, inEffect));

    private static DateOnly? LatestStarted(IEnumerable<DateOnly> starts, DateOnly today)
    {
        DateOnly[] started = starts.Where(s => s <= today).ToArray();
        return started.Length == 0 ? null : started.Max();
    }

    private static string StatusOf(DateOnly validFrom, DateOnly today, DateOnly? inEffect) =>
        validFrom > today ? Upcoming : validFrom == inEffect ? InEffect : Superseded;
}
