using Application.CQRS;
using Domain.Common;

namespace Features.ElectricityCost.Queries;

/// <summary>Hourly rates for one Oslo calendar day (today when <paramref name="Date"/> is null).</summary>
public record GetDayPricesQuery(Guid UserId, Guid LocationId, DateTime? Date) : IQuery<Result<DayPricesResponse>>;

/// <summary>The NOK rate per kWh for one hour; null when the price or exchange rate is not available yet.</summary>
public record HourPriceResponse(DateTime HourStart, decimal? RatePerKwh);

public record DayPricesResponse(DateOnly Date, string Zone, string PricingModel, IReadOnlyList<HourPriceResponse> Items);
