using FastEndpoints;
using Features.Locations.Endpoints;
using FluentValidation;

namespace Features.Locations.Validators;

public class UpdateOwnLocationValidator : Validator<UpdateOwnLocationRequest>
{
    public UpdateOwnLocationValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Location ID is required");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required").MaximumLength(100);
        RuleFor(x => x.Address).NotEmpty().WithMessage("Address is required").MaximumLength(100);
        // A missing flag must not silently switch the location off.
        RuleFor(x => x.IsActive).NotNull().WithMessage("The active flag is required");
    }
}
