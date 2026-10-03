using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;

namespace Features.Users.Handlers;

/// <summary>Keeps the system from losing its last active Admin (revoke, delete, deactivate).</summary>
internal static class AdminGuard
{
    public static async Task<bool> WouldLeaveNoActiveAdminAsync(
        Guid userId,
        IUserRepository<User> users,
        IRoleRepository<Role> roles,
        IUserRoleRepository<UserRole> userRoles,
        CancellationToken ct)
    {
        Role? adminRole = (await roles.FindAsync(r => r.Name == RoleNames.Admin, ct)).FirstOrDefault();
        if (adminRole is null)
        {
            return false;
        }

        var adminIds = (await userRoles.FindAsync(ur => ur.RoleId == adminRole.Id, ct, noTrack: true))
            .OfType<UserRole>().Select(ur => ur.UserId).ToList();

        // Only an active Admin counts; removing anyone else cannot reduce the active-Admin count.
        User? target = (await users.FindAsync(u => u.Id == userId, ct, noTrack: true)).FirstOrDefault();
        if (target is null || !target.IsActive || !adminIds.Contains(userId))
        {
            return false;
        }

        bool anotherActiveAdmin = (await users.FindAsync(
            u => u.IsActive && u.Id != userId && adminIds.Contains(u.Id), ct, noTrack: true)).Any();

        return !anotherActiveAdmin;
    }
}
