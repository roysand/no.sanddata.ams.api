using Application.CQRS;
using Domain.Common;
using Features.Pricing.Queries;

namespace Features.Pricing.Commands;

/// <param name="ConfirmPastDate">Must be true when ValidFrom is today or earlier: past costs are recalculated.</param>
public record AddPriceRateCommand(
    PriceRateKind Kind, DateOnly ValidFrom, decimal Value, bool ConfirmPastDate, Guid ActingUserId)
    : ICommand<Result<PriceRateResponse>>;

public record DeletePriceRateCommand(Guid Id, Guid ActingUserId) : ICommand<Result<PricingDeleted>>;

public record CreateGridTariffCommand(string Name, Guid ActingUserId) : ICommand<Result<GridTariffResponse>>;

public record AddGridTariffRateCommand(
    Guid GridTariffId,
    DateOnly ValidFrom,
    decimal DayRatePerKwh,
    decimal NightRatePerKwh,
    bool ConfirmPastDate,
    Guid ActingUserId) : ICommand<Result<GridTariffRateResponse>>;

public record DeleteGridTariffRateCommand(Guid GridTariffId, Guid RateId, Guid ActingUserId)
    : ICommand<Result<PricingDeleted>>;

/// <param name="ElectricityTaxExempt">Null keeps the current value.</param>
/// <param name="VatExempt">Null keeps the current value.</param>
/// <param name="IsHolidayHome">Null keeps the current value.</param>
public record SetLocationPricingCommand(
    Guid LocationId,
    Guid? GridTariffId,
    decimal SupplierMarkupPerKwh,
    bool? ElectricityTaxExempt,
    bool? VatExempt,
    bool? IsHolidayHome,
    Guid ActingUserId)
    : ICommand<Result<LocationPricingResponse>>;

public record PricingDeleted(Guid Id);
