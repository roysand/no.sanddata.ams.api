using Application.CQRS;
using Domain.Common;

namespace Features.Meters.Commands;

public record CreateMeterCommand(Guid LocationId, string DeviceId, string? Comment) : ICommand<Result<MeterResponse>>;

public record MeterResponse(
    Guid Id,
    Guid LocationId,
    string DeviceId,
    string? MeterId,
    string? MeterType,
    string? Comment,
    bool IsActive);
