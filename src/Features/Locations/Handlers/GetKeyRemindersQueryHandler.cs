using Application.Common.ApiKeys;
using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Application.ElectricityCost;
using Domain.Common;
using Domain.Common.Entities;
using Features.Locations.Logging;
using Features.Locations.Mappers;
using Features.Locations.Queries;
using Microsoft.Extensions.Logging;

namespace Features.Locations.Handlers;

/// <summary>
/// The keys an administrator must replace soon (spec 015): sensor keys from the location list and OSO keys of the heaters
/// at active locations, two reads in all. Nothing is stored; the list is worked out on each request.
/// </summary>
public class GetKeyRemindersQueryHandler(
    ILocationRepository<Location> locations,
    IWaterHeaterRepository<WaterHeater> heaters,
    TimeProvider clock,
    ILogger<GetKeyRemindersQueryHandler> logger)
    : IQueryHandler<GetKeyRemindersQuery, Result<KeyRemindersResponse>>
{
    public async Task<Result<KeyRemindersResponse>> Handle(GetKeyRemindersQuery query, CancellationToken ct)
    {
        IReadOnlyList<Location> all = await locations.GetAllWithKeyAsync(ct);
        IReadOnlyList<WaterHeater> active = await heaters.GetForActiveLocationsAsync(ct);

        var names = all.ToDictionary(l => l.Id, l => l.Name);
        IEnumerable<SensorKeyFacts> sensorKeys = all
            .Where(l => l.ApiKey is not null)
            .Select(l => new SensorKeyFacts(l.Id, l.Name, l.IsActive, l.ApiKey.IsActive, l.ApiKey.ExpiresAt));
        IEnumerable<HeaterKeyFacts> heaterKeys = active
            .Where(h => names.ContainsKey(h.LocationId))
            .Select(h => new HeaterKeyFacts(h.LocationId, names[h.LocationId], h.SubscriptionKeyExpiresOn));

        DateOnly today = NorgesPrisSchedule.OsloDateOf(clock.GetUtcNow().UtcDateTime);
        IReadOnlyList<KeyReminder> due = KeyReminders.Due(today, sensorKeys, heaterKeys);

        LogMessages.KeyRemindersListed(logger, query.ActingUserId, due.Count);

        return Result.Success(KeyReminderMapper.ToResponse(today, KeyReminders.DefaultWindowDays, due));
    }
}
