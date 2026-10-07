using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Domain.Common;
using Domain.Common.Entities;
using Features.Locations.Commands;
using Features.Locations.Logging;
using Features.Locations.Mappers;
using Features.Locations.Queries;
using Microsoft.Extensions.Logging;

namespace Features.Locations.Handlers;

public class SetLocationKeyActiveCommandHandler(
    IApiKeyRepository<ApiKey> apiKeyRepository,
    ILogger<SetLocationKeyActiveCommandHandler> logger)
    : ICommandHandler<SetLocationKeyActiveCommand, Result<ApiKeyInfoResponse>>
{
    public async Task<Result<ApiKeyInfoResponse>> Handle(SetLocationKeyActiveCommand command, CancellationToken ct)
    {
        ApiKey? apiKey = await apiKeyRepository.FindByLocationIdAsync(command.LocationId, ct);
        if (apiKey is null)
        {
            return Result.Failure<ApiKeyInfoResponse>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        if (apiKey.IsActive != command.IsActive)
        {
            apiKey.SetActive(command.IsActive);
            await apiKeyRepository.SaveChangesAsync(ct);
            LogMessages.KeyActiveChanged(logger, command.LocationId, command.IsActive, command.ActingUserId);
        }

        return Result.Success(LocationMapper.ToKeyInfo(apiKey, DateTime.UtcNow));
    }
}
