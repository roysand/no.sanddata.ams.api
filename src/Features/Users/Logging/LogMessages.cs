using Domain.Common;
using Microsoft.Extensions.Logging;

namespace Features.Users.Logging;

internal static class LogMessages
{
    // Users: event ids 1000 - 1099
    private static readonly Action<ILogger, Guid, string, Exception?> _userCreated =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Information,
            new EventId(1000, nameof(UserCreated)),
            "User created: {UserId} {Email}");

    private static readonly Action<ILogger, Guid, Guid, Exception?> _userDeleted =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Information,
            new EventId(1001, nameof(UserDeleted)),
            "User deleted: {UserId} by {CallerId}");

    private static readonly Action<ILogger, Guid, string, Exception?> _userUpdated =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Information,
            new EventId(1002, nameof(UserUpdated)),
            "User updated: {UserId} {Changes}");

    // User creation/update failures
    private static readonly Action<ILogger, string, string, Exception?> _userCreateFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(1003, nameof(UserCreated)),
            "User creation failed for: {Email} Reason: {Reason}");

    // Roles and access (reason codes are stable: UserNotFound, NotOwnerOrAdmin, LastAdmin)
    private static readonly Action<ILogger, Guid, Guid, Exception?> _adminGranted =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Information,
            new EventId(1010, nameof(AdminGranted)),
            "Admin role granted to {UserId} by {CallerId}");

    private static readonly Action<ILogger, Guid, Guid, Exception?> _adminRevoked =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Information,
            new EventId(1011, nameof(AdminRevoked)),
            "Admin role revoked from {UserId} by {CallerId}");

    private static readonly Action<ILogger, Guid, Guid, Guid, Exception?> _userLocationLinked =
        LoggerMessage.Define<Guid, Guid, Guid>(
            LogLevel.Information,
            new EventId(1012, nameof(UserLocationLinked)),
            "User {UserId} linked to location {LocationId} by {CallerId}");

    private static readonly Action<ILogger, Guid, Guid, Guid, Exception?> _userLocationUnlinked =
        LoggerMessage.Define<Guid, Guid, Guid>(
            LogLevel.Information,
            new EventId(1013, nameof(UserLocationUnlinked)),
            "User {UserId} unlinked from location {LocationId} by {CallerId}");

    private static readonly Action<ILogger, Guid, Guid, LocationRole, Guid, Exception?> _userLocationRoleChanged =
        LoggerMessage.Define<Guid, Guid, LocationRole, Guid>(
            LogLevel.Information,
            new EventId(1020, nameof(UserLocationRoleChanged)),
            "User {UserId} is now {Role} at location {LocationId}, changed by {CallerId}");

    private static readonly Action<ILogger, string, Exception?> _adminBootstrapped =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1014, nameof(AdminBootstrapped)),
            "No Admin existed; owner {Email} was granted the Admin role");

    private static readonly Action<ILogger, Guid, Guid, Exception?> _lastAdminProtected =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Warning,
            new EventId(1015, nameof(LastAdminProtected)),
            "Refused: {CallerId} tried to remove the last active Admin {UserId} (reason: LastAdmin)");

    private static readonly Action<ILogger, Guid, Guid, Exception?> _userAccessDenied =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Warning,
            new EventId(1016, nameof(UserAccessDenied)),
            "Refused: {CallerId} tried to access user {UserId} (reason: NotOwnerOrAdmin)");

    private static readonly Action<ILogger, Exception?> _adminBootstrapFailed =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1017, nameof(AdminBootstrapFailed)),
            "Admin bootstrap failed; it will be retried at the next start (reason: BootstrapFailed)");

    private static readonly Action<ILogger, string, Exception?> _adminBootstrapOwnerMissing =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1018, nameof(AdminBootstrapOwnerMissing)),
            "No Admin exists and owner {Email} has no account; set Bootstrap:OwnerPassword to create it (reason: OwnerMissing)");

    private static readonly Action<ILogger, string, Exception?> _adminBootstrapOwnerCreated =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1019, nameof(AdminBootstrapOwnerCreated)),
            "Owner account {Email} did not exist; created it from Bootstrap:OwnerPassword");

    public static void AdminBootstrapFailed(ILogger logger, Exception exception)
        => _adminBootstrapFailed(logger, exception);

    public static void AdminBootstrapOwnerMissing(ILogger logger, string email)
        => _adminBootstrapOwnerMissing(logger, email, null);

    public static void AdminBootstrapOwnerCreated(ILogger logger, string email)
        => _adminBootstrapOwnerCreated(logger, email, null);

    public static void AdminGranted(ILogger logger, Guid userId, Guid callerId)
        => _adminGranted(logger, userId, callerId, null);

    public static void AdminRevoked(ILogger logger, Guid userId, Guid callerId)
        => _adminRevoked(logger, userId, callerId, null);

    public static void UserLocationLinked(ILogger logger, Guid userId, Guid locationId, Guid callerId)
        => _userLocationLinked(logger, userId, locationId, callerId, null);

    public static void UserLocationUnlinked(ILogger logger, Guid userId, Guid locationId, Guid callerId)
        => _userLocationUnlinked(logger, userId, locationId, callerId, null);

    public static void UserLocationRoleChanged(
        ILogger logger, Guid userId, Guid locationId, LocationRole role, Guid callerId)
        => _userLocationRoleChanged(logger, userId, locationId, role, callerId, null);

    public static void AdminBootstrapped(ILogger logger, string email)
        => _adminBootstrapped(logger, email, null);

    public static void LastAdminProtected(ILogger logger, Guid userId, Guid callerId)
        => _lastAdminProtected(logger, callerId, userId, null);

    public static void UserAccessDenied(ILogger logger, Guid userId, Guid callerId)
        => _userAccessDenied(logger, callerId, userId, null);

    public static void UserCreated(ILogger logger, Guid userId, string email)
        => _userCreated(logger, userId, email, null);

    public static void UserDeleted(ILogger logger, Guid userId, Guid callerId)
        => _userDeleted(logger, userId, callerId, null);

    public static void UserUpdated(ILogger logger, Guid userId, string changes)
        => _userUpdated(logger, userId, changes, null);

    public static void UserCreateFailed(ILogger logger, string email, string reason)
        => _userCreateFailed(logger, email, reason, null);
}
