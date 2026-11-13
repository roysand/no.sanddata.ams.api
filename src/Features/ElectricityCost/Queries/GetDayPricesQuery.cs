using Application.CQRS;
using Domain.Common;

namespace Features.ElectricityCost.Queries;

/// <summary>Hourly rates for one Oslo calendar day (today when <paramref name="Date"/> is null).</summary>
public record GetDayPricesQuery(Guid UserId, Guid LocationId, DateTime? Date) : IQuery<Result<DayPricesResponse>>;

/// <summary>
/// The NOK rate per kWh for one hour under the location's enrolled model, and the spot (market) rate for the same
/// hour. Either is null when its price or exchange rate is not available yet.
/// </summary>
public record HourPriceResponse(DateTime HourStart, decimal? RatePerKwh, decimal? SpotRatePerKwh);

public record DayPricesResponse(DateOnly Date, string Zone, string PricingModel, IReadOnlyList<HourPriceResponse> Items);
