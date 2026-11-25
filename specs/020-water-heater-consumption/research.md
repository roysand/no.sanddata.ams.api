# Research: Hot Water Used per Hour, Day and Month

Spec: [spec.md](spec.md). The Technical Context has no open "NEEDS CLARIFICATION". The spec leaves three details of
the counting rule to be settled against real data. R5 gives a starting value for each and R6 says how they are
checked, so nothing blocks the build.

## R1. Where the litres are stored

**Decision**: A nullable `AvailableLitres40` (`int?`) column on the existing `WaterHeaterPowerSample`, filled by the
same sampler in the same row as the power. One migration adds the column, with no default and no backfill, so older
rows have `NULL` (FR-002). The entity and table keep their names.

**Rationale**:
- The counting rule needs the power and the litres at the same moment, because the heating in a pair of samples
  corrects that pair's fall. One row with both gives that without a join on time.
- The sampler already writes one row a minute per heater. One more column is the smallest change.
- Renaming the table to "sample" would touch spec 012's and 017's SQL, the repository, the migrations and the tests,
  for a name only.

**Alternatives considered**:
- *A separate litres sample table.* That needs a second insert per minute and a join on near-equal times.
- *Renaming the entity to `WaterHeaterSample`.* That's churn with nothing gained (Principle VIII).

## R2. Reading the litres from OSO

**Decision**: A new client method, `GetSampleAsync(credentials)`. It calls `ActualLoad` and `Status` in parallel
and returns a `WaterHeaterSample(PowerKw, Litres40, LitresError)`:

- If `ActualLoad` fails, the whole sample fails, exactly as today (spec 011's rules and logging).
- If `Status` fails or has no `availableMix40Volume`, the sample still succeeds with `Litres40 = null`, and the error
  goes in `LitresError` (FR-001, US1 scenario 2).

The sampler calls `GetSampleAsync` instead of `GetActualLoadAsync`. `GetActualLoadAsync` has no other caller in
`src`, so it is removed and its tests move to the new method.

**Rationale**:
- `Status` is already parsed by `GetStateAsync` and was checked against the real heater on 2026-11-19 (spec 011). Its
  `availableMix40Volume` is the number the OSO app and our state endpoint show.
- Running the two calls in parallel keeps a sample close to one moment and inside the 10-second client timeout.
- Keeping the power when the litres fail means spec 012's and 017's figures can't get worse because of this feature.

**Alternatives considered**:
- *OSO's `AvailableMixV40Volume` call.* It's a smaller answer, but its JSON shape hasn't been checked against the real
  heater. Spec 011 chose `Status` for the same number.
- *Making the whole sample fail when either call fails.* That would lose power samples (FR-001).

## R3. Logging when the litres can't be read

**Decision**: A second `SampleFailureTracker` in the sampler, for litres only. It has three new log messages with
the same rate limit as the power failures (once at the start, at most hourly while it goes on, once on recovery):

| EventId | Name | Level |
|---|---|---|
| 1834 | `WaterHeaterLitresFailing` | Warning |
| 1835 | `WaterHeaterLitresStillFailing` | Warning |
| 1836 | `WaterHeaterLitresRecovered` | Information |

They carry the location id and the reason code (`LogMessages.Reason`), never the device id or key.

**Rationale**: If the litres were missing without a log line, the gap would only show weeks later as an empty chart.
SC-005 (95 % of minutes with litres) needs it to be visible. The tracker already exists and is pure, so a second
instance costs nothing new.

**Alternatives considered**: no log. That's cheaper, but a parsing change at OSO would go unseen.

## R4. Counting the water used, in SQL per UTC hour

> **Replaced on 2026-11-25 by [R10](#r10-counting-each-hour-as-an-energy-balance).** Production showed that OSO's
> figures change in steps, so pairs of samples lost most of the water drawn while the heater ran.

**Decision**: One new raw SQL query, `GetHourlyHotWaterAsync(locationId, fromUtc, toUtc, nowUtc, rule)`, in
`WaterHeaterPowerSampleEfRepository`. It returns one row per UTC hour that has any counted pair:
`(HourStartUtc, Litres, SampledSeconds)`.

- **Pairs:** each sample `b` with litres is paired with the sample `a` just before it (`LAG` over
  `SampledAtUtc`). The pair counts when `a` also has litres and `b − a` is at most 2 minutes (spec 012's hold
  limit).
- **Used litres:** with `fall = a.litres − b.litres` and
  `heated = a.power × (b − a) seconds / 3600 / rule.KwhPerLitre`:
  `used = fall + heated` when `fall ≥ rule.MinFallLitres`, otherwise 0 (FR-004).
- **Sampled seconds:** `b − a` for every counted pair, also when it uses 0. A rise is sampled time with no water
  used, not a gap.
- **Bucket:** the UTC hour of `b`, the later sample (spec, Edge Cases).
- **Window:** pairs whose `b` is in `[from, min(to, now))`. The read starts 2 minutes before `from`, so the first
  pair has its `a`.

The handler then adds the UTC hours up to Oslo days or months in C# (R7).

**Rationale**:
- The hold rule for the heating is spec 012's: the power of `a` holds until `b`, which is at most 2 minutes later.
  The heating in a pair is therefore exactly the heater kWh that spec 012 counts for that time.
- A year is about 525,000 samples, and loading them into C# would miss SC-004. `LAG` has no LINQ equivalent. The
  hourly result is at most 8,784 rows a year.
- Bucketing by hour in SQL and by Oslo day or month in C# keeps the time zone rules where the rest of the repo has
  them. Oslo hours are whole UTC hours, so the hour buckets are exact for every Oslo part.
- The rule's numbers are parameters, not literals in the SQL, so calibration (R6) changes one C# constant.

**Alternatives considered**:
- *Reuse `QueryStepsAsync` (spec 017).* It splits holds at boundaries and has no litres. Its question is "power over
  time", not "change between two samples". Forcing both into one query would make the hardest SQL in the repo harder.
- *`date_trunc(..., 'Europe/Oslo')` for days and months in SQL.* That works in PostgreSQL, but it would be the only
  place where Oslo time lives in SQL. Rolling up 8,784 rows in C# is instant.
- *Load the samples and count in C#.* Simplest to test, but too slow for a year (as in spec 012 R2).

## R5. The rule's starting values

> **Changed on 2026-11-25 by [R10](#r10-counting-each-hour-as-an-energy-balance):** `KwhPerLitre` is 0.04, and
> `MinFallLitres` became `MinHourLitres` 3.

**Decision**: One static class in the feature, `HotWaterRule`, holds the values the handler passes to the query:

| Value | Start | Why |
|---|---|---|
| `KwhPerLitre` | 0.035 | Heating 1 L of water from 10 °C to 40 °C: 4.186 kJ/(kg·K) × 30 K = 125.6 kJ ≈ 0.0349 kWh |
| `MinFallLitres` | 2 | A fall of 1 L in a minute is what standby cooling and OSO's whole-litre rounding give. A tap that is really running draws at least 2–3 L of 40 °C water a minute. |

The pairing rule is FR-004 as written: consecutive samples, at most 2 minutes apart.

**Rationale**:
- 10 °C is a typical cold-water temperature in Norway over the year. The element's power all ends up in the water.
- At 3 kW the heater makes 3 / 60 / 0.035 ≈ 1.4 L a minute. A shower draws about 8–10 L a minute. So the correction
  is 15–20 % of a shower taken while heating, which SC-002 tests.
- Without a minimum fall, standby losses would add one litre now and then all day: about 1–2 L an hour, right at
  SC-003's limit.

**Alternatives considered**:
- *Derive the kWh per litre from OSO's own stored kWh ÷ available litres.* That ratio is OSO's model of the tank, but
  it depends on how much energy sits below 40 °C. R6 checks it against 0.035 instead of building on it.
- *A minimum fall of 1 L.* It catches more small draws but counts standby losses. R6 decides between 1 and 2.

## R6. Checking the rule against the OSO app

> **Changed on 2026-11-25:** the endpoint shipped before the calibration (PR #74), and point 1's check led to
> [R10](#r10-counting-each-hour-as-an-energy-balance). Points 2 and 3 still apply, to `MinHourLitres` and
> `KwhPerLitre`.

**Decision**: Ship story 1 (storing the litres) on its own first. After 3–5 days of samples, run the check in
[quickstart.md §4](quickstart.md#4-calibration-against-the-oso-app) and write the results at the end of this file
under "Calibration results":

1. **How often OSO's litres change.** Count the share of consecutive pairs with equal litres during a known shower. If
   OSO updates its figure only every few minutes, the drop arrives in one step, and the heating in the flat minutes
   before it is lost. In that case the pairs become "from the last change to this change", still with at most
   2 minutes between any two samples in between, and the heating is summed over the whole run. This changes only the
   SQL's pairing CTE.
2. **`MinFallLitres`.** Count the water used in quiet night hours (no draws) with 1 and 2. Pick the smallest that keeps
   SC-003 (at most 2 L in an idle hour).
3. **`KwhPerLitre`.** Compare OSO's `TappingCapacityKwh ÷ availableMix40Volume` at a few moments with 0.035, and the
   day totals with the OSO app's "Total v40 used" (SC-001). Change the figure only if the day totals are off in one
   direction on most days.

**Rationale**: OSO doesn't document how often its figure changes or how its own chart counts. Five days of real
data answer that better than guessing, and the counting can be built in the meantime with the starting values.

**Alternatives considered**: shipping the endpoint with the starting values and never checking. That leaves SC-001
untested.

## R7. Periods, parts and the answer

**Decision**:
- **Request:** `GET /api/locations/{locationId}/water-heater/hot-water?period=day&date=2026-11-21`.
  - `period` is one of `day`, `week`, `month`, `year`, without regard to case. It defaults to `day`.
  - `date` is an Oslo calendar date (`yyyy-MM-dd`) and defaults to today in Oslo.
- **Period:**
  - day: that date;
  - week: the ISO week holding it, Monday to Sunday;
  - month: its calendar month;
  - year: its calendar year.

  Start and end are Oslo midnights, converted to UTC.
- **Parts:**
  - day: each UTC hour of the period, which gives 23, 24 or 25;
  - week and month: each Oslo day;
  - year: each Oslo month.

  Parts that start at or after now are left out (FR-006). A period wholly in the future answers with no parts and a
  missing total. That is not an error, the same as a period before the litres were stored.
- **Per part:** `litres` is the sum of the part's hours, rounded half away from zero to a whole number. It is `null`
  when no hour of the part has sampled time. `sampledMinutes` is the summed seconds, rounded down to whole minutes,
  as in spec 012.
- **Total:** the sum of the parts' rounded `litres`, so the chart's bars add up to the figure above it. It is `null`
  when every part is `null`. `sampledMinutes` for the period is given too.
- **Validation:** an unknown `period`, or a `date` that is not a real date, gives a 400 from the validator. The
  handler only fills in the defaults (Principle IV).

**Rationale**:
- The four periods and their steps match the OSO app (spec Assumptions).
- ISO weeks start on Monday, as Norwegian calendars do.
- Listing parts that have no data, with `null`, lets the frontend draw a fixed axis (24 hours, 7 days, 12 months)
  without filling in the gaps itself. Spec 017 left steps out instead, but its chart is a line over time, not a row of
  bars.
- Totalling the rounded parts avoids a total that is one litre off the bars.

**Alternatives considered**:
- *`from`/`to` with a step, like spec 017.* That's more flexible, but every client would then have to rebuild the
  Oslo week and month rules. The spec asks for the four periods only.
- *Leaving out parts without data.* Then the client has to work out the missing hours, including on days when the
  clocks change.

## R8. Who may read it

**Decision**: The same check as spec 017 (its research R4), copied into the new handler:

1. An Admin needs an existing location; anyone else needs `IsUserAssociatedAsync`.
2. Otherwise the answer is `404 Location.NotFound`, logged with the existing `LocationAccessDenied` (EventId 1805).

**Rationale**: The spec asks for exactly the same rule (US3). Copying a few lines keeps the slices independent
(Principle II), as spec 017 did.

## R9. Speed (SC-004)

- **A day:** about 1,440 samples through the existing index `(LocationId, SampledAtUtc)`.
- **A year:** about 525,000 samples in one index range scan with a `LAG` window, grouped into at most 8,784 rows.
  Spec 012's year view runs a query of the same shape (`LEAD` over the same rows) within its own target, so a year
  should answer within SC-004's 1 second.
- **Fallback if it doesn't:** quickstart §5 measures it on production. If a year misses SC-004, the next step is a
  covering index `(LocationId, SampledAtUtc) INCLUDE (PowerKw, AvailableLitres40)`. A stored daily rollup comes after
  that.

## R10. Counting each hour as an energy balance

**Decision** (2026-11-25, with Roy): count each UTC hour as a whole, with no pairs of samples:

> `used = litres at the start − litres at the end + heated kWh ÷ KwhPerLitre`

- **Start:** the last litres of the hour before. When the hour before has no litres, the hour's own first litres.
- **End:** the hour's last litres.
- **Heated kWh and sampled time:** the hour's power over time by spec 012's hold rule, the same figures as the heater's
  kWh view (`GetHourlyEnergyAsync`).
- **Floor:** an hour under `MinHourLitres` counts 0. That covers standby cooling and a negative result.
- **Values:** `KwhPerLitre` 0.04 and `MinHourLitres` 3, from the first calibration results below.

The SQL only groups the samples with litres into first and last litres per UTC hour, starting one hour before `from`.
`HotWaterBalance` in the Application layer does the counting, so it is unit-tested with Roy's real hours.

**Rationale**:
- OSO's power and litres change in steps every 2–10 minutes (calibration results, point 1). Within an hour the steps
  cancel out: the balance needs only the litres at the hour's edges and the energy put in.
- Water drawn while the heater runs is counted even when the litres rise. The pairing rule dropped such minutes, so the
  first day counted 54 L against OSO's 106 L, and 3 L against OSO's 22 L in the hour after a shower.
- With 0.04, the balance gives OSO's figures for the four hours checked. OSO most likely counts the same way.

**Alternatives considered**:
- *Pairs from one change to the next (R6, point 1).* It still needs a minimum fall per run, and a run while heating
  can rise. The hour's balance has neither problem.
- *The balance per minute or per 5 minutes.* The steps don't cancel out in so short a time, and the chart shows hours
  at the finest.
- *The balance over the whole period at once.* It's simplest, but standby cooling over a day would then add up to
  litres used.

**Speed (SC-004):** a year is now two queries. One is spec 012's power query. The other is a `GROUP BY` hour with
`array_agg` over the same index range, at most 8,784 rows. R9's fallback still applies.

## Calibration results

_Filled in after R6's check, with the dates of the samples used, the values chosen and the day totals compared._

**1. How often OSO's litres change (T029), 2026-11-24.** Production samples taken while a hot tap was running: the
litres drop every minute. So the pairing stays as R4 describes: each sample with the one just before it. There is no
switch to runs.

**1, corrected on 2026-11-25.** A longer look shows steps. Roy listed the samples for 09:00–11:00 and 17:00–19:00
Oslo time on 2026-11-25. Power and litres both stay the same for 2–10 minutes and then jump. For example, 324 L lasted
from 09:49 to 09:57, and while heating the litres rose 4–6 L at a time. The tap of 2026-11-24 was strong enough to
make the steps look like minutes. This led to R10.

**First day against the OSO app, 2026-11-25** (Hjemme, `KwhPerLitre` 0.04; the kWh are the hours' mean power):

| Oslo hour | Litres start → end | Heated | Balance | OSO app | Pairing rule (PR #74) |
|---|---|---|---|---|---|
| 09 | 358 → 320 | 0.48 kWh | 50 L | ~50 L | 40 L |
| 10 | 320 → 348 | 2.00 kWh | 22 L | ~22 L | 3 L |
| 17 | 344 → 339 | 0.97 kWh | 29 L | ~26 L | 9 L |
| 18 | 339 → 361 | 1.16 kWh | 7 L | ~8 L | 0 L |
| **Day** | | | **108 L** | **106 L** | **54 L** (with 2 L at 00) |

- With `KwhPerLitre` 0.035 the four hours give 123 L, so 0.04 is used.
- The 17 o'clock shower shows why the heating counts: the litres fell only 5 L, but about 29 L were drawn.
- About 2 L move between hours 17 and 18. OSO's steps blur the hour's edges, but the day total is unaffected.
- Idle cooling: 358 → 356 in 36 minutes in the morning, 361 flat for 25 minutes in the evening. That's at most
  2–3 L an hour, so `MinHourLitres` is 3.
- One day is not five. SC-001 (4 of 5 days within 15 %), SC-003 (quiet nights, T030) and OSO's own ratio (T031) are
  still to be checked with the new rule.

**T012, 2026-11-24.** Checked in production, not locally. After PR #66 was deployed, new samples have
`AvailableLitres40`, and the litres fall while a hot tap runs.
