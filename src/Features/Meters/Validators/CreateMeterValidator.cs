using FastEndpoints;
using Features.Meters.Endpoints;
using FluentValidation;

namespace Features.Meters.Validators;

public class CreateMeterValidator : Validator<CreateMeterRequest>
{
    public CreateMeterValidator()
    {
        RuleFor(x => x.LocationId)
            .NotEmpty().WithMessage("Location id is required");

        RuleFor(x => x.DeviceId)
            .NotEmpty().WithMessage("Device id is required")
            .MaximumLength(100).WithMessage("Device id must not exceed 100 characters");

        RuleFor(x => x.Comment)
            .MaximumLength(200).WithMessage("Comment must not exceed 200 characters");
    }
}
