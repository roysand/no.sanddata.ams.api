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

The counting rule's numbers, passed to the query (research R4, R5).

| Field | Type | Starting value |
|---|---|---|
| `KwhPerLitre` | `decimal` | 0.035 |
| `MinFallLitres` | `int` | 2 |

The values live in `Features/WaterHeaters/Services/HotWaterRule.cs` as `HotWaterRule.Current`. Calibration (research
R6) changes them there.

### `WaterHeaterHotWaterHour` (Application/WaterHeaters)

One row of the new SQL query (research R4).

| Field | Type | Notes |
|---|---|---|
| `HourStartUtc` | `DateTime` (UTC) | Whole UTC hour holding the later sample of each counted pair |
| `Litres` | `decimal` | Σ used litres of the hour's counted pairs, unrounded, ≥ 0 |
| `SampledSeconds` | `decimal` | Σ seconds of the hour's counted pairs (0 < s ≤ 3600 + one pair's spill, see note) |

**Note on the bucket:** a pair is put in its later sample's hour in full, so an hour can hold up to about
2 minutes that started in the hour before. The mapper caps `sampledMinutes` at the part's length in minutes.

### Repository method (Application, `IWaterHeaterPowerSampleRepository<T>`)

```text
GetHourlyHotWaterAsync(locationId, fromUtc, toUtc, nowUtc, HotWaterRuleValues rule, ct)
    → IReadOnlyList<WaterHeaterHotWaterHour>   // ordered by hour, only hours with counted pairs
```

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
samples ──LAG──► pairs (≤ 2 min apart, both with litres)
        ──► used = fall + heated  if fall ≥ MinFallLitres, else 0
        ──► Σ per UTC hour of the later sample          (SQL)
        ──► Σ per part, round litres, total of rounded  (C#)
```

- **Parts in the future:** left out. The part holding now counts the pairs up to now.
- **No pairs in a part:** `litres = null`, `sampledMinutes = 0`.
- **The rounding:** per part. The total is the sum of the rounded parts (research R7).
