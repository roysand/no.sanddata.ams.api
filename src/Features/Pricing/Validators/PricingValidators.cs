using Domain.Common;
using FastEndpoints;
using Features.Pricing.Commands;
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
    public const decimal MaxMonthlyFee = 10000m;
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
    public SetLocationPricingValidator()
    {
        RuleFor(x => x.SupplierMarkupPerKwh)
            .Must(value => PricingLimits.IsAmount(value, PricingLimits.MaxPerKwh))
            .WithMessage("Markup must be between 0 and 100 with at most 4 decimals")
            .WithErrorCode("Validation.InvalidMarkup");

        RuleFor(x => x.SupplierMonthlyFee)
            .Must(value => value is null || PricingLimits.IsAmount(value.Value, PricingLimits.MaxMonthlyFee))
            .WithMessage("Monthly fee must be between 0 and 10000 with at most 4 decimals")
            .WithErrorCode("Validation.InvalidMonthlyFee");
    }
}

public class AddCapacityStepsValidator : Validator<AddCapacityStepsRequest>
{
    private const decimal MaxKwOrAmount = 99999m;
    private const int MaxSteps = 50;

    public AddCapacityStepsValidator()
    {
        RuleFor(x => x.ValidFrom)
            .NotEqual(default(DateOnly))
            .WithMessage("A start date is required")
            .WithErrorCode("Validation.ValidFromRequired");

        RuleFor(x => x.Steps)
            .Must(steps => steps is { Count: > 0 and <= MaxSteps })
            .WithMessage($"Between 1 and {MaxSteps} steps are required")
            .WithErrorCode("Validation.InvalidSteps");

        RuleFor(x => x.Steps)
            .Must(steps => steps is null || steps.All(s =>
                PricingLimits.IsAmount(s.LowerKw, MaxKwOrAmount)
                && (s.UpperKw is null || PricingLimits.IsAmount(s.UpperKw.Value, MaxKwOrAmount))
                && PricingLimits.IsAmount(s.MonthlyAmount, MaxKwOrAmount)))
            .WithMessage("kW and amounts must be between 0 and 99999 with at most 4 decimals")
            .WithErrorCode("Validation.InvalidStepValue");

        RuleFor(x => x.Steps)
            .Must(IsAContiguousTable)
            .When(x => x.Steps is { Count: > 0 })
            .WithMessage("The steps must start at 0 kW, each must end above where it starts, each must start where the " +
                         "previous one ends, and only the last may have no upper bound")
            .WithErrorCode("Validation.StepsNotContiguous");
    }

    private static bool IsAContiguousTable(IReadOnlyList<CapacityStepInput> steps)
    {
        if (steps[0].LowerKw != 0m || steps[^1].UpperKw is not null)
        {
            return false;
        }

        for (int i = 0; i < steps.Count - 1; i++)
        {
            if (steps[i].UpperKw is not { } upper || upper <= steps[i].LowerKw || steps[i + 1].LowerKw != upper)
            {
                return false;
            }
        }

        return true;
    }
}

public class DeleteCapacityStepsValidator : Validator<DeleteCapacityStepsRequest>
{
    public DeleteCapacityStepsValidator() =>
        RuleFor(x => x.ValidFrom)
            .NotEqual(default(DateOnly))
            .WithMessage("A start date is required")
            .WithErrorCode("Validation.ValidFromRequired");
}
