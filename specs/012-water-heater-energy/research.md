# Research: The Water Heater's kWh and Cost, Split from the Rest

Phase 0 of [plan.md](plan.md). The Technical Context had no open questions; these are the decisions behind it.

## R1. From samples to kWh per hour

- **Decision**:
  - **Hold:** each sample's power holds from its time until the next sample. The hold ends at most 2 minutes later,
    and never after `now`.
  - **Not sampled:** time that no hold covers.
  - **Hour boundaries:** a hold that crosses one is split at the boundary. The part before goes to the earlier hour
    and the rest to the next.
  - **Per hour:** energy = Σ power × held seconds / 3600 (kWh), and sampled seconds = Σ held seconds, which is at
    most 3600.
  - **Sampled minutes:** the sampled seconds rounded down to whole minutes.
- **Rationale**:
  - Samples come about every 60 s (spec 011). Holding up to 2 minutes bridges one missed sample. A longer gap is a
    real outage and is reported as not sampled rather than guessed (spec FR-001, US1 scenario 2).
  - The heater switches between about 2–3 kW and about 0,01 kW and stays in each state for many minutes. The error of
    holding the previous value is at most one sample interval per switch, roughly ±1 minute of heating, and it evens
    out over a day. That is within SC-002 for a steady hour, where there is no switch.
  - Splitting at the boundary keeps every hour at 60 minutes or less, and keeps the day sum equal to the sum of whole
    holds.
- **Alternatives**:
  - Trapezoid between samples: wrong for an on/off load, because it invents half-power minutes at every switch.
  - Giving the whole hold to the sample's own hour: an hour could show 61 minutes.
  - A fixed 60 s per sample: overcounts when two samples are close, and can't see gaps.

## R2. Where the integration runs

- **Decision**: In PostgreSQL, as one query in `WaterHeaterPowerSampleEfRepository.GetHourlyEnergyAsync`. It reads the
  samples from `from − 2 min` to `to`, uses `LEAD("SampledAtUtc") OVER (ORDER BY "SampledAtUtc")` for each hold's end,
  `LEAST(next, t + 2 min, now)`, `UNION ALL` for the part past the hour boundary, and
  `date_trunc('hour', t, 'UTC')`. It returns `(HourStartUtc, Kwh, SampledSeconds)` per hour, as `numeric`, through
  `Database.SqlQuery<WaterHeaterHourEnergy>` with parameters (no string building). Oslo hours are whole UTC hours, so
  UTC buckets match the cost hours exactly.
- **Rationale**: The frontend's year view asks for two years of daily figures, which is about 1 M samples. Moving
  ≤ 17,520 hourly rows instead is the difference between a fast answer and a slow one (SC-004). `LEAD` has no LINQ
  equivalent in EF Core. The database already depends on PostgreSQL and TimescaleDB (the consumption continuous
  aggregates), so one PostgreSQL query doesn't change the provider situation.
- **Alternatives**:
  - Load the samples and integrate them in C#. This is simplest to test and fine for a day or a month (≤ 45,000 rows),
    but too slow for the year view. It is the fallback if the SQL proves hard to get right: load it only for ranges
    up to one month, and leave the heater part out of longer daily ranges.
  - A stored hourly rollup filled by the sampler. Reads would be fastest, but it adds a table, a migration, a job, a
    backfill and recompute rules. Keep it in reserve in case quickstart §3 misses SC-004.
  - A TimescaleDB continuous aggregate. Window functions aren't allowed in continuous aggregates.

## R3. Sharing the hour's cost

- **Decision**:
  - **kWh:** `heaterKwh = min(energy.Kwh, hour.ConsumptionKwh)`, and `capped = energy.Kwh > hour.ConsumptionKwh`.
  - **Cost:** `cost = hour.Cost.Actual?.Cost × heaterKwh / hour.ConsumptionKwh`. It is `null` when the hour has no
    actual cost, and `0` when the hour's kWh is 0 (the heater is then capped to 0).
  - **No rounding** in the API: amounts stay full-precision `decimal`, like the other cost fields.
- **Rationale**:
  - The hour's own cost already includes the Norgespris cap split, exemptions and VAT. A share of it gives the heater
    exactly the hour's average price per kWh (spec FR-003), so heater ≤ total and total − heater is never negative.
  - With full precision, the client's "rest" (total − heater) agrees with the total to the øre once both are rounded
    the same way (SC-001).
- **Alternatives**: Pricing the heater's kWh with `RatePerKwh` directly. That gives the same result for a hour without
  a cap split, but needs a separate rule for a split hour.

## R4. Days and the month

- **Decision**: `WaterHeaterShare.Sum(parts)` adds up kWh and sampled minutes, and ORs the capped flags. A day or
  month whose hours have no heater part gets `null`, never zeros.
  - **Cost:** the sum of the hour costs, or `null` if any hour with a heater part has a `null` cost. This is the same
    rule as `CostMapper.SumCost` for the day's own cost.
  - **The bill:** also `firstHourUtc` and `lastHourUtc` of the hours with a heater part. It uses only `soFar` (hours
    up to the current hour, or the whole month once it has ended), and `estimate: false`.
- **Rationale**: FR-005 and FR-006 ("sums of its hours, never a separate calculation"; "not projected"). The first and
  last hour let the client say "since 19 November" without a second request.
- **Alternatives**: Projecting the heater to the end of the month. That would need a heater profile on top of the
  house profile, and was left out (spec US2 scenario 2).

## R5. The current hour

- **Decision**: The hourly endpoint already returns the current hour so far. The SQL clips each hold at `now`
  (passed in from the handler's clock), so the current hour's heater part covers only the minutes so far (US1
  scenario 7). The current-hour endpoint (`/electricity-cost/current`) is unchanged; the frontend doesn't need it for
  this.
- **Rationale**: This keeps the same "so far" meaning as the hour's kWh.
- **Alternatives**: Adding the heater to `/current` too. Nothing asks for it yet (Principle VIII).

## R6. Who pays for the extra query

- **Decision**: `HourlyCostProvider.GetAsync(..., bool withWaterHeater = false)`. The hourly, daily and bill
  handlers pass `true`; the Norgespris saving handler doesn't.
- **Rationale**: Saving is computed over long ranges and never shows the split. One extra indexed query per request
  is otherwise cheap.
- **Alternatives**: Always load it. That's simpler, but makes the saving card pay for something it never uses.
