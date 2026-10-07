using Domain.Common;
using FastEndpoints;
using Features.Locations.Endpoints;
using FluentValidation;

namespace Features.Locations.Validators;

public class UpdateLocationValidator : Validator<UpdateLocationRequest>
{
    public UpdateLocationValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Location ID is required");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required").MaximumLength(100);
        RuleFor(x => x.Address).NotEmpty().WithMessage("Address is required").MaximumLength(100);
        RuleFor(x => x.SerialNumber).NotEmpty().WithMessage("Serial number is required").MaximumLength(100);
        RuleFor(x => x.Zone)
            .Must(PriceZones.IsValid)
            .WithMessage($"Zone must be one of {string.Join(", ", PriceZones.All)}")
            .WithErrorCode("Validation.InvalidZone");
    }
}

public class SetLocationKeyActiveValidator : Validator<SetLocationKeyActiveRequest>
{
    public SetLocationKeyActiveValidator() =>
        RuleFor(x => x.Id).NotEmpty().WithMessage("Location ID is required");
}
