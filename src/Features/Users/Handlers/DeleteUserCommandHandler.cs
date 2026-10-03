using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Domain.Common;
using Domain.Common.Entities;
using Features.Users.Commands;
using Features.Users.Logging;
using Microsoft.Extensions.Logging;

namespace Features.Users.Handlers;

public class DeleteUserCommandHandler : ICommandHandler<DeleteUserCommand, Result<DeleteUserResponse>>
{
    private readonly IUserRepository<User> _userRepository;
    private readonly IRoleRepository<Role> _roleRepository;
    private readonly IUserRoleRepository<UserRole> _userRoleRepository;
    private readonly ILogger<DeleteUserCommandHandler> _logger;

    public DeleteUserCommandHandler(
        IUserRepository<User> userRepository,
        IRoleRepository<Role> roleRepository,
        IUserRoleRepository<UserRole> userRoleRepository,
        ILogger<DeleteUserCommandHandler> logger)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _userRoleRepository = userRoleRepository;
        _logger = logger;
    }

    public async Task<Result<DeleteUserResponse>> Handle(DeleteUserCommand command, CancellationToken cancellationToken)
    {
        User? user = await _userRepository.GetByIdAsync(command.Id, cancellationToken);

        if (user is null)
        {
            return Result.Failure<DeleteUserResponse>(
                Error.NotFound("User.NotFound", $"User with ID {command.Id} was not found"));
        }

        if (await AdminGuard.WouldLeaveNoActiveAdminAsync(
                user.Id, _userRepository, _roleRepository, _userRoleRepository, cancellationToken))
        {
            LogMessages.LastAdminProtected(_logger, user.Id, command.Caller.Id);
            return Result.Failure<DeleteUserResponse>(
                Error.Conflict("User.LastAdmin", "The last active administrator cannot be deleted"));
        }

        _userRepository.Delete(user);
        await _userRepository.SaveChangesAsync(cancellationToken);

        LogMessages.UserDeleted(_logger, command.Id, command.Caller.Id);

        return Result.Success(new DeleteUserResponse(true));
    }
}
