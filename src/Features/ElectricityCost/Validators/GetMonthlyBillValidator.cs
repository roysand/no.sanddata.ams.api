using System.Globalization;
using FastEndpoints;
using Features.ElectricityCost.Endpoints;
using FluentValidation;

namespace Features.ElectricityCost.Validators;

public class GetMonthlyBillValidator : Validator<GetMonthlyBillRequest>
{
    public GetMonthlyBillValidator()
    {
        RuleFor(x => x.LocationId)
            .NotEmpty()
            .WithMessage("A location is required");

        RuleFor(x => x.Month)
            .Must(month => month is null || DateOnly.TryParseExact(
                month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            .WithMessage("Month must be yyyy-MM")
            .WithErrorCode("Validation.InvalidMonth");
    }
}
