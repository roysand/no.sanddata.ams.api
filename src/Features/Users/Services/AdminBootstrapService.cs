using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Features.Users.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Features.Users.Services;

/// <summary>
/// On startup, if no user holds the Admin role, promotes the account named by Bootstrap:OwnerEmail.
/// Does nothing once any Admin exists, so the setting cannot be used to take over a running system.
/// </summary>
public class AdminBootstrapService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AdminBootstrapService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await PromoteOwnerAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Startup must not fail because of this (e.g. database not reachable yet); it retries next start.
            LogMessages.AdminBootstrapFailed(logger, ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task PromoteOwnerAsync(CancellationToken ct)
    {
        string? ownerEmail = configuration["Bootstrap:OwnerEmail"];
        if (string.IsNullOrWhiteSpace(ownerEmail))
        {
            return;
        }

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IServiceProvider sp = scope.ServiceProvider;
        var users = sp.GetRequiredService<IUserRepository<User>>();
        var roles = sp.GetRequiredService<IRoleRepository<Role>>();
        var userRoles = sp.GetRequiredService<IUserRoleRepository<UserRole>>();

        Role[] allRoles = (await roles.AllAsync(ct)).OfType<Role>().ToArray();
        Role? adminRole = allRoles.FirstOrDefault(r => r.Name == RoleNames.Admin);
        Role? userRole = allRoles.FirstOrDefault(r => r.Name == RoleNames.User);
        if (adminRole is null || userRole is null)
        {
            return; // migration not applied yet
        }

        if (await userRoles.ExistsAsync(ur => ur.RoleId == adminRole.Id, ct))
        {
            return;
        }

        User? owner = (await users.FindAsync(u => u.Email.Value == ownerEmail, ct, noTrack: true)).FirstOrDefault();
        if (owner is null)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        userRoles.Insert(new UserRole(owner.Id, adminRole.Id, now));
        if (!await userRoles.ExistsAsync(ur => ur.UserId == owner.Id && ur.RoleId == userRole.Id, ct))
        {
            userRoles.Insert(new UserRole(owner.Id, userRole.Id, now));
        }

        await userRoles.SaveChangesAsync(ct);
        LogMessages.AdminBootstrapped(logger, ownerEmail);
    }
}
