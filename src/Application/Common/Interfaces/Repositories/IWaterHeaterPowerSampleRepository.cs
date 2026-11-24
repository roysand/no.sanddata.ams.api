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

    /// <summary>
    /// The heater's energy per 5-minute UTC step in [<paramref name="fromUtc"/>, <paramref name="toUtc"/>), ordered by
    /// step, with one row per step that has any sampled time. Each sample's power holds until the next sample, for at
    /// most 2 minutes, and never past <paramref name="nowUtc"/> or <paramref name="toUtc"/>; a hold that crosses a step
    /// boundary is split between the two steps (spec 017, research R1).
    /// </summary>
    Task<IReadOnlyList<WaterHeaterPowerStep>> GetPowerStepsAsync(
        Guid locationId, DateTime fromUtc, DateTime toUtc, DateTime nowUtc, CancellationToken ct);

    /// <summary>
    /// The 40 °C litres used per UTC hour in [<paramref name="fromUtc"/>, min(<paramref name="toUtc"/>,
    /// <paramref name="nowUtc"/>)), ordered by hour (spec 020, research R4). Each sample is paired with the one just
    /// before it when both have litres and they are at most 2 minutes apart. A pair counts <c>fall + heated</c> when
    /// the litres fell by at least <see cref="HotWaterRuleValues.MinFallLitres"/>, otherwise 0, and goes in full to the
    /// UTC hour of its later sample. <c>heated</c> is the earlier sample's power over the pair, turned into litres at
    /// <see cref="HotWaterRuleValues.KwhPerLitre"/>. Only hours with counted pairs are returned.
    /// </summary>
    Task<IReadOnlyList<WaterHeaterHotWaterHour>> GetHourlyHotWaterAsync(
        Guid locationId, DateTime fromUtc, DateTime toUtc, DateTime nowUtc, HotWaterRuleValues rule,
        CancellationToken ct);
}
