using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Application.WaterHeaters;
using Domain.Common;
using Domain.Common.Entities;
using Features.WaterHeaters.Logging;
using Features.WaterHeaters.Mappers;
using Features.WaterHeaters.Queries;
using Features.WaterHeaters.Services;
using Microsoft.Extensions.Logging;

namespace Features.WaterHeaters.Handlers;

/// <summary>
/// The 40 °C litres used per hour, day or month of a period, counted from the stored heater samples (spec 020).
/// Owners and viewers of an active location may read it, and so may an Admin for any existing location, active or
/// not (research R8); everyone else gets the same 404. It reads only, and needs no connected heater: the samples
/// belong to the location. The validator has already refused a bad period or date, so this only fills in today's
/// date and does not check them again (Principle IV).
/// </summary>
public class GetWaterHeaterHotWaterQueryHandler(
    ILocationRepository<Location> locations,
    IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample> samples,
    TimeProvider clock,
    ILogger<GetWaterHeaterHotWaterQueryHandler> logger)
    : IQueryHandler<GetWaterHeaterHotWaterQuery, Result<WaterHeaterHotWaterResponse>>
{
    private static readonly Error LocationNotFound = Error.NotFound("Location.NotFound", "Location not found");

    public async Task<Result<WaterHeaterHotWaterResponse>> Handle(
        GetWaterHeaterHotWaterQuery query, CancellationToken ct)
    {
        bool mayRead = query.IsAdmin
            ? await locations.GetByIdAsync(query.LocationId, ct) is not null
            : await locations.IsUserAssociatedAsync(query.UserId, query.LocationId, ct);
        if (!mayRead)
        {
            LogMessages.LocationAccessDenied(logger, query.UserId, query.LocationId);
            return Result.Failure<WaterHeaterHotWaterResponse>(LocationNotFound);
        }

        DateTime now = clock.GetUtcNow().UtcDateTime;
        DateOnly date = query.Date ?? HotWaterPeriods.TodayInOslo(now);
        (DateTime from, DateTime to) = HotWaterPeriods.Bounds(query.Period, date);
        IReadOnlyList<DateTime> partStarts = HotWaterPeriods.PartStarts(query.Period, date);

        IReadOnlyList<WaterHeaterHotWaterHour> hours =
            await samples.GetHourlyHotWaterAsync(query.LocationId, from, to, now, HotWaterRule.Current, ct);

        IReadOnlyList<WaterHeaterHotWaterItem> items = WaterHeaterMapper.ToHotWaterItems(partStarts, to, now, hours);
        int? total = items.Any(i => i.Litres is not null) ? items.Sum(i => i.Litres ?? 0) : null;
        int sampledMinutes = items.Sum(i => i.SampledMinutes);

        return Result.Success(new WaterHeaterHotWaterResponse(
            query.LocationId, WaterHeaterMapper.ToWire(query.Period), from, to, total, sampledMinutes, items));
    }
}
