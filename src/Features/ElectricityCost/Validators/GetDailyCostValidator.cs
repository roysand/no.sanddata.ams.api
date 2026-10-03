using FastEndpoints;
using Features.ElectricityCost.Endpoints;
using FluentValidation;

namespace Features.ElectricityCost.Validators;

public class GetDailyCostValidator : Validator<GetDailyCostRequest>
{
    public GetDailyCostValidator() =>
        RuleFor(x => x)
            .Must(x => x.From is null || x.To is null || x.To >= x.From)
            .WithMessage("'to' must not be earlier than 'from'")
            .WithErrorCode("Validation.InvalidRange");
}
