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
    public const decimal MaxCapKwh = 99999m;
    public const string KindNames = "ElectricityTax, EnovaFee, Vat, NorgesPrisCapHome, NorgesPrisCapHolidayHome";

    /// <summary>The largest value of a kind: a VAT fraction, a cap in kWh per month, or NOK per kWh for the fees.</summary>
    public static decimal MaxFor(PriceRateKind kind) => kind switch
    {
        PriceRateKind.Vat => MaxVatFraction,
        PriceRateKind.NorgesPrisCapHome or PriceRateKind.NorgesPrisCapHolidayHome => MaxCapKwh,
        _ => MaxPerKwh
    };

    public static bool IsCap(PriceRateKind kind) =>
        kind is PriceRateKind.NorgesPrisCapHome or PriceRateKind.NorgesPrisCapHolidayHome;
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
            .WithMessage("Kind must be one of " + PricingLimits.KindNames)
            .WithErrorCode("Validation.InvalidKind");
}

public class AddPriceRateValidator : Validator<AddPriceRateRequest>
{
    public AddPriceRateValidator()
    {
        RuleFor(x => x.Kind)
            .Must(kind => PricingMapper.TryParseKind(kind, out _))
            .WithMessage("Kind must be one of " + PricingLimits.KindNames)
            .WithErrorCode("Validation.InvalidKind");

        RuleFor(x => x.ValidFrom)
            .NotEqual(default(DateOnly))
            .WithMessage("A start date is required")
            .WithErrorCode("Validation.ValidFromRequired");

        // VAT is a fraction (0.25), the two fees are NOK per kWh, a cap is kWh per month and must be above 0.
        RuleFor(x => x.Value)
            .Must((request, value) =>
            {
                PriceRateKind kind = PricingMapper.TryParseKind(request.Kind, out PriceRateKind parsed)
                    ? parsed
                    : PriceRateKind.ElectricityTax;
                return PricingLimits.IsAmount(value, PricingLimits.MaxFor(kind))
                    && (!PricingLimits.IsCap(kind) || value > 0m);
            })
            .WithMessage("Value must not be negative, must be at most 100 (1 for VAT; above 0 and at most 99999 kWh " +
                         "for a cap) and have at most 4 decimals")
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
