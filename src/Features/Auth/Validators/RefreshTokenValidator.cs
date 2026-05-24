using FastEndpoints;
using Features.Auth.Commands;
using FluentValidation;

namespace Features.Auth.Validators;

public class RefreshTokenValidator : Validator<RefreshTokenCommand>
{
    public RefreshTokenValidator() =>
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("Refresh token is required");
}
