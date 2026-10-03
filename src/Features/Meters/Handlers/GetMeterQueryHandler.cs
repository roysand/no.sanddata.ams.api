using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Domain.Common;
using Domain.Common.Entities;
using Features.Meters.Commands;
using Features.Meters.Mappers;
using Features.Meters.Queries;

namespace Features.Meters.Handlers;

public class GetMeterQueryHandler : IQueryHandler<GetMeterQuery, Result<MeterResponse>>
{
    private readonly IMeterRepository<Meter> _meterRepository;
    private readonly ILocationRepository<Location> _locationRepository;

    public GetMeterQueryHandler(
        IMeterRepository<Meter> meterRepository,
        ILocationRepository<Location> locationRepository)
    {
        _meterRepository = meterRepository;
        _locationRepository = locationRepository;
    }

    public async Task<Result<MeterResponse>> Handle(GetMeterQuery query, CancellationToken ct)
    {
        Meter? meter = await _meterRepository.GetByIdAsync(query.Id, ct);

        // A meter at a location the user does not belong to looks exactly like a missing meter.
        if (meter is null || !await _locationRepository.IsUserAssociatedAsync(query.UserId, meter.LocationId, ct))
        {
            return Result.Failure<MeterResponse>(Error.NotFound("Meter.NotFound", "Meter not found"));
        }

        return Result.Success(MeterMapper.ToResponse(meter));
    }
}
