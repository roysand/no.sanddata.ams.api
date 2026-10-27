using Application.CQRS;
using Domain.Common;

namespace Features.Users.Commands;

/// <param name="Role">The role the link should have; a link made without stating one is an owner link.</param>
public record LinkUserLocationCommand(Guid UserId, Guid LocationId, Caller Caller, LocationRole Role = LocationRole.Owner)
    : ICommand<Result<UserLocationChangeResponse>>;

public record UnlinkUserLocationCommand(Guid UserId, Guid LocationId, Caller Caller)
    : ICommand<Result<UserLocationChangeResponse>>;

/// <summary>Changed is false when the link already existed (link) or did not exist (unlink); both are idempotent.</summary>
public record UserLocationChangeResponse(bool Changed);
