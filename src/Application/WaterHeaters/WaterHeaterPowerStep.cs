namespace Application.WaterHeaters;

/// <summary>
/// The water heater's energy in one 5-minute UTC step, integrated from the stored power samples (spec 017, research
/// R1). One row per step that has any held time: <see cref="Kwh"/> ≥ 0 and 0 &lt; <see cref="SampledSeconds"/> ≤ 300.
/// Like <see cref="WaterHeaterHourEnergy"/>, the property names are the column aliases of the repository's SQL query.
/// </summary>
public sealed record WaterHeaterPowerStep(DateTime StepStartUtc, decimal Kwh, decimal SampledSeconds);
