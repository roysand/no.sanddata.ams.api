using FastEndpoints;
using Features.Measurements.Endpoints;
using FluentValidation;

namespace Features.Measurements.Validators;

public class GetMeasurementsValidator : Validator<GetMeasurementsRequest>
{
    public GetMeasurementsValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1).WithMessage("Page must be at least 1");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 2000).WithMessage("Page size must be between 1 and 2000");

        RuleFor(x => x)
            .Must(x => x.From is null || x.To is null || x.To >= x.From)
            .WithMessage("'to' must not be earlier than 'from'")
            .WithErrorCode("Validation.InvalidRange");
    }
}
