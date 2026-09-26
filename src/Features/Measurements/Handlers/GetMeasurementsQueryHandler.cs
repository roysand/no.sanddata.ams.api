using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Domain.Common;
using Domain.Common.Entities;
using Features.Measurements.Logging;
using Features.Measurements.Mappers;
using Features.Measurements.Queries;
using Microsoft.Extensions.Logging;

namespace Features.Measurements.Handlers;

public class GetMeasurementsQueryHandler : IQueryHandler<GetMeasurementsQuery, Result<PagedMeasurementsResponse>>
{
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromHours(24);

    private readonly ILocationRepository<Location> _locationRepository;
    private readonly IMeterRepository<Meter> _meterRepository;
    private readonly IMeasurementRepository<Measurement> _measurementRepository;
    private readonly ILogger<GetMeasurementsQueryHandler> _logger;

    public GetMeasurementsQueryHandler(
        ILocationRepository<Location> locationRepository,
        IMeterRepository<Meter> meterRepository,
        IMeasurementRepository<Measurement> measurementRepository,
        ILogger<GetMeasurementsQueryHandler> logger)
    {
        _locationRepository = locationRepository;
        _meterRepository = meterRepository;
        _measurementRepository = measurementRepository;
        _logger = logger;
    }

    public async Task<Result<PagedMeasurementsResponse>> Handle(GetMeasurementsQuery query, CancellationToken ct)
    {
        if (!await _locationRepository.IsUserAssociatedAsync(query.UserId, query.LocationId, ct))
        {
            LogMessages.LocationAccessDenied(_logger, query.UserId, query.LocationId);
            return Result.Failure<PagedMeasurementsResponse>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        if (query.MeterId is { } meterId)
        {
            Meter? meter = await _meterRepository.GetByIdAsync(meterId, ct);
            if (meter is null || meter.LocationId != query.LocationId)
            {
                return Result.Failure<PagedMeasurementsResponse>(Error.NotFound("Meter.NotFound", "Meter not found"));
            }
        }

        DateTime to = query.To is { } toValue ? ToUtc(toValue) : DateTime.UtcNow;
        DateTime from = query.From is { } fromValue ? ToUtc(fromValue) : to - DefaultWindow;

        (IReadOnlyList<Measurement> items, int totalCount) = await _measurementRepository.GetPagedAsync(
            query.LocationId, query.MeterId, from, to, query.Page, query.PageSize, ct);

        LogMessages.MeasurementsQueried(_logger, query.UserId, query.LocationId);

        return Result.Success(MeasurementMapper.ToPagedResponse(items, query.Page, query.PageSize, totalCount));
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
