using Application.Common.Interfaces.Repositories;
using Application.EvChargers;
using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Database.Repositories;

public class ChargingSessionEfRepository(ApplicationDbContext context) : IChargingSessionRepository
{
    public async Task UpsertAsync(
        Guid locationId, Guid chargerId, IReadOnlyList<ChargingSessionToStore> sessions, CancellationToken cancellationToken)
    {
        if (sessions.Count == 0)
        {
            return;
        }

        // Replace, never merge: a session read again brings all its hours, so the old ones are removed with it.
        var ids = sessions.Select(s => s.Session.Id).ToList();
        List<ChargingSession> existing = await context.ChargingSession
            .Include(s => s.Hours)
            .Where(s => ids.Contains(s.ZaptecSessionId))
            .ToListAsync(cancellationToken);
        context.ChargingSession.RemoveRange(existing);

        foreach (ChargingSessionToStore item in sessions.Where(s => !s.Session.Discard))
        {
            ZaptecSession session = item.Session;
            context.ChargingSession.Add(new ChargingSession(
                Guid.NewGuid(),
                locationId,
                session.Id,
                chargerId,
                session.StartUtc,
                session.EndUtc,
                session.Kwh,
                hasEnergyDetails: item.Hours.All(h => !h.Estimated),
                item.Hours.Select(h => (h.HourStartUtc, h.Kwh, h.Estimated))));
        }
    }

    public async Task<IReadOnlyList<EvChargerHourEnergy>> GetHourlyEnergyAsync(
        Guid locationId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken) =>
        (await context.ChargingSessionHour
            .AsNoTracking()
            .Where(h => h.LocationId == locationId && h.HourStartUtc >= fromUtc && h.HourStartUtc < toUtc)
            .GroupBy(h => h.HourStartUtc)
            .Select(g => new { Hour = g.Key, Kwh = g.Sum(h => h.Kwh), Estimated = g.Any(h => h.Estimated) })
            .OrderBy(g => g.Hour)
            .ToListAsync(cancellationToken))
        .Select(g => new EvChargerHourEnergy(DateTime.SpecifyKind(g.Hour, DateTimeKind.Utc), g.Kwh, g.Estimated))
        .ToList();

    public async Task<IReadOnlyList<ChargingSession>> GetSessionsAsync(
        Guid locationId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken) =>
        await context.ChargingSession
            .AsNoTracking()
            .Where(s => s.LocationId == locationId && s.EndUtc >= fromUtc && s.EndUtc < toUtc)
            .OrderByDescending(s => s.EndUtc)
            .ToListAsync(cancellationToken);

    public async Task<bool> AnyForLocationAsync(Guid locationId, CancellationToken cancellationToken) =>
        await context.ChargingSession.AnyAsync(s => s.LocationId == locationId, cancellationToken);

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        await context.SaveChangesAsync(cancellationToken);
}
