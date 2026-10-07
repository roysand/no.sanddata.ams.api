using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Domain.Common.ValueObjects;
using Features.Users.Logging;
using Infrastructure.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Features.Users.Services;

/// <summary>
/// On startup, if no user holds the Admin role, promotes the account named by Bootstrap:OwnerEmail -
/// creating it first, from Bootstrap:OwnerPassword, if it doesn't exist yet. Does nothing once any Admin
/// exists, so neither setting can be used to take over a running system.
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
        IUserRepository<User> users = sp.GetRequiredService<IUserRepository<User>>();
        IRoleRepository<Role> roles = sp.GetRequiredService<IRoleRepository<Role>>();
        IUserRoleRepository<UserRole> userRoles = sp.GetRequiredService<IUserRoleRepository<UserRole>>();

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
            owner = await TryCreateOwnerAsync(ownerEmail, users, sp, ct);
            if (owner is null)
            {
                return;
            }
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

    // Only runs when the owner email has no User row yet. A configured OwnerPassword is required, both
    // because an account needs a password hash and so the setting can't create an account without one.
    private async Task<User?> TryCreateOwnerAsync(
        string ownerEmail, IUserRepository<User> users, IServiceProvider sp, CancellationToken ct)
    {
        string? ownerPassword = configuration["Bootstrap:OwnerPassword"];
        if (string.IsNullOrWhiteSpace(ownerPassword))
        {
            LogMessages.AdminBootstrapOwnerMissing(logger, ownerEmail);
            return null;
        }

        Result<EmailAddress> emailResult = EmailAddress.Create(ownerEmail);
        if (emailResult.IsFailure)
        {
            LogMessages.AdminBootstrapOwnerMissing(logger, ownerEmail);
            return null;
        }

        IPasswordHasher passwordHasher = sp.GetRequiredService<IPasswordHasher>();
        var owner = new User(
            Guid.NewGuid(), "Owner", "Account", passwordHasher.HashPassword(ownerPassword), emailResult.Value, true);

        users.Insert(owner);
        await users.SaveChangesAsync(ct);
        LogMessages.AdminBootstrapOwnerCreated(logger, ownerEmail);
        return owner;
    }
}
