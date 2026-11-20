# Research: The Water Heater's Power over the Last Hours

Spec: [spec.md](spec.md). The Technical Context had no open "NEEDS CLARIFICATION". The points below are the design
choices it rests on.

## R1. One SQL query for both step sizes

**Decision**: Turn the body of spec 012's `GetHourlyEnergyAsync` into one private query that takes the step as a
parameter (`interval`). It puts each hold in a step with `date_bin(step, t, 'epoch')` in place of
`date_trunc('hour', t, 'UTC')`. `GetHourlyEnergyAsync` calls it with 1 hour and keeps its signature and results.
The new `GetPowerStepsAsync` calls it with 5 minutes.

**Rationale**:
- The hold rule is the same: until the next sample, at most 2 minutes, never past now or `to`, and split across a
  boundary. With one query it can't drift between the hourly and 5-minute views. SC-002 (the 5-minute kWh adds up to
  the hour's) then holds by construction, not by testing.
- `date_bin` with origin `1970-01-01 00:00 UTC` gives whole hours and whole 5 minutes in UTC. For a 1-hour step that
  is exactly `date_trunc('hour', t, 'UTC')`, so the hourly results don't change (FR-006).
- `date_bin` needs PostgreSQL 14 or newer. Local runs PG16 (`compose.db.yaml`) and production PG17
  (`compose.prod.yaml`).
- The `parts` CTE splits a hold in at most two, which needs a step of at least 2 minutes (the longest hold). 5 minutes
  and 1 hour both qualify. The comment on the private method says so.

**Alternatives considered**:
- *A second, copied query with a fixed 5-minute step.* Leaves the hourly query alone, but puts two copies of the
  trickiest SQL in the repo in the same class, and SC-002 would then rest on the two staying identical.
- *Load the samples and bucket them in C#.* A 48-hour window is about 2,900 samples, so it's affordable. But it would
  be a second copy of the hold rule in another language, and spec 012 already chose SQL for it.
- *A stored 5-minute rollup.* Adds a table, a job and a backfill for data that a 3-hour query reads in milliseconds.

## R2. Average power over the sampled time, and the sampled seconds

**Decision**: Per step: `powerKw = Σ(p × held seconds) / Σ held seconds`, rounded to 3 decimals, and
`sampledSeconds = Σ held seconds`, rounded to a whole second. A step whose sampled time rounds to 0 is left out.

**Rationale**:
- The spec asks for the average over the sampled time, so a gap doesn't pull the power down.
- The query already returns kWh and seconds per step (`WaterHeaterHourEnergy` for hours). The power is
  `kWh × 3600 / seconds`, so the query's shape doesn't change. The 5-minute rows get their own record,
  `WaterHeaterPowerStep`, so a field name never calls a 5-minute figure an hour.
- With 3-decimal kW, rounding moves an hour's kWh by at most 12 × 0.0005 kW × 300 s / 3600 = 0.0005 kWh. That is
  inside SC-002's 0.001 kWh.
- A sub-second sliver can only come from the edges of the window (`from`, or now). It carries no power worth drawing,
  and keeps the spec's 1–300 range true.

**Alternatives considered**: decimal `sampledSeconds`. That's more precise than the client needs, and the spec says
whole seconds.

## R3. The window and its defaults

**Decision**:
- `to` left out means now. `from` left out means `to` minus 3 hours.
- Both are converted to UTC and refused if:
  - `from` is not before `to`,
  - the window is longer than 48 hours,
  - `from` is after now.
- `to` may be in the future: holds stop at now anyway.
- `from` is not rounded to whole 5 minutes. A `from` at 10:02 gives a first step starting 10:00 with at most
  180 sampled seconds. The frontend sends a whole 5 minutes, as it does for the meter.

The defaults and checks are in one small static helper in the feature, `PowerWindow`. The validator uses it to refuse
bad windows and the handler to get the window. `TimeProvider` gives "now" in both: the validator resolves it with
FastEndpoints' `Resolve<TimeProvider>()`, and the handler receives it in its constructor.

**Rationale**: the validator must know "now" to refuse a `from` in the future (FR-004), and the handler needs the same
defaults. One helper keeps them from drifting. Validating at the boundary (Principle IV) stays true: the handler only
fills in defaults.

**Alternatives considered**: Check "starts in the future" in the handler. That would split validation between two
places and break Principle IV.

## R4. Who may read it

**Decision**: The same check as spec 016's charging sessions:
`IsAdmin || IsUserAssociatedAsync(user, location)`, then a location that does not exist gives `404 Location.NotFound`.
Anyone not allowed gets the same 404, and the existing `LocationAccessDenied` log line (EventId 1805) records it.

**Rationale**:
- `IsUserAssociatedAsync` covers owners and viewers of an active location.
- The repo's rule is that an inactive location behaves as nonexistent for regular users (comment on
  `LocationEfRepository.IsUserAssociatedAsync`). So "those who may see it" for an inactive location (spec, Edge Cases)
  means Admins.
- This needs no new rule.

**Alternatives considered**: Let owners read an inactive location's history. That would be the first exception to the
inactive rule, and the spec doesn't ask for it.

## R5. Route and placement

**Decision**: `GET /api/locations/{locationId}/water-heater/power?from=&to=` in `Features/WaterHeaters`.

**Rationale**: It sits next to `GET /api/locations/{locationId}/water-heater` (live state), uses the same tag
`WaterHeater` in Scalar, and has the same shape as the charger's `/ev-charger/sessions`. It needs no connected heater:
the samples belong to the location (spec 011), so a removed heater's past windows still answer.

**Alternatives considered**: a query on `/api/electricity-cost/...`. That area is about cost and hourly steps. This
endpoint has neither.

## R6. Speed (SC-003)

A 3-hour window reads about 180 samples (plus 2 minutes before `from`) through the existing index
`(LocationId, SampledAtUtc)`, and a 48-hour window about 2,900. The query is one index range scan and a window
function, well under the 300 ms p95. No cache and no new index are needed.
