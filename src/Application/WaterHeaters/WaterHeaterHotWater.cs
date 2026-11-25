namespace Application.WaterHeaters;

/// <summary>
/// The numbers of the hot water counting rule, passed to the repository's query (spec 020, research R4/R5). A pair of
/// samples counts its litres only when they fell by at least <paramref name="MinFallLitres"/>; the heating done in
/// the pair is turned back into litres at <paramref name="KwhPerLitre"/>.
/// </summary>
/// <param name="KwhPerLitre">kWh that heat one litre of cold water to 40 °C.</param>
/// <param name="MinFallLitres">The smallest fall in litres between two samples that counts as water used.</param>
public sealed record HotWaterRuleValues(decimal KwhPerLitre, int MinFallLitres);

/// <summary>
/// The 40 °C litres used in one UTC hour, counted from consecutive stored samples (spec 020, research R4). One row per
/// hour with any counted pair. <see cref="Litres"/> is unrounded and ≥ 0. <see cref="SampledSeconds"/> is the summed
/// time of the hour's pairs; a pair goes in full to its later sample's hour, so it can be a little over 3600. Like
/// <see cref="WaterHeaterPowerStep"/>, the property names are the column aliases of the repository's SQL query.
/// </summary>
public sealed record WaterHeaterHotWaterHour(DateTime HourStartUtc, decimal Litres, decimal SampledSeconds);
