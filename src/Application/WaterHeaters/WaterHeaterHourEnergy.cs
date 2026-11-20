namespace Application.WaterHeaters;

/// <summary>
/// The water heater's energy in one UTC hour, integrated from the stored power samples (spec 012, research R1).
/// One row per hour that has any held time: <see cref="Kwh"/> ≥ 0 and 0 &lt; <see cref="SampledSeconds"/> ≤ 3600.
/// The property names are the column aliases of the repository's SQL query.
/// </summary>
public sealed record WaterHeaterHourEnergy(DateTime HourStartUtc, decimal Kwh, decimal SampledSeconds);
