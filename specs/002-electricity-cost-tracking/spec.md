# Feature Specification: Electricity Cost Tracking

**Feature Branch**: `002-electricity-cost-tracking`

**Created**: 2026-09-27

**Status**: Draft

**Input**: User description: "As a logged-in user, I want to see what my electricity usage is
costing me in NOK, not just raw power/energy numbers, including a live-updating view of cost
accrued so far in the current (still in-progress) hour, so I can understand my spending in
near-real-time as well as historically by hour and day."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See cost accrued so far this hour (Priority: P1)

A household user wants to check, at any moment, how much their electricity use has cost them
since the top of the current hour — a live, continuously-accurate figure rather than one that
only appears once the hour is over.

**Why this priority**: This is the specific capability that motivated the feature — turning raw
power readings into a spending number the user actually cares about, updated as they watch,
without waiting for the hour to close.

**Independent Test**: Can be fully tested by checking the current-hour cost figure at two
different points within the same hour and confirming it has increased (or stayed flat if no
further consumption occurred), reflecting consumption received since the top of the hour.

**Acceptance Scenarios**:

1. **Given** a user with an active location and ongoing measurement data, **When** they check
   the cost for the current hour partway through, **Then** the figure reflects consumption from
   the start of that hour up to now, priced at that location's applicable rate for that hour.
2. **Given** the current hour has only just started, **When** the user checks the cost, **Then**
   the figure shows at or near zero rather than an error.
3. **Given** the location uses the spot-price model and the price for the current hour has not
   yet been retrieved, **When** the user checks the cost, **Then** they see a clear "not yet
   available" indication rather than an incorrect or missing number.

---

### User Story 2 - Browse historical cost by hour and by day (Priority: P2)

A household user wants to look back at completed hours and days to see what their electricity
actually cost, to compare against their bill or understand spending patterns over time.

**Why this priority**: Builds on User Story 1 by extending the same cost concept to completed
periods; valuable for understanding trends but not needed for the core "watch it happen live"
experience.

**Independent Test**: Can be fully tested by requesting the cost for a specific completed hour
and a specific completed day and confirming the day's figure equals the sum of that day's hourly
figures.

**Acceptance Scenarios**:

1. **Given** a user with completed hours of cost history, **When** they request cost for a
   specific past hour, **Then** they see that hour's consumption, the rate applied, and the
   resulting cost.
2. **Given** a user requests cost for a full day, **When** the request is made, **Then** the
   returned total equals the sum of that day's individual hourly costs.
3. **Given** a user requests cost for a period before this feature existed, **When** the request
   is made, **Then** they see a clear indication that no cost data exists for that period, not an
   error or a zero that looks like a real value.

---

### User Story 3 - View consumption trends independent of cost (Priority: P3)

A household user wants to see their energy consumption (kWh) at a fine-grained, minute-by-minute
level, separate from cost, to understand usage patterns (e.g. spotting what caused a spike).

**Why this priority**: A supporting capability — minute-level data is what makes the live
current-hour cost figure possible, so exposing it directly for charting is a small additional
step once it exists, but it's not the primary value of the feature.

**Independent Test**: Can be fully tested by requesting minute-level consumption for a location
over a short recent window and confirming values are returned per minute.

**Acceptance Scenarios**:

1. **Given** a user with ongoing measurement data, **When** they request minute-level consumption
   for the last hour, **Then** they receive one consumption value per minute for that window.

---

### User Story 4 - Compare cost under both pricing models (Priority: P4)

A household user wants to see, alongside their actual cost, what the same consumption would have
cost under the *other* pricing model (the government fixed rate vs. the electricity spot price),
so they can judge which pricing arrangement suits them better.

**Why this priority**: A nice-to-have comparison layered on top of the core cost-tracking
capability — useful for decision-making, not required for the feature's primary purpose of
showing the user's actual cost.

**Independent Test**: Can be fully tested by requesting cost for an hour and confirming both the
location's actual (enrolled) cost and the alternative model's cost for the same consumption are
returned together.

**Acceptance Scenarios**:

1. **Given** a user views cost for an hour, **When** the response is returned, **Then** it
   includes both the cost under the location's actual pricing model and what the same
   consumption would have cost under the other pricing model.
2. **Given** the alternative model's required data (spot price or exchange rate) is not
   available for that hour, **When** the comparison is requested, **Then** the actual cost is
   still shown, with the comparison figure marked unavailable rather than failing the whole
   request.

---

### Edge Cases

- What happens when the external price source hasn't published a price for the current or an
  upcoming hour yet (e.g. source is late or unreachable)?
- What happens when the daily exchange rate hasn't been retrieved yet?
- What happens for an existing location that doesn't yet have a price region assigned (from
  before this became a required field)?
- What happens when there's a gap in measurement data during part of an hour (e.g. the sensor or
  its network link was offline) — cost should reflect only the data that exists, not error out.
- What happens when a user requests cost/consumption for a location they are not associated
  with? (Must follow the same authorization behavior as existing measurement data access — no
  indication of whether the location exists.)

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST compute electricity cost for a given hour as that hour's energy
  consumption (in kWh) multiplied by that location's applicable rate (in NOK per kWh) for that
  hour.
- **FR-002**: System MUST provide a live cost figure for the current, still-in-progress hour,
  reflecting consumption received up to the moment of the request — not only after the hour
  completes.
- **FR-003**: System MUST provide energy consumption at minute and hour granularity.
- **FR-004**: System MUST provide daily cost and daily consumption as the sum of that day's
  hourly cost/consumption values, not as an independently computed figure.
- **FR-005**: System MUST retrieve day-ahead hourly electricity spot prices, per price region,
  from an external price source, ahead of the hours they apply to.
- **FR-006**: System MUST retrieve daily currency exchange rates from an external source and use
  them to convert electricity spot prices into NOK.
- **FR-007**: System MUST require every location to have a price region assigned, used to
  determine its applicable spot-price rate.
- **FR-008**: System MUST support two pricing models per location: a flat government-set rate
  that stays constant throughout the day and across days, and the converted hourly spot-price
  rate; each location's cost MUST be calculated using whichever model it is enrolled in.
- **FR-009**: System MUST allow a user to see what their consumption would have cost under the
  pricing model they are *not* enrolled in, alongside their actual cost, when the data needed for
  that comparison is available.
- **FR-010**: System MUST scope all cost and consumption data to locations the requesting user
  is associated with, using the same authorization rules as existing measurement data access
  (no indication of existence for locations the user isn't associated with).
- **FR-011**: System MUST clearly indicate when a cost figure (actual or comparison) is
  unavailable (required price or exchange rate not yet retrieved) rather than showing an
  incorrect or misleading number.
- **FR-012**: System MUST NOT compute cost for measurement data from before this feature became
  available — cost tracking applies going forward only.

### Key Entities *(include if feature involves data)*

- **Electricity Price**: The day-ahead spot price for one price region for one hour, as published
  by the external price source.
- **Exchange Rate**: The currency conversion rate for one day, used to convert electricity spot
  prices into NOK.
- **Energy Consumption**: Energy used (kWh) by a location over a time bucket (minute or hour),
  derived from existing raw power measurements.
- **Hourly Cost**: The computed monetary cost for one location for one hour, under one pricing
  model — consumption for that hour multiplied by that model's applicable rate.
- **Location** *(existing)*: Gains a required price region (already has a `Zone` field, e.g.
  "NO1" for Oslo) and continues to use its existing flag to indicate enrollment in the flat-rate
  government scheme rather than the spot-price market. A future, separate capability may add a
  reference table of valid price regions; out of scope here.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can see the cost accrued so far in the current hour at any time, reflecting
  consumption data that is no more than 2 minutes old.
- **SC-002**: A user can retrieve cost and consumption for any completed hour or day since this
  feature launched, with the daily figure always exactly equal to the sum of its 24 hourly
  figures.
- **SC-003**: Displayed cost figures match consumption × applicable rate exactly (within standard
  currency rounding), verified against manual calculation, 100% of the time, for both pricing
  models.
- **SC-004**: A user is never shown a cost number for an hour whose required rate data is
  missing — they see an explicit "not available" state instead, 100% of the time.
- **SC-005**: A user can never retrieve cost or consumption data for a location they are not
  associated with.

## Assumptions

- Displayed cost reflects only the electricity rate itself (spot price or the flat government
  rate, each converted/including their respective tax component); it does not include grid/
  network fees or other charges that appear on a real electricity bill — those are out of scope
  for this feature.
- The flat government rate and its tax component are configurable values (not hardcoded), since
  the government-set rate can change over time; exact current values are a planning/
  implementation detail, not specified here.
- Cost and consumption are computed per location (summing all of a location's meters), matching
  the existing domain model where price region and pricing-model enrollment live on the
  Location, not the individual Meter.
- Every location is expected to have a price region going forward; existing locations created
  before this requirement may need their price region backfilled as a data-migration concern,
  separate from this feature's own scope.
- This feature depends on the existing measurement data (from the already-built measurement
  ingestion and query capability) but does not modify it — it is a new, independent capability
  built on top of that data.
