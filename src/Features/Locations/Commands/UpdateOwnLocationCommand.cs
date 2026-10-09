using Application.CQRS;
using Domain.Common;
using Features.Locations.Queries;

namespace Features.Locations.Commands;

/// <summary>
/// An owner editing their own location. Deliberately has no serial number, zone, Norgespris or key member:
/// those stay administrator-only, and they cannot be sent through this command at all.
/// </summary>
public record UpdateOwnLocationCommand(
    Guid ActingUserId,
    Guid LocationId,
    string Name,
    string Address,
    bool IsActive) : ICommand<Result<LocationSummaryResponse>>;
