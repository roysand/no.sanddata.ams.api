using Application.CQRS;
using Domain.Common;

namespace Features.Users.Commands;

public record GrantAdminCommand(Guid UserId, Caller Caller) : ICommand<Result<AdminRoleChangeResponse>>;

public record RevokeAdminCommand(Guid UserId, Caller Caller) : ICommand<Result<AdminRoleChangeResponse>>;

/// <summary>Changed is false when the user already was (or was not) an Admin; both calls are idempotent.</summary>
public record AdminRoleChangeResponse(bool Changed);
