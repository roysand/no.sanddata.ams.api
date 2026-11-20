using Application.WaterHeaters;

namespace Application.Common.Interfaces.Repositories;

/// <summary>Append-only store of water heater power samples.</summary>
public interface IWaterHeaterPowerSampleRepository<T> : IRepository<T> where T : class
{
    /// <summary>
    /// The heater's energy per UTC hour in [<paramref name="fromUtc"/>, <paramref name="toUtc"/>), ordered by hour, with
    /// one row per hour that has any sampled time. Each sample's power holds until the next sample, for at most 2
    /// minutes, and never past <paramref name="nowUtc"/> or <paramref name="toUtc"/>; a hold that crosses an hour
    /// boundary is split between the two hours (spec 012, research R1).
    /// </summary>
    Task<IReadOnlyList<WaterHeaterHourEnergy>> GetHourlyEnergyAsync(
        Guid locationId, DateTime fromUtc, DateTime toUtc, DateTime nowUtc, CancellationToken ct);
}
