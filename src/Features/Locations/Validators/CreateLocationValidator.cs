using Domain.Common;
using FastEndpoints;
using Features.Locations.Endpoints;
using FluentValidation;

namespace Features.Locations.Validators;

public class CreateLocationValidator : Validator<CreateLocationRequest>
{
    public CreateLocationValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required").MaximumLength(100);
        RuleFor(x => x.Address).NotEmpty().WithMessage("Address is required").MaximumLength(100);
        RuleFor(x => x.SerialNumber).NotEmpty().WithMessage("Serial number is required").MaximumLength(100);
        RuleFor(x => x.Zone)
            .Must(PriceZones.IsValid)
            .WithMessage($"Zone must be one of {string.Join(", ", PriceZones.All)}")
            .WithErrorCode("Validation.InvalidZone");
    }
}
