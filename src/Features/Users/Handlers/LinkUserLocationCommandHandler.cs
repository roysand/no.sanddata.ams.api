using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Domain.Common;
using Domain.Common.Entities;
using Features.Users.Commands;
using Features.Users.Logging;
using Microsoft.Extensions.Logging;

namespace Features.Users.Handlers;

public class LinkUserLocationCommandHandler(
    IUserRepository<User> userRepository,
    ILocationRepository<Location> locationRepository,
    IUserLocationRepository<UserLocation> userLocationRepository,
    ILogger<LinkUserLocationCommandHandler> logger)
    : ICommandHandler<LinkUserLocationCommand, Result<UserLocationChangeResponse>>
{
    public async Task<Result<UserLocationChangeResponse>> Handle(LinkUserLocationCommand command, CancellationToken ct)
    {
        if (!await userRepository.ExistsAsync(u => u.Id == command.UserId, ct))
        {
            return Result.Failure<UserLocationChangeResponse>(
                Error.NotFound("User.NotFound", $"User with ID {command.UserId} was not found"));
        }

        if (!await locationRepository.ExistsAsync(l => l.Id == command.LocationId, ct))
        {
            return Result.Failure<UserLocationChangeResponse>(
                Error.NotFound("Location.NotFound", "Location not found"));
        }

        if (await userLocationRepository.ExistsAsync(
                ul => ul.UserId == command.UserId && ul.LocationId == command.LocationId, ct))
        {
            return Result.Success(new UserLocationChangeResponse(false));
        }

        userLocationRepository.Insert(new UserLocation(command.UserId, command.LocationId));
        await userLocationRepository.SaveChangesAsync(ct);

        LogMessages.UserLocationLinked(logger, command.UserId, command.LocationId, command.Caller.Id);
        return Result.Success(new UserLocationChangeResponse(true));
    }
}
