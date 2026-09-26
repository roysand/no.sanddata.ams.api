using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Domain.Common;
using Domain.Common.Entities;
using Features.Measurements.Logging;
using Features.Measurements.Mappers;
using Features.Measurements.Queries;
using Microsoft.Extensions.Logging;

namespace Features.Measurements.Handlers;

public class GetLatestMeasurementQueryHandler : IQueryHandler<GetLatestMeasurementQuery, Result<LatestMeasurementResponse>>
{
    private readonly ILocationRepository<Location> _locationRepository;
    private readonly IMeterRepository<Meter> _meterRepository;
    private readonly IMeasurementRepository<Measurement> _measurementRepository;
    private readonly ILogger<GetLatestMeasurementQueryHandler> _logger;

    public GetLatestMeasurementQueryHandler(
        ILocationRepository<Location> locationRepository,
        IMeterRepository<Meter> meterRepository,
        IMeasurementRepository<Measurement> measurementRepository,
        ILogger<GetLatestMeasurementQueryHandler> logger)
    {
        _locationRepository = locationRepository;
        _meterRepository = meterRepository;
        _measurementRepository = measurementRepository;
        _logger = logger;
    }

    public async Task<Result<LatestMeasurementResponse>> Handle(GetLatestMeasurementQuery query, CancellationToken ct)
    {
        if (!await _locationRepository.IsUserAssociatedAsync(query.UserId, query.LocationId, ct))
        {
            LogMessages.LocationAccessDenied(_logger, query.UserId, query.LocationId);
            return Result.Failure<LatestMeasurementResponse>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        if (query.MeterId is { } meterId)
        {
            Meter? meter = await _meterRepository.GetByIdAsync(meterId, ct);
            if (meter is null || meter.LocationId != query.LocationId)
            {
                return Result.Failure<LatestMeasurementResponse>(Error.NotFound("Meter.NotFound", "Meter not found"));
            }
        }

        Measurement? latest = await _measurementRepository.GetLatestAsync(query.LocationId, query.MeterId, ct);

        LogMessages.LatestMeasurementQueried(_logger, query.UserId, query.LocationId);

        return Result.Success(new LatestMeasurementResponse(latest is null ? null : MeasurementMapper.ToResponse(latest)));
    }
}
