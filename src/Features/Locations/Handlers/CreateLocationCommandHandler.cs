using Application.Common.ApiKeys;
using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Domain.Common;
using Domain.Common.Entities;
using Features.Locations.Commands;
using Features.Locations.Logging;
using Features.Locations.Mappers;
using Microsoft.Extensions.Logging;

namespace Features.Locations.Handlers;

public class CreateLocationCommandHandler(
    ILocationRepository<Location> locationRepository,
    IUserLocationRepository<UserLocation> userLocationRepository,
    ILogger<CreateLocationCommandHandler> logger)
    : ICommandHandler<CreateLocationCommand, Result<CreatedLocationResponse>>
{
    private const int DescriptionMaxLength = 100;

    public async Task<Result<CreatedLocationResponse>> Handle(CreateLocationCommand command, CancellationToken ct)
    {
        if (await locationRepository.SerialNumberExistsAsync(command.SerialNumber, null, ct))
        {
            return Result.Failure<CreatedLocationResponse>(Error.Conflict(
                "Location.SerialNumberExists", "Another location already uses this serial number"));
        }

        DateTime now = DateTime.UtcNow;
        ApiKeyCrypto.GeneratedKey generated = ApiKeyCrypto.Generate();

        string description = $"Sensor key for {command.Name}";
        var apiKey = new ApiKey(
            Guid.NewGuid(), generated.Hash, generated.Hint,
            description.Length <= DescriptionMaxLength ? description : description[..DescriptionMaxLength],
            isActive: true, now + ApiKeyCrypto.KeyLifetime);

        var location = new Location(
            Guid.NewGuid(), command.Name, command.Address, command.SerialNumber, command.Zone,
            command.IsActive, command.HasNorgesPriceAgreement);
        location.AssignApiKey(apiKey);

        // One save: the location, its key and (for a user's own location) the link are created together or not at all.
        // Both repositories share the request's DbContext, so a single SaveChanges commits all of it.
        locationRepository.Insert(location);
        if (command.LinkToUserId is { } userId)
        {
            userLocationRepository.Insert(new UserLocation(userId, location.Id));
        }

        await locationRepository.SaveChangesAsync(ct);

        LogMessages.LocationCreated(logger, location.Id, command.ActingUserId);

        return Result.Success(new CreatedLocationResponse(LocationMapper.ToAdminResponse(location, now), generated.Key));
    }
}
