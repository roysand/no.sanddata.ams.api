using FastEndpoints;
using Features.NorgesPris.Endpoints;
using FluentValidation;

namespace Features.NorgesPris.Validators;

public class AddNorgesPrisRateValidator : Validator<AddNorgesPrisRateRequest>
{
    public const decimal MaxPerKwh = 100m;

    public AddNorgesPrisRateValidator()
    {
        RuleFor(x => x.ValidFrom)
            .NotEqual(default(DateOnly))
            .WithMessage("A start date is required")
            .WithErrorCode("Validation.ValidFromRequired");

        RuleFor(x => x.RatePerKwh).Must(BeAValidAmount)
            .WithMessage($"Rate must be between 0 and {MaxPerKwh} with at most 4 decimals")
            .WithErrorCode("Validation.InvalidRate");

        RuleFor(x => x.TaxPerKwh).Must(BeAValidAmount)
            .WithMessage($"Tax must be between 0 and {MaxPerKwh} with at most 4 decimals")
            .WithErrorCode("Validation.InvalidTax");
    }

    private static bool BeAValidAmount(decimal value) =>
        value is >= 0m and <= MaxPerKwh && Math.Round(value, 4) == value;
}
