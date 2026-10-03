using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Domain.Common;
using Domain.Common.Entities;
using Domain.Common.ValueObjects;
using Features.Users.Commands;
using Features.Users.Logging;
using Microsoft.Extensions.Logging;

namespace Features.Users.Handlers;

public class UpdateUserCommandHandler : ICommandHandler<UpdateUserCommand, Result<UpdateUserResponse>>
{
    private readonly IUserRepository<User> _userRepository;
    private readonly IRoleRepository<Role> _roleRepository;
    private readonly IUserRoleRepository<UserRole> _userRoleRepository;
    private readonly ILogger<UpdateUserCommandHandler> _logger;

    public UpdateUserCommandHandler(
        IUserRepository<User> userRepository,
        IRoleRepository<Role> roleRepository,
        IUserRoleRepository<UserRole> userRoleRepository,
        ILogger<UpdateUserCommandHandler> logger)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _userRoleRepository = userRoleRepository;
        _logger = logger;
    }

    public async Task<Result<UpdateUserResponse>> Handle(UpdateUserCommand command, CancellationToken cancellationToken)
    {
        User? user = (await _userRepository.FindAsync(u => u.Id == command.Id, cancellationToken)).FirstOrDefault();

        // A non-Admin updating another account gets the same answer as for a missing account.
        if (user is not null && !command.Caller.IsAdmin && command.Caller.Id != command.Id)
        {
            LogMessages.UserAccessDenied(_logger, command.Id, command.Caller.Id);
            user = null;
        }

        if (user is null)
        {
            return Result.Failure<UpdateUserResponse>(
                Error.NotFound("User.NotFound", $"User with ID {command.Id} was not found"));
        }

        if (!command.Caller.IsAdmin && command.IsActive != user.IsActive)
        {
            return Result.Failure<UpdateUserResponse>(
                Error.Validation("User.IsActiveAdminOnly", "Only an administrator can activate or deactivate an account"));
        }

        if (user.IsActive && !command.IsActive && await AdminGuard.WouldLeaveNoActiveAdminAsync(
                user.Id, _userRepository, _roleRepository, _userRoleRepository, cancellationToken))
        {
            LogMessages.LastAdminProtected(_logger, user.Id, command.Caller.Id);
            return Result.Failure<UpdateUserResponse>(
                Error.Conflict("User.LastAdmin", "The last active administrator cannot be deactivated"));
        }

        // Check if email is being changed and if it's already taken by another user
        if (user.Email.Value != command.Email)
        {
            IEnumerable<User?> existingUsers = await _userRepository.FindAsync(
                u => u.Email.Value == command.Email && u.Id != command.Id,
                cancellationToken);

            if (existingUsers.Any())
            {
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
