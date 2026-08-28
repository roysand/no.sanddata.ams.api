using Domain.Common.Entities;
using Features.Meters.Commands;
using Features.Meters.Endpoints;

namespace Features.Meters.Mappers;

public static class MeterMapper
{
    public static CreateMeterCommand ToCommand(CreateMeterRequest request) =>
        new(request.LocationId, request.DeviceId, request.Comment);

    public static MeterResponse ToResponse(Meter meter) =>
        new(meter.Id, meter.LocationId, meter.DeviceId, meter.MeterId, meter.MeterType, meter.Comment, meter.IsActive);
}
