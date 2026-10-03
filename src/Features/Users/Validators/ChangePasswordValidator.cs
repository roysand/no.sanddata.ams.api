using FastEndpoints;
using Features.Users.Endpoints;
using FluentValidation;

namespace Features.Users.Validators;

public class ChangePasswordValidator : Validator<ChangePasswordRequest>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("User ID is required");

        // CurrentPassword is optional here: an administrator resetting another account omits it.
        // The handler requires it when the caller changes their own password.

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("New password is required")
            .MinimumLength(8).WithMessage("New password must be at least 8 characters")
            .Matches(@"[A-Z]").WithMessage("New password must contain at least one uppercase letter")
            .Matches(@"[a-z]").WithMessage("New password must contain at least one lowercase letter")
            .Matches(@"[0-9]").WithMessage("New password must contain at least one number")
            .Matches(@"[\W_]").WithMessage("New password must contain at least one special character");

        RuleFor(x => x.NewPassword)
            .NotEqual(x => x.CurrentPassword).WithMessage("New password must be different from current password")
            .When(x => !string.IsNullOrEmpty(x.CurrentPassword));
    }
}
