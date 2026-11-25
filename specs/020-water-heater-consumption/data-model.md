# Data Model: Hot Water Used per Hour, Day and Month

Spec: [spec.md](spec.md). Research: [research.md](research.md).

## Stored

### `WaterHeaterPowerSample` (Domain, extended)

| Field | Type | Change | Notes |
|---|---|---|---|
| `Id` | `Guid` | – | |
| `LocationId` | `Guid` | – | The samples belong to the location (spec 011) |
| `SampledAtUtc` | `DateTime` (UTC) | – | Truncated to the second |
| `PowerKw` | `decimal(9,6)` | – | Unchanged; specs 012 and 017 read it as before |
| `AvailableLitres40` | `int?` | **new** | Litres of 40 °C water OSO reported at the sample (`Status.availableMix40Volume`). `null` when OSO did not give it and for every row stored before this feature (FR-001, FR-002) |

- The constructor gets an optional `int? availableLitres40 = null` argument, so existing callers and tests still
  compile.
- **Migration** `AddWaterHeaterLitres`: one nullable `integer` column with no default and no backfill.
- **Index:** the existing `(LocationId, SampledAtUtc)` serves the new query (research R9).
- **Validation:** none in the domain. OSO's number is stored as given. A negative or missing value from OSO makes the
  client treat the litres as missing (research R2).

## Application (records, not stored)

### `WaterHeaterSample` (Application/WaterHeaters)

What one sampler call reads from OSO (research R2).

| Field | Type | Notes |
|---|---|---|
| `PowerKw` | `decimal` | From `ActualLoad`. If it can't be read, the whole call fails |
| `Litres40` | `int?` | From `Status.availableMix40Volume`; `null` when `Status` failed or had no number |
| `LitresError` | `Error?` | Why `Litres40` is `null`, for the rate-limited log (research R3). `null` when the litres were read |

### `HotWaterRuleValues` (Application/WaterHeaters)

The counting rule's numbers (research R10; first values in R5, changed on 2026-11-25).

| Field | Type | Value |
|---|---|---|
| `KwhPerLitre` | `decimal` | 0.04 |
| `MinHourLitres` | `int` | 3 |

The values live in `Features/WaterHeaters/Services/HotWaterRule.cs` as `HotWaterRule.Current`. Calibration (research
R6) changes them there.

### `WaterHeaterLitresHour` (Application/WaterHeaters)

One row of the litres query (research R10): the first and last litres of a UTC hour, from its samples that have
litres.

| Field | Type | Notes |
|---|---|---|
| `HourStartUtc` | `DateTime` (UTC) | Whole UTC hour |
| `FirstLitres` | `int` | Litres of the hour's earliest sample with litres |
| `LastLitres` | `int` | Litres of the hour's latest sample with litres |

### `WaterHeaterHotWaterHour` (Application/WaterHeaters)

One counted hour, made by `HotWaterBalance.Count` (research R10).

| Field | Type | Notes |
|---|---|---|
| `HourStartUtc` | `DateTime` (UTC) | Whole UTC hour |
| `Litres` | `decimal` | start − end + heated kWh ÷ `KwhPerLitre`, unrounded; 0 when under `MinHourLitres` |
| `SampledSeconds` | `decimal` | The hour's sampled time by the hold rule (spec 012), at most 3600 |

### Repository method (Application, `IWaterHeaterPowerSampleRepository<T>`)

```text
GetHourlyHotWaterAsync(locationId, fromUtc, toUtc, nowUtc, HotWaterRuleValues rule, ct)
    → IReadOnlyList<WaterHeaterHotWaterHour>   // ordered by hour, only hours with litres
```

It reads the hourly energy (`GetHourlyEnergyAsync`) and the hourly litres (from one hour before `fromUtc`), then
calls `HotWaterBalance.Count`.

## Features (query and response, not stored)

### `HotWaterPeriod` (enum)

`Day`, `Week`, `Month`, `Year`.

### `GetWaterHeaterHotWaterQuery`

| Field | Type | Notes |
|---|---|---|
| `UserId` | `Guid` | The caller |
| `IsAdmin` | `bool` | |
| `LocationId` | `Guid` | |
| `Period` | `HotWaterPeriod` | Already validated; `Day` when left out |
| `Date` | `DateOnly?` | Oslo date; today in Oslo when `null` |

### `WaterHeaterHotWaterResponse`

| Field | Type | Notes |
|---|---|---|
| `LocationId` | `Guid` | |
| `Period` | `string` | `day`, `week`, `month` or `year` |
| `From` | `DateTime` (UTC) | The period's start: an Oslo midnight |
| `To` | `DateTime` (UTC) | The period's end (exclusive), even when it is after now |
| `TotalLitres` | `int?` | Σ of the items' `litres`; `null` when every item is `null` or there are no items |
| `SampledMinutes` | `int` | Σ of the items' `sampledMinutes` |
| `Items` | list of `WaterHeaterHotWaterItem` | One per part that starts before now, in time order |

### `WaterHeaterHotWaterItem`

| Field | Type | Notes |
|---|---|---|
| `Start` | `DateTime` (UTC) | The part's start: a UTC hour (day), an Oslo midnight (week, month) or an Oslo first of the month (year) |
| `Litres` | `int?` | Whole litres of 40 °C water used, rounded half away from zero; `null` when the part has no sampled time, never 0 for that |
| `SampledMinutes` | `int` | Rounded down, capped at the part's length; 0 when `Litres` is `null` |

## Derivation and rules

```text
period (kind, date) ──► [from, to) in UTC ──► parts (hours | Oslo days | Oslo months), starting before now
samples with litres ──► first and last litres per UTC hour     (SQL)
samples ──► heated kWh and sampled seconds per UTC hour         (SQL, spec 012's hold rule)
        ──► used = start − end + kWh ÷ KwhPerLitre, 0 if < MinHourLitres   (C#, HotWaterBalance)
        ──► Σ per part, round litres, total of rounded           (C#)
```

- **Parts in the future:** left out. The part holding now counts up to now.
- **No samples with litres in a part:** `litres = null`, `sampledMinutes = 0`.
- **The rounding:** per part. The total is the sum of the rounded parts (research R7).
