using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Domain.Common;
using Domain.Common.Entities;
using Features.Users.Commands;
using Features.Users.Logging;
using Microsoft.Extensions.Logging;

namespace Features.Users.Handlers;

public class UnlinkUserLocationCommandHandler(
    IUserRepository<User> userRepository,
    ILocationRepository<Location> locationRepository,
    IUserLocationRepository<UserLocation> userLocationRepository,
    ILogger<UnlinkUserLocationCommandHandler> logger)
    : ICommandHandler<UnlinkUserLocationCommand, Result<UserLocationChangeResponse>>
{
    public async Task<Result<UserLocationChangeResponse>> Handle(UnlinkUserLocationCommand command, CancellationToken ct)
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

        UserLocation? link = (await userLocationRepository.FindAsync(
            ul => ul.UserId == command.UserId && ul.LocationId == command.LocationId, ct)).FirstOrDefault();
        if (link is null)
        {
            return Result.Success(new UserLocationChangeResponse(false));
        }

        // A location always keeps at least one owner; viewers and surplus owners can be removed freely.
        if (link.Role == LocationRole.Owner
            && await userLocationRepository.CountOwnersAsync(command.LocationId, ct) <= 1)
        {
            return Result.Failure<UserLocationChangeResponse>(Error.Conflict(
                "Location.LastOwner", "A location must keep at least one owner. Make another user an owner first."));
        }

        userLocationRepository.Delete(link);
        await userLocationRepository.SaveChangesAsync(ct);

        LogMessages.UserLocationUnlinked(logger, command.UserId, command.LocationId, command.Caller.Id);
        return Result.Success(new UserLocationChangeResponse(true));
    }
}
