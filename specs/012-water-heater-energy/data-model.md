# Data Model: The Water Heater's kWh and Cost, Split from the Rest

Phase 1 of [plan.md](plan.md). Wire format: [contracts/endpoints.md](contracts/endpoints.md). Decisions:
[research.md](research.md).

Nothing new is stored. The source is `WaterHeaterPowerSample` (spec 011): `LocationId`, `SampledAtUtc`, `PowerKw`.

## Application

### `WaterHeaterHourEnergy` (record, from the repository)

| Field | Type | Rules |
|---|---|---|
| `HourStartUtc` | `DateTime` (UTC, whole hour) | One row per hour that has any held time |
| `Kwh` | `decimal` | Σ power × held seconds / 3600 (research R1); ≥ 0 |
| `SampledSeconds` | `decimal` | Σ held seconds; 0 < value ≤ 3600 |

`IWaterHeaterPowerSampleRepository.GetHourlyEnergyAsync(Guid locationId, DateTime fromUtc, DateTime toUtc,
DateTime nowUtc, CancellationToken ct)` returns the hours in `[fromUtc, toUtc)`, ordered by hour. A hold never
reaches past `nowUtc` or `toUtc`, and samples from `fromUtc − 2 min` are read so the first hour's start is covered.

### `WaterHeaterHour` (record in Application/WaterHeaters, carried on `HourCost`)

| Field | Type | Rules |
|---|---|---|
| `HourStartUtc` | `DateTime` | the hour it belongs to (for the bill's first and last hour) |
| `Kwh` | `decimal` | `min(energy.Kwh, hour.ConsumptionKwh)` |
| `Cost` | `decimal?` | `hour.Cost.Actual.Cost × Kwh / hour.ConsumptionKwh`; `null` without an actual cost; `0` when the hour's kWh is 0 |
| `SampledMinutes` | `int` | `floor(SampledSeconds / 60)`, 0–60 |
| `Capped` | `bool` | `energy.Kwh > hour.ConsumptionKwh` |

`HourCost(DateTime HourStart, decimal ConsumptionKwh, CostResult Cost, WaterHeaterHour? WaterHeater = null)`. It is
`null` when the hour has no `WaterHeaterHourEnergy` row, or when the provider was called without `withWaterHeater`.

### `WaterHeaterTotal` (record, from `WaterHeaterShare.Sum`)

| Field | Type | Rules |
|---|---|---|
| `Kwh` | `decimal` | Σ hour `Kwh` |
| `Cost` | `decimal?` | Σ hour `Cost`; `null` if any summed hour's `Cost` is `null` |
| `SampledMinutes` | `int` | Σ hour `SampledMinutes` |
| `Capped` | `bool` | any hour `Capped` |
| `FirstHourUtc`, `LastHourUtc` | `DateTime` | the first and last summed hour |

`Sum` returns `null` for an empty list (no heater part, never zeros).

### `WaterHeaterShare` (static, pure)

- `ForHour(WaterHeaterHourEnergy energy, decimal hourKwh, decimal? hourActualCost) → WaterHeaterHour`. It takes
  plain values, not `HourCost`, because `HourCost` lives in Features and Application must not reference it.
- `Sum(IEnumerable<WaterHeaterHour?> hours) → WaterHeaterTotal?`: `null` entries are skipped; first and last hour
  come from `HourStartUtc`.

## Where each figure comes from

| Figure | Built from | Projected |
|---|---|---|
| Hour | one `WaterHeaterHourEnergy` + the hour's `HourCost` | no |
| Day (Oslo calendar day) | `Sum` of the day's hours | no |
| Month on the bill | `Sum` of the `soFar` hours | no, never; the bill says `estimate: false` for it |

## Invariants (tested)

- heater kWh ≤ hour kWh; heater cost ≤ hour cost (when the hour's cost ≥ 0)
- day and month sums equal the sum of their hours exactly
- no samples ⇒ `null`, never `0`
- sampled minutes of an hour ≤ 60, of a day ≤ 1440 (1380 / 1500 on clock-change days)
