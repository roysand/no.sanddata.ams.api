using Domain.Common;
using FastEndpoints;
using Features.Users.Endpoints;
using FluentValidation;

namespace Features.Users.Validators;

public class LinkUserLocationValidator : Validator<LinkUserLocationRequest>
{
    public LinkUserLocationValidator() =>
        // Compare against the names only: Enum.TryParse would also accept "1" and "Owner,Viewer".
        RuleFor(x => x.Role)
            .Must(role => Enum.GetNames<LocationRole>().Contains(role!.Trim(), StringComparer.OrdinalIgnoreCase))
            .When(x => !string.IsNullOrWhiteSpace(x.Role))
            .WithMessage("Role must be Owner or Viewer");
}
