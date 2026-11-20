# Feature Specification: The Water Heater's kWh and Cost, Split from the Rest

**Feature Branch**: `feature/012-water-heater-energy`

**Created**: 2026-11-19

**Status**: Draft

**Input**: Follows spec 011 (OSO water heater, deployed 2026-11-19), whose User Story 3 stores the heater's power about
once a minute "so the heater's kWh per hour/day can be shown later". Roy's real heater at Hjemme was connected and is
being sampled since the evening of 2026-11-19. User description: "Implement the heater's kWh and cost per day. These
values I want integrated with the other numbers so I can see what is heating of water and the rest of the electricity
used (mostly heating and a little cooking, washing and drying clothes)." Choices made with Roy the same evening: the
split is shown in the usage chart (Now and History), the monthly bill, the Heater tab and the CSV export; the water
heating cost is its share of the price per kWh only (the capacity charge and monthly fees are not split); heating in
the cheapest hours is on hold.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Every hour and day says how much of it was water heating (Priority: P1)

A household member looks at the location's hourly or daily use and cost, as the app already shows it. For a location
with a water heater, each hour and each day now also says how many of its kWh the heater used and what those kWh
cost, so the app can show "water heating" and "everything else" side by side. The two always add up to the hour's or
day's total, which stays exactly what it is today.

**Why this priority**: This is the feature: without the split in the figures the app already reads, nothing can be
shown, and every other story builds on it.

**Independent Test**: For Hjemme, ask for the hourly figures of a day after the heater was connected. Hours where the
heater ran show its kWh (for example 2,1 of the hour's 3,4 kWh) and its cost; the rest is the hour's total minus that.
Add the heater kWh of the day's hours by hand and compare with the day's heater kWh in the daily figures: they match.

**Acceptance Scenarios**:

1. **Given** a location with a heater and an hour in which every minute was sampled, **When** the hourly figures are
   read, **Then** the hour carries the heater's kWh for that hour, the cost of those kWh, and that 60 of 60 minutes
   were sampled.
2. **Given** an hour where some samples are missing, **When** it is read, **Then** the heater's kWh covers only the
   sampled minutes (missing minutes are not guessed), and the number of sampled minutes says how complete it is.
3. **Given** the heater's cost for an hour, **When** it is compared with the hour's cost, **Then** it is the hour's
   cost times the heater's share of the hour's kWh, so the heater pays the same price per kWh as the rest of the hour,
   including how the Norgespris monthly cap splits that hour.
4. **Given** a day, **When** the daily figures are read, **Then** the day's heater kWh, cost and sampled minutes are the
   sums of its hours, never a separate calculation.
5. **Given** an hour before the heater was connected, or a location that never had one, **When** it is read, **Then**
   it carries no heater part at all (not zero), so "unknown" is never shown as "the heater used nothing".
6. **Given** an hour where the samples add up to more kWh than the meter measured (sampling and metering are separate
   devices), **When** it is read, **Then** the heater's kWh is capped at the hour's kWh, so "everything else" is never
   negative, and the hour is marked as capped.
7. **Given** the current hour, **When** it is read, **Then** the heater part covers the minutes so far, like the
   hour's own figures.

---

### User Story 2 - The monthly bill says how much was water heating (Priority: P1)

A household member opens the bill for a month. Next to the total it says how many kWh the water heater used in the
month so far and what those kWh cost, so they can see what heating water costs against everything else.

**Why this priority**: The bill is where cost is judged; Roy asked for the split there explicitly.

**Independent Test**: For Hjemme, read the bill for November 2026. It shows the heater's kWh and cost for the hours
since it was connected, and how many minutes were covered by samples; the heater's cost equals the sum of its hourly
costs.

**Acceptance Scenarios**:

1. **Given** a month with heater samples, **When** the bill is read, **Then** it carries the heater's kWh and cost for
   the month's passed hours, the sampled minutes, and the first and last hour that had samples.
2. **Given** the current month (an estimate), **When** the bill is read, **Then** the heater part covers only what has
   happened so far; it is not projected to the end of the month, and it says so.
3. **Given** the heater's cost in the bill, **When** it is compared with the bill's lines, **Then** it is part of the
   energy cost (power and grid energy part, taxes and VAT), never of the capacity charge or the monthly fees, and it
   does not change any amount on the bill.
4. **Given** a month without any samples, **When** the bill is read, **Then** it carries no heater part.

---

### Edge Cases

- **Gaps in sampling** (OSO down, API restarted, key rejected): a missed minute or two is bridged by the previous
  sample; a longer gap counts as not sampled. The day still shows what was sampled, with fewer sampled minutes.
- **Heater removed**: the samples are kept (spec 011), so past hours keep their heater part; hours after removal have
  none.
- **Heater replaced by another device at the same location**: the samples belong to the location, so the history
  continues across the change.
- **Inactive location**: no new samples (spec 011); past hours keep theirs.
- **Clock changes**: hours and days follow the same Oslo-day rules as the rest of the figures (a 23- or 25-hour day).
- **Hour with readings but no price yet**: the heater part has kWh but no cost, the same as the hour itself.
- **Hour with heater samples but no meter readings** (meter down): no hour is shown today, so no heater part either;
  the heater's kWh for that hour is not counted anywhere.
- **Two samples in the same second**: harmless; counted once.
- **Comparison pricing** (what the hour would cost on the other pricing model): the heater part carries only the
  actual cost in this version.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST compute the water heater's energy for each hour from the stored power samples: each
  sample's power counts until the next sample, for at most 2 minutes; time not covered that way counts as not sampled.
- **FR-002**: Each hour in the hourly cost figures MUST carry, for a location with heater samples in that hour: the
  heater's kWh, the cost of those kWh, the number of sampled minutes (0 to 60), and whether the kWh was capped at the
  hour's kWh. An hour without samples MUST carry no heater part.
- **FR-003**: The heater's cost for an hour MUST be the hour's actual cost multiplied by heater kWh / hour kWh, so it
  uses the hour's own price per kWh, including the Norgespris cap; with no price for the hour, the cost MUST be absent.
- **FR-004**: The heater's kWh for an hour MUST NOT exceed the hour's metered kWh; when the samples add up to more,
  it MUST be capped and the hour marked as capped.
- **FR-005**: Each day in the daily cost figures MUST carry the sums of its hours' heater kWh, cost and sampled
  minutes, and whether any hour was capped; a day without samples MUST carry no heater part.
- **FR-006**: The monthly bill MUST carry the heater's kWh, cost and sampled minutes for the month's passed hours,
  and the first and last hour with samples; it MUST NOT be projected, MUST NOT change any other amount on the bill,
  and a month without samples MUST carry no heater part.
- **FR-007**: The heater figures MUST be visible to exactly those who may see the location's cost figures (owner,
  viewer, Admin), with the same answers for a location that does not exist or is not visible.
- **FR-008**: Existing fields of the hourly, daily and bill figures MUST NOT change; the heater part is added, so
  clients that ignore it keep working.
- **FR-009**: Asking for a day, a month or a year of figures MUST NOT become noticeably slower for a location with a
  heater than without one.

### Key Entities

- **Water heater part of an hour**: kWh, cost (or none without a price), sampled minutes (0–60), capped or not.
  Derived from the stored power samples and the hour's own figures; nothing new is stored.
- **Water heater part of a day**: the sums of its hours.
- **Water heater part of a month's bill**: kWh, cost, sampled minutes, first and last sampled hour; not projected.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For every hour and day, water heating plus everything else equals the total kWh and the total cost, to
  the øre and to 0,001 kWh.
- **SC-002**: For an hour of steady heating at a known power with all 60 minutes sampled, the heater's kWh is within
  2 % of power × 1 hour.
- **SC-003**: Over Hjemme's first full week with the heater, at least 95 % of the minutes are counted as sampled.
- **SC-004**: A year of daily figures for Hjemme is answered in no more than 1,5 times the time it takes today.
- **SC-005**: The heater's cost for a month equals the sum of its hourly costs, to the øre.

## Assumptions

- Samples are taken about once a minute (spec 011) and give the heater's power at that moment. A heater switches
  between heating at about 2–3 kW and standby at about 0,01 kW and stays in each state for many minutes, so holding a
  sample for up to 2 minutes is accurate enough; anything finer would need OSO data that does not exist.
- The heater is on the same meter as the rest of the house, so its kWh are part of the hour's metered kWh.
- The heater's cost uses the price per kWh only. The capacity charge (driven by the hour peaks) and the monthly fees
  are not shared out; a share of the capacity charge was considered and left out as an estimate on top of an estimate.
- Only the actual pricing model is split; the comparison with the other model is left for later.
- History starts on 2026-11-19, when sampling began. Nothing before that can be split.
- The frontend (its spec 010) shows the split in the usage chart (Now and History), the monthly bill, the Heater tab
  and the CSV export. This spec only provides the figures.
- Heating in the cheapest hours and pausing to stay below the capacity step are on hold (Roy, 2026-11-19).
