using Application.Common.Interfaces.Repositories;
using Application.WaterHeaters;
using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Database.Repositories;

public class WaterHeaterPowerSampleEfRepository
    : GenericEfRepository<WaterHeaterPowerSample>, IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample>
{
    public WaterHeaterPowerSampleEfRepository(ApplicationDbContext applicationDbContext) : base(applicationDbContext)
    {
    }

    /// <remarks>
    /// The repo's one raw SQL query (spec 012, plan "Complexity Tracking", research R1/R2). The hold rule needs each
    /// sample's next sample (<c>LEAD</c>), which LINQ to EF can't express, and a year view must not load about a million
    /// samples into memory. Every value is a parameter of the interpolated query.
    /// <list type="bullet">
    /// <item><c>s</c>: each sample's hold ends at the next sample, 2 minutes after it, now or <c>to</c>, whichever comes
    /// first. Samples from 2 minutes before <c>from</c> are read so the first hour's start is covered.</item>
    /// <item><c>held</c>: holds are clipped to start at <c>from</c>; empty holds (two samples in the same second) are
    /// dropped.</item>
    /// <item><c>parts</c>: a hold is at most 2 minutes, so it crosses at most one hour boundary; the part before the
    /// boundary goes to its own hour and the rest to the next.</item>
    /// </list>
    /// </remarks>
    public async Task<IReadOnlyList<WaterHeaterHourEnergy>> GetHourlyEnergyAsync(
        Guid locationId, DateTime fromUtc, DateTime toUtc, DateTime nowUtc, CancellationToken ct)
    {
        List<WaterHeaterHourEnergy> rows = await _context.Database
            .SqlQuery<WaterHeaterHourEnergy>(
                $"""
                WITH s AS (
                    SELECT "SampledAtUtc" AS t,
                           "PowerKw" AS p,
                           LEAST(
                               COALESCE(LEAD("SampledAtUtc") OVER (ORDER BY "SampledAtUtc"), {nowUtc}),
                               "SampledAtUtc" + interval '2 minutes',
                               {nowUtc},
                               {toUtc}) AS e
                    FROM public."WaterHeaterPowerSample"
                    WHERE "LocationId" = {locationId}
                      AND "SampledAtUtc" >= {fromUtc} - interval '2 minutes'
                      AND "SampledAtUtc" < {toUtc}
                ),
                held AS (
                    SELECT GREATEST(t, {fromUtc}) AS t, e, p
                    FROM s
                    WHERE e > GREATEST(t, {fromUtc})
                ),
                parts AS (
                    SELECT date_trunc('hour', t, 'UTC') AS h,
                           t,
                           LEAST(e, date_trunc('hour', t, 'UTC') + interval '1 hour') AS e,
                           p
                    FROM held
                    UNION ALL
                    SELECT date_trunc('hour', t, 'UTC') + interval '1 hour' AS h,
                           date_trunc('hour', t, 'UTC') + interval '1 hour' AS t,
                           e,
                           p
                    FROM held
                    WHERE e > date_trunc('hour', t, 'UTC') + interval '1 hour'
                )
                SELECT h AS "HourStartUtc",
                       SUM(p * EXTRACT(EPOCH FROM e - t) / 3600) AS "Kwh",
                       SUM(EXTRACT(EPOCH FROM e - t)) AS "SampledSeconds"
                FROM parts
                GROUP BY h
                ORDER BY h
                """)
            .ToListAsync(ct);

        return rows
            .Select(r => r with { HourStartUtc = DateTime.SpecifyKind(r.HourStartUtc, DateTimeKind.Utc) })
            .ToList();
    }
}
