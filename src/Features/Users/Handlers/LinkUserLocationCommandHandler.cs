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

        UserLocation? link = (await userLocationRepository.FindAsync(
            ul => ul.UserId == command.UserId && ul.LocationId == command.LocationId, ct)).FirstOrDefault();

        if (link is null)
        {
            userLocationRepository.Insert(new UserLocation(command.UserId, command.LocationId, command.Role));
            await userLocationRepository.SaveChangesAsync(ct);

            LogMessages.UserLocationLinked(logger, command.UserId, command.LocationId, command.Caller.Id);
            return Result.Success(new UserLocationChangeResponse(true));
        }

        if (link.Role == command.Role)
        {
            return Result.Success(new UserLocationChangeResponse(false));
        }

        // A location always keeps at least one owner. Several are allowed, so making someone an owner never conflicts.
        if (link.Role == LocationRole.Owner
            && command.Role == LocationRole.Viewer
            && await userLocationRepository.CountOwnersAsync(command.LocationId, ct) <= 1)
        {
            return Result.Failure<UserLocationChangeResponse>(Error.Conflict(
                "Location.LastOwner", "A location must keep at least one owner. Make another user an owner first."));
        }

        link.ChangeRole(command.Role);
        userLocationRepository.Update(link);
        await userLocationRepository.SaveChangesAsync(ct);

        LogMessages.UserLocationRoleChanged(logger, command.UserId, command.LocationId, command.Role, command.Caller.Id);
        return Result.Success(new UserLocationChangeResponse(true));
    }
}
