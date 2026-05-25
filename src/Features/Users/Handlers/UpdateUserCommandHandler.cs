using Application.CQRS;
using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Domain.Common.ValueObjects;
using Features.Users.Commands;
using Microsoft.Extensions.Logging;
using Features.Users.Logging;
using Infrastructure.Logging;

namespace Features.Users.Handlers;

public class UpdateUserCommandHandler : ICommandHandler<UpdateUserCommand, Result<UpdateUserResponse>>
{
    private readonly IUserEfRepository<User> _userRepository;
    private readonly ILogger<UpdateUserCommandHandler> _logger;

    public UpdateUserCommandHandler(IUserEfRepository<User> userRepository, ILogger<UpdateUserCommandHandler> logger) => (_userRepository, _logger) = (userRepository, logger);

    public async Task<Result<UpdateUserResponse>> Handle(UpdateUserCommand command, CancellationToken cancellationToken)
    {
        User? user = await _userRepository.GetByIdAsync(command.Id, cancellationToken);

        if (user is null)
        {
            LogMessages.UserNotFound(_logger, command.Id);

            return Result.Failure<UpdateUserResponse>(
                Error.NotFound("User.NotFound", $"User with ID {command.Id} was not found"));
        }

        // Check if email is being changed and if it's already taken by another user
        if (user.Email.Value != command.Email)
        {
            IEnumerable<User?> existingUsers = await _userRepository.FindAsync(
                u => u.Email.Value == command.Email && u.Id != command.Id,
                cancellationToken);

            if (existingUsers.Any())
            {
                // Log update failure due to email conflict
                LogMessages.UserUpdateFailed(_logger, command.Email, FailureReasons.EmailAlreadyExists, FailureReasons.EmailAlreadyExistsCode);

                return Result.Failure<UpdateUserResponse>(
                    Error.Conflict("User.EmailExists", "A user with this email already exists"));
            }

            // Create new email value object using factory pattern
            Result<EmailAddress> emailResult = EmailAddress.Create(command.Email);
            if (emailResult.IsFailure)
            {
                return Result.Failure<UpdateUserResponse>(emailResult.Error);
            }

            user.Email = emailResult.Value;
        }

        // Update user properties
        user.FirstName = command.FirstName;
        user.LastName = command.LastName;
        user.IsActive = command.IsActive;

        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync(cancellationToken);

        var response = new UpdateUserResponse(
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email.Value,
            user.IsActive,
            user.Roles.Select(r => r.Name).ToArray(),
            user.Locations.Select(l => l.Name).ToArray()
        );

        return Result.Success(response);
    }
}
