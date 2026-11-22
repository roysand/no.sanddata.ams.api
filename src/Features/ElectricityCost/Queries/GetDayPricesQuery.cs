using Application.CQRS;
using Domain.Common;

namespace Features.ElectricityCost.Queries;

/// <summary>Hourly rates for one Oslo calendar day (today when <paramref name="Date"/> is null).</summary>
public record GetDayPricesQuery(Guid UserId, Guid LocationId, DateTime? Date) : IQuery<Result<DayPricesResponse>>;

/// <summary>
/// The NOK rate per kWh for one hour under the location's enrolled model, and the spot (market) rate for the same
/// hour. Either is null when its price or exchange rate is not available yet.
/// </summary>
/// <param name="SpotEnergyPricePerKwh">
/// The day-ahead price in NOK per kWh including VAT (none when the location is VAT exempt) and nothing else: no grid
/// charge, taxes or supplier markup. The same whatever the pricing model or the Norgespris cap; null when the price or
/// the exchange rate is missing, and negative when the price is (spec 018).
/// </param>
public record HourPriceResponse(
    DateTime HourStart, decimal? RatePerKwh, decimal? SpotRatePerKwh, decimal? SpotEnergyPricePerKwh);

public record DayPricesResponse(DateOnly Date, string Zone, string PricingModel, IReadOnlyList<HourPriceResponse> Items);
