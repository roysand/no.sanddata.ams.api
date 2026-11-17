using Application.CQRS;
using Domain.Common;

namespace Features.Pricing.Queries;

/// <param name="Kind">ElectricityTax, EnovaFee or Vat, as text.</param>
/// <param name="Status">Superseded, InEffect (one per kind) or Upcoming, from today in Norway.</param>
public record PriceRateResponse(Guid Id, string Kind, DateOnly ValidFrom, decimal Value, string Status);

public record GridTariffRateResponse(
    Guid Id, DateOnly ValidFrom, decimal DayRatePerKwh, decimal NightRatePerKwh, string Status);

public record GridTariffResponse(Guid Id, string Name, IReadOnlyList<GridTariffRateResponse> Rates);

/// <param name="IsComplete">True when a grid tariff is set; without one the grid part of the price is left out.</param>
public record LocationPricingResponse(
    Guid LocationId,
    string Name,
    Guid? GridTariffId,
    string? GridTariffName,
    decimal SupplierMarkupPerKwh,
    bool ElectricityTaxExempt,
    bool VatExempt,
    bool IsHolidayHome,
    bool IsComplete);

public record ListPriceRatesQuery(PriceRateKind? Kind, Guid ActingUserId)
    : IQuery<Result<IReadOnlyList<PriceRateResponse>>>;

public record ListGridTariffsQuery(Guid ActingUserId) : IQuery<Result<IReadOnlyList<GridTariffResponse>>>;

public record ListLocationPricingQuery(Guid ActingUserId) : IQuery<Result<IReadOnlyList<LocationPricingResponse>>>;
