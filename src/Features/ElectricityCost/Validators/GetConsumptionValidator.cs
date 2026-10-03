using FastEndpoints;
using Features.ElectricityCost.Endpoints;
using FluentValidation;

namespace Features.ElectricityCost.Validators;

public class GetConsumptionValidator : Validator<GetConsumptionRequest>
{
    public GetConsumptionValidator()
    {
        RuleFor(x => x.Granularity)
            .Must(g => g is "minute" or "hour")
            .WithMessage("Granularity must be 'minute' or 'hour'");

        RuleFor(x => x)
            .Must(x => x.From is null || x.To is null || x.To >= x.From)
            .WithMessage("'to' must not be earlier than 'from'")
            .WithErrorCode("Validation.InvalidRange");
    }
}
