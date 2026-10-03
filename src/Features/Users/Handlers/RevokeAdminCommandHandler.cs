using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Domain.Common;
using Domain.Common.Entities;
using Features.Users.Commands;
using Features.Users.Logging;
using Microsoft.Extensions.Logging;

namespace Features.Users.Handlers;

public class RevokeAdminCommandHandler(
    IUserRepository<User> userRepository,
    IRoleRepository<Role> roleRepository,
    IUserRoleRepository<UserRole> userRoleRepository,
    ILogger<RevokeAdminCommandHandler> logger) : ICommandHandler<RevokeAdminCommand, Result<AdminRoleChangeResponse>>
{
    public async Task<Result<AdminRoleChangeResponse>> Handle(RevokeAdminCommand command, CancellationToken ct)
    {
        if (!await userRepository.ExistsAsync(u => u.Id == command.UserId, ct))
        {
            return Result.Failure<AdminRoleChangeResponse>(
                Error.NotFound("User.NotFound", $"User with ID {command.UserId} was not found"));
        }

        Role? adminRole = (await roleRepository.FindAsync(r => r.Name == RoleNames.Admin, ct)).FirstOrDefault();
        UserRole? held = adminRole is null
            ? null
            : (await userRoleRepository.FindAsync(ur => ur.UserId == command.UserId && ur.RoleId == adminRole.Id, ct))
                .FirstOrDefault();
        if (held is null)
        {
            return Result.Success(new AdminRoleChangeResponse(false));
        }

        if (await AdminGuard.WouldLeaveNoActiveAdminAsync(command.UserId, userRepository, roleRepository, userRoleRepository, ct))
        {
            LogMessages.LastAdminProtected(logger, command.UserId, command.Caller.Id);
            return Result.Failure<AdminRoleChangeResponse>(
                Error.Conflict("User.LastAdmin", "The last active administrator cannot lose the Admin role"));
        }

        userRoleRepository.Delete(held);
        await userRoleRepository.SaveChangesAsync(ct);

        LogMessages.AdminRevoked(logger, command.UserId, command.Caller.Id);
        return Result.Success(new AdminRoleChangeResponse(true));
    }
}
