using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Domain.Common;
using Domain.Common.Entities;
using Features.Meters.Commands;
using Features.Meters.Logging;
using Features.Meters.Mappers;
using Microsoft.Extensions.Logging;

namespace Features.Meters.Handlers;

public class UpdateMeterCommentCommandHandler(
    IMeterRepository<Meter> meterRepository,
    IUserLocationRepository<UserLocation> userLocationRepository,
    ILogger<UpdateMeterCommentCommandHandler> logger)
    : ICommandHandler<UpdateMeterCommentCommand, Result<MeterResponse>>
{
    public async Task<Result<MeterResponse>> Handle(UpdateMeterCommentCommand command, CancellationToken ct)
    {
        Meter? meter = await meterRepository.GetByIdAsync(command.MeterId, ct);

        // Owners of the meter's location and administrators may edit it. A viewer, a stranger and a missing meter all
        // get the same answer, so nobody learns that a meter exists.
        if (meter is null
            || !command.IsAdmin && !await userLocationRepository.IsOwnerAsync(command.UserId, meter.LocationId, ct))
        {
            return Result.Failure<MeterResponse>(Error.NotFound("Meter.NotFound", "Meter not found"));
        }

        meter.SetComment(command.Comment);
        await meterRepository.SaveChangesAsync(ct);

        LogMessages.ReaderCommentUpdated(logger, meter.Id, meter.LocationId, command.UserId);
        return Result.Success(MeterMapper.ToResponse(meter));
    }
}
