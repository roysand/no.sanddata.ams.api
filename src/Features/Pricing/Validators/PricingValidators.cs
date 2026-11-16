using Domain.Common;
using FastEndpoints;
using Features.Pricing.Endpoints;
using Features.Pricing.Mappers;
using FluentValidation;

namespace Features.Pricing.Validators;

/// <summary>The limits shared by the validators of this slice (NOK per kWh, or a VAT fraction).</summary>
internal static class PricingLimits
{
    public const decimal MaxPerKwh = 100m;
    public const decimal MaxVatFraction = 1m;
    public const int Decimals = 4;
    public const int MaxNameLength = 100;

    public static bool IsAmount(decimal value, decimal max) =>
        value >= 0m && value <= max && Math.Round(value, Decimals) == value;
}

public class ListPriceRatesValidator : Validator<ListPriceRatesRequest>
{
    public ListPriceRatesValidator() =>
        RuleFor(x => x.Kind)
            .Must(kind => kind is null || PricingMapper.TryParseKind(kind, out _))
            .WithMessage("Kind must be one of ElectricityTax, EnovaFee, Vat")
            .WithErrorCode("Validation.InvalidKind");
}

public class AddPriceRateValidator : Validator<AddPriceRateRequest>
{
    public AddPriceRateValidator()
    {
        RuleFor(x => x.Kind)
            .Must(kind => PricingMapper.TryParseKind(kind, out _))
            .WithMessage("Kind must be one of ElectricityTax, EnovaFee, Vat")
            .WithErrorCode("Validation.InvalidKind");

        RuleFor(x => x.ValidFrom)
            .NotEqual(default(DateOnly))
            .WithMessage("A start date is required")
            .WithErrorCode("Validation.ValidFromRequired");

        // VAT is a fraction (0.25), the two fees are NOK per kWh.
        RuleFor(x => x.Value)
            .Must((request, value) =>
                PricingLimits.IsAmount(
                    value,
                    PricingMapper.TryParseKind(request.Kind, out PriceRateKind kind) && kind == PriceRateKind.Vat
                        ? PricingLimits.MaxVatFraction
                        : PricingLimits.MaxPerKwh))
            .WithMessage("Value must not be negative, must be at most 100 (1 for VAT) and have at most 4 decimals")
            .WithErrorCode("Validation.InvalidValue");
    }
}

public class CreateGridTariffValidator : Validator<CreateGridTariffRequest>
{
    public CreateGridTariffValidator() =>
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("A name is required")
            .MaximumLength(PricingLimits.MaxNameLength);
}

public class AddGridTariffRateValidator : Validator<AddGridTariffRateRequest>
{
    public AddGridTariffRateValidator()
    {
        RuleFor(x => x.ValidFrom)
            .NotEqual(default(DateOnly))
            .WithMessage("A start date is required")
            .WithErrorCode("Validation.ValidFromRequired");

        RuleFor(x => x.DayRatePerKwh)
            .Must(value => PricingLimits.IsAmount(value, PricingLimits.MaxPerKwh))
            .WithMessage("Day rate must be between 0 and 100 with at most 4 decimals")
            .WithErrorCode("Validation.InvalidDayRate");

        RuleFor(x => x.NightRatePerKwh)
            .Must(value => PricingLimits.IsAmount(value, PricingLimits.MaxPerKwh))
            .WithMessage("Night rate must be between 0 and 100 with at most 4 decimals")
            .WithErrorCode("Validation.InvalidNightRate");
    }
}

public class SetLocationPricingValidator : Validator<SetLocationPricingRequest>
{
    public SetLocationPricingValidator() =>
        RuleFor(x => x.SupplierMarkupPerKwh)
            .Must(value => PricingLimits.IsAmount(value, PricingLimits.MaxPerKwh))
            .WithMessage("Markup must be between 0 and 100 with at most 4 decimals")
            .WithErrorCode("Validation.InvalidMarkup");
}
