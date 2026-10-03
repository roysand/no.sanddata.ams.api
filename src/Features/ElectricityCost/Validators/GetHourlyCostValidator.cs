using FastEndpoints;
using Features.ElectricityCost.Endpoints;
using FluentValidation;

namespace Features.ElectricityCost.Validators;

public class GetHourlyCostValidator : Validator<GetHourlyCostRequest>
{
    public GetHourlyCostValidator()
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
