using Application.Common.ApiKeys;
using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Domain.Common;
using Domain.Common.Entities;
using Features.Locations.Commands;
using Features.Locations.Logging;
using Microsoft.Extensions.Logging;

namespace Features.Locations.Handlers;

public class RotateLocationKeyCommandHandler(
    IApiKeyRepository<ApiKey> apiKeyRepository,
    ILogger<RotateLocationKeyCommandHandler> logger)
    : ICommandHandler<RotateLocationKeyCommand, Result<RotatedKeyResponse>>
{
    public async Task<Result<RotatedKeyResponse>> Handle(RotateLocationKeyCommand command, CancellationToken ct)
    {
        ApiKey? apiKey = await apiKeyRepository.FindByLocationIdAsync(command.LocationId, ct);
        if (apiKey is null)
        {
            return Result.Failure<RotatedKeyResponse>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        ApiKeyCrypto.GeneratedKey generated = ApiKeyCrypto.Generate();
        DateTime expiresAt = DateTime.UtcNow + ApiKeyCrypto.KeyLifetime;

        // The old fingerprint is overwritten in the same save, so the previous key stops working immediately.
        apiKey.Rotate(generated.Hash, generated.Hint, expiresAt);
        await apiKeyRepository.SaveChangesAsync(ct);

        LogMessages.KeyRotated(logger, command.LocationId, command.ActingUserId);

        return Result.Success(new RotatedKeyResponse(generated.Key, generated.Hint, expiresAt));
    }
}
