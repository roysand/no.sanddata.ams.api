using Domain.Common;
using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using FastEndpoints;
using FluentValidation;
using Infrastructure.Authentication;
using MediatR;

namespace Features.Users;

public static class ChangePassword
{
    public record ChangePasswordRequest(
        Guid Id,
        string CurrentPassword,
        string NewPassword
    ) : IRequest<Result<bool>>;

    internal sealed class Handler : IRequestHandler<ChangePasswordRequest, Result<bool>>
    {
        private readonly IUserEfRepository<User> _userRepository;
        private readonly IPasswordHasher _passwordHasher;

        public Handler(IUserEfRepository<User> userRepository, IPasswordHasher passwordHasher)
        {
            _userRepository = userRepository;
            _passwordHasher = passwordHasher;
        }

        public async Task<Result<bool>> Handle(
            ChangePasswordRequest request,
            CancellationToken cancellationToken)
        {
            User? user = await _userRepository.GetByIdAsync(request.Id, cancellationToken);

            if (user is null)
            {
                return Result.Failure<bool>(
                    Error.NotFound("User.NotFound", $"User with ID {request.Id} was not found"));
            }

            if (!user.IsActive)
            {
                return Result.Failure<bool>(
                    Error.Validation("User.Inactive", "User account is inactive"));
            }

            // Verify current password using BCrypt
            if (!_passwordHasher.VerifyPassword(request.CurrentPassword, user.PasswordHash))
            {
                return Result.Failure<bool>(
                    Error.Validation("User.InvalidPassword", "Current password is incorrect"));
            }

            // Hash the new password using BCrypt
            user.PasswordHash = _passwordHasher.HashPassword(request.NewPassword);

            _userRepository.Update(user);
            await _userRepository.SaveChangesAsync(cancellationToken);

            return Result.Success(true);
        }
    }

    public class ChangePasswordValidator : Validator<ChangePasswordRequest>
    {
        public ChangePasswordValidator()
        {
            RuleFor(x => x.Id)
                .NotEmpty().WithMessage("User ID is required");

            RuleFor(x => x.CurrentPassword)
                .NotEmpty().WithMessage("Current password is required");

            RuleFor(x => x.NewPassword)
                .NotEmpty().WithMessage("New password is required")
                .MinimumLength(8).WithMessage("New password must be at least 8 characters")
                .Matches(@"[A-Z]").WithMessage("New password must contain at least one uppercase letter")
                .Matches(@"[a-z]").WithMessage("New password must contain at least one lowercase letter")
                .Matches(@"[0-9]").WithMessage("New password must contain at least one number")
                .Matches(@"[\W_]").WithMessage("New password must contain at least one special character")
                .NotEqual(x => x.CurrentPassword).WithMessage("New password must be different from current password");
        }
    }
}

public class ChangePasswordEndpoint : Endpoint<ChangePassword.ChangePasswordRequest>
{
    private readonly ISender _sender;

    public ChangePasswordEndpoint(ISender sender) => _sender = sender;

    public override void Configure()
    {
        Put("/api/users/{id}/password");
        AllowAnonymous(); // TODO: Add authorization - users can only change their own password
        // Policies(new[] { "SelfOnly" });
        Summary(s =>
        {
            s.Summary = "Change user password";
            s.Description = "Allow users to change their password by providing current and new password";
            s.ExampleRequest = new ChangePassword.ChangePasswordRequest(
                Guid.NewGuid(),
                "OldPassword123!",
                "NewSecurePass456!"
            );
            s.Response(200, "Password changed successfully");
            s.Response(404, "User not found");
            s.Response(400, "Invalid request or current password incorrect");
        });
    }

    public override async Task HandleAsync(
        ChangePassword.ChangePasswordRequest req,
        CancellationToken ct)
    {
        Result<bool> result = await _sender.Send(req, ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Code, result.Error.Description);
            ThrowIfAnyErrors(result.Error.Type switch
            {
                ErrorType.NotFound => 404,
                ErrorType.Validation => 400,
                _ => 400
            });
        }

        // Success - FastEndpoints will automatically send 200 OK
    }
}
