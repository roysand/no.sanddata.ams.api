using FastEndpoints;
using Features.Auth.Endpoints;
using FluentValidation;

namespace Features.Auth.Validators;

public class RefreshTokenValidator : Validator<RefreshTokenRequest>
{
    public RefreshTokenValidator() =>
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("Refresh token is required");
}
