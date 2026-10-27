using FastEndpoints;
using Features.Meters.Endpoints;
using FluentValidation;

namespace Features.Meters.Validators;

public class UpdateMeterCommentValidator : Validator<UpdateMeterCommentRequest>
{
    public UpdateMeterCommentValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Meter id is required");

        RuleFor(x => x.Comment)
            .MaximumLength(200).WithMessage("Comment must not exceed 200 characters");
    }
}
