using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Domain.Common;
using Domain.Common.Entities;
using Features.Users.Commands;
using Features.Users.Logging;
using Microsoft.Extensions.Logging;

namespace Features.Users.Handlers;

public class GrantAdminCommandHandler(
    IUserRepository<User> userRepository,
    IRoleRepository<Role> roleRepository,
    IUserRoleRepository<UserRole> userRoleRepository,
    ILogger<GrantAdminCommandHandler> logger) : ICommandHandler<GrantAdminCommand, Result<AdminRoleChangeResponse>>
{
    public async Task<Result<AdminRoleChangeResponse>> Handle(GrantAdminCommand command, CancellationToken ct)
    {
        if (!await userRepository.ExistsAsync(u => u.Id == command.UserId, ct))
        {
            return Result.Failure<AdminRoleChangeResponse>(
                Error.NotFound("User.NotFound", $"User with ID {command.UserId} was not found"));
        }

        Role? adminRole = (await roleRepository.FindAsync(r => r.Name == RoleNames.Admin, ct)).FirstOrDefault();
        if (adminRole is null)
        {
            return Result.Failure<AdminRoleChangeResponse>(
                Error.Problem("Role.AdminRoleMissing", "The Admin role does not exist; has the database migration been applied?"));
        }

        if (await userRoleRepository.ExistsAsync(ur => ur.UserId == command.UserId && ur.RoleId == adminRole.Id, ct))
        {
            return Result.Success(new AdminRoleChangeResponse(false));
        }

        userRoleRepository.Insert(new UserRole(command.UserId, adminRole.Id, DateTime.UtcNow));
        await userRoleRepository.SaveChangesAsync(ct);

        LogMessages.AdminGranted(logger, command.UserId, command.Caller.Id);
        return Result.Success(new AdminRoleChangeResponse(true));
    }
}
