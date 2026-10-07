using Application.CQRS;
using Domain.Common;
using Features.Locations.Queries;

namespace Features.Locations.Commands;

public record RotateLocationKeyCommand(Guid ActingUserId, Guid LocationId) : ICommand<Result<RotatedKeyResponse>>;

/// <summary>The new key, shown only in this response. The previous key has already stopped working.</summary>
public record RotatedKeyResponse(string ApiKey, string Hint, DateTime ExpiresAt);

public record SetLocationKeyActiveCommand(Guid ActingUserId, Guid LocationId, bool IsActive)
    : ICommand<Result<ApiKeyInfoResponse>>;
