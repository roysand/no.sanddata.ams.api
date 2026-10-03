using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Domain.Common;
using Domain.Common.Entities;
using Features.ElectricityCost.Logging;
using Features.ElectricityCost.Mappers;
using Features.ElectricityCost.Queries;
using Features.ElectricityCost.Services;
using Microsoft.Extensions.Logging;

namespace Features.ElectricityCost.Handlers;

public class GetConsumptionQueryHandler(
    ILocationRepository<Location> locationRepository,
    IMeterRepository<Meter> meterRepository,
    IConsumptionRepository consumptionRepository,
    ILogger<GetConsumptionQueryHandler> logger)
    : IQueryHandler<GetConsumptionQuery, Result<ConsumptionResponse>>
{
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromHours(24);

    public async Task<Result<ConsumptionResponse>> Handle(GetConsumptionQuery query, CancellationToken ct)
    {
        if (!await locationRepository.IsUserAssociatedAsync(query.UserId, query.LocationId, ct))
        {
            LogMessages.LocationAccessDenied(logger, query.UserId, query.LocationId);
            return Result.Failure<ConsumptionResponse>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        if (query.MeterId is { } meterId)
        {
            Meter? meter = await meterRepository.GetByIdAsync(meterId, ct);
            if (meter is null || meter.LocationId != query.LocationId)
            {
                return Result.Failure<ConsumptionResponse>(Error.NotFound("Meter.NotFound", "Meter not found"));
            }
        }

        DateTime to = query.To is { } toValue ? CostTime.ToUtc(toValue) : DateTime.UtcNow;
        DateTime from = query.From is { } fromValue ? CostTime.ToUtc(fromValue) : to - DefaultWindow;

        IEnumerable<(DateTime, decimal)> buckets = query.Granularity == "minute"
            ? (await consumptionRepository.GetMinuteAsync(query.LocationId, query.MeterId, from, to, ct))
                .Select(r => (r.BucketStart, r.ConsumptionKwh))
            : (await consumptionRepository.GetHourlyAsync(query.LocationId, query.MeterId, from, to, ct))
                .Select(r => (r.BucketStart, r.ConsumptionKwh));

        LogMessages.CostQueried(logger, query.UserId, query.LocationId);

        return Result.Success(ConsumptionMapper.ToConsumptionResponse(query.Granularity, buckets));
    }
}
