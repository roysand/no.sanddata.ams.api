using Application.CQRS;
using Domain.Common;

namespace Features.Pricing.Queries;

/// <param name="Kind">ElectricityTax, EnovaFee or Vat, as text.</param>
/// <param name="Status">Superseded, InEffect (one per kind) or Upcoming, from today in Norway.</param>
public record PriceRateResponse(Guid Id, string Kind, DateOnly ValidFrom, decimal Value, string Status);

public record GridTariffRateResponse(
    Guid Id, DateOnly ValidFrom, decimal DayRatePerKwh, decimal NightRatePerKwh, string Status);

public record GridTariffResponse(Guid Id, string Name, IReadOnlyList<GridTariffRateResponse> Rates);

/// <param name="UpperKw">Null for the top step.</param>
/// <param name="MonthlyAmount">NOK per month excluding VAT.</param>
public record CapacityStepResponse(decimal LowerKw, decimal? UpperKw, decimal MonthlyAmount);

/// <param name="Status">Superseded, InEffect (one per tariff) or Upcoming, from today in Norway.</param>
public record CapacityStepTableResponse(DateOnly ValidFrom, string Status, IReadOnlyList<CapacityStepResponse> Steps);

public record ListCapacityStepsQuery(Guid GridTariffId, Guid ActingUserId)
    : IQuery<Result<IReadOnlyList<CapacityStepTableResponse>>>;

/// <param name="SupplierMonthlyFee">NOK per month excluding VAT; null until it is set.</param>
/// <param name="IsComplete">
/// True when a grid tariff and the supplier monthly fee are set; without the tariff the grid part of the price is left
/// out, and without the fee the bill shows the fee as not set.
/// </param>
public record LocationPricingResponse(
    Guid LocationId,
    string Name,
    Guid? GridTariffId,
    string? GridTariffName,
    decimal SupplierMarkupPerKwh,
    decimal? SupplierMonthlyFee,
    bool ElectricityTaxExempt,
    bool VatExempt,
    bool IsHolidayHome,
    bool IsComplete);

public record ListPriceRatesQuery(PriceRateKind? Kind, Guid ActingUserId)
    : IQuery<Result<IReadOnlyList<PriceRateResponse>>>;

public record ListGridTariffsQuery(Guid ActingUserId) : IQuery<Result<IReadOnlyList<GridTariffResponse>>>;

public record ListLocationPricingQuery(Guid ActingUserId) : IQuery<Result<IReadOnlyList<LocationPricingResponse>>>;
