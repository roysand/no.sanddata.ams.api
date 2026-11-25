using Application.WaterHeaters;

namespace Features.WaterHeaters.Services;

/// <summary>
/// The hot water counting rule's numbers (spec 020, research R6). First calibrated on 2026-11-25 against the OSO
/// app's Day view, from four hours of Roy's samples:
/// <list type="bullet">
/// <item><c>KwhPerLitre</c> 0.04: the 0.0349 kWh that heat 1 L from 10 °C to 40 °C, plus heat losses. With it the
/// day's four hours gave 108 L against OSO's 106 L; 0.035 gave 123 L.</item>
/// <item><c>MinHourLitres</c> 3: with the heater idle the tank loses about 2–3 L of 40 °C water an hour, which OSO
/// shows as 0.</item>
/// </list>
/// Quiet nights (T030) and OSO's own ratio (T031) may still adjust them. The outcome is written under "Calibration
/// results" in specs/020-water-heater-consumption/research.md.
/// </summary>
public static class HotWaterRule
{
    public static readonly HotWaterRuleValues Current = new(0.04m, 3);
}
