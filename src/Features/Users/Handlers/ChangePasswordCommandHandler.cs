using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Domain.Common;
using Domain.Common.Entities;
using Features.Users.Commands;
using Features.Users.Logging;
using Infrastructure.Authentication;
using Microsoft.Extensions.Logging;

namespace Features.Users.Handlers;

public class ChangePasswordCommandHandler : ICommandHandler<ChangePasswordCommand, Result<ChangePasswordResponse>>
{
    private readonly IUserRepository<User> _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<ChangePasswordCommandHandler> _logger;

    public ChangePasswordCommandHandler(
        IUserRepository<User> userRepository,
        IPasswordHasher passwordHasher,
        ILogger<ChangePasswordCommandHandler> logger)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<Result<ChangePasswordResponse>> Handle(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        User? user = (await _userRepository.FindAsync(u => u.Id == command.Id, cancellationToken)).FirstOrDefault();

        // A non-Admin changing another account's password gets the same answer as for a missing account.
        if (user is not null && !command.Caller.IsAdmin && command.Caller.Id != command.Id)
        {
            LogMessages.UserAccessDenied(_logger, command.Id, command.Caller.Id);
            user = null;
        }

        if (user is null)
        {
            return Result.Failure<ChangePasswordResponse>(
                Error.NotFound("User.NotFound", $"User with ID {command.Id} was not found"));
        }

        if (!user.IsActive)
        {
            return Result.Failure<ChangePasswordResponse>(
                Error.Validation("User.Inactive", "User account is inactive"));
        }

        // Changing your own password requires the current one; an Admin resetting another account's does not
        // (they cannot know it).
        if (command.Caller.Id == command.Id)
        {
            if (string.IsNullOrEmpty(command.CurrentPassword))
            {
                return Result.Failure<ChangePasswordResponse>(
                    Error.Validation("User.CurrentPasswordRequired", "Current password is required"));
            }

            if (!_passwordHasher.VerifyPassword(command.CurrentPassword, user.PasswordHash))
            {
                return Result.Failure<ChangePasswordResponse>(
                    Error.Validation("User.InvalidPassword", "Current password is incorrect"));
            }
        }

        // Hash the new password using BCrypt
        user.PasswordHash = _passwordHasher.HashPassword(command.NewPassword);

        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(new ChangePasswordResponse(true));
    }
}
