# Feature Specification: The Water Heater's Power over the Last Hours

**Feature Branch**: `feature/017-water-heater-power`

**Created**: 2026-11-20

**Status**: Draft

**Input**: User description: "This graph should have the water heater usage shown", about the frontend's "Last 3 hours"
card. That card draws the meter's power as the average kW per 5 minutes over the last three hours, against the same
three hours yesterday.

Spec 011 stores the heater's power about once a minute. Spec 012 turns those samples into kWh per hour, but nothing
gives them at a finer step. An hourly figure would show as flat steps in a 5-minute chart, so the frontend needs the
heater's power per 5 minutes. Chosen with Roy on 2026-11-20: start with the API, then specify the frontend.

## User Scenarios & Testing _(mandatory)_

### User Story 1 - The heater's power per 5 minutes for a time window (Priority: P1)

A household member's app asks for the heater's power over a window of a few hours, for example the last three hours,
or the same three hours yesterday. For each 5 minutes in that window it gets the heater's average power and how much
of the 5 minutes was covered by samples. The app can then draw water heating under the house's own power line, at the
same step.

**Why this priority**: This is the whole feature. The chart cannot show water heating without it.

**Independent Test**: For Hjemme, with the heater heating at about 2 kW for the last hour, ask for the last 3 hours.

- The 5-minute steps in that hour show about 2 kW, and the steps before it show the standby power.
- Each step says how many seconds were sampled, and full steps show 300.
- Ask for the same 3 hours yesterday: the steps follow yesterday's samples.

**Acceptance Scenarios**:

1. **Given** a location with heater samples, **When** a window is asked for, **Then** the answer has one entry per
   5 minutes that has any sampled time. Each entry gives its start, the average power over its sampled time, and the
   sampled seconds (1–300).
2. **Given** the 5-minute steps, **When** they are compared with the meter's 5-minute steps in the frontend, **Then**
   they start at the same times: on whole 5 minutes of the clock, the same in UTC and in Oslo time.
3. **Given** 5 minutes without any sampled time, **When** the window is read, **Then** there is no entry for them. They
   never appear as 0 kW.
4. **Given** the 5 minutes going on now, **When** the window reaches now, **Then** its entry covers only the time up
   to now, and its sampled seconds say so.
5. **Given** a window with no samples at all (no heater, before the heater was connected, or a long outage), **When**
   it is read, **Then** the answer is an empty list. It is not an error.
6. **Given** no window in the request, **When** it is read, **Then** it covers the last 3 hours up to now.
7. **Given** a window longer than 48 hours, ending before it starts, or starting in the future, **When** it is asked
   for, **Then** the request is refused with a message that says why.

---

### User Story 2 - Only people who may see the location (Priority: P1)

The heater's power is part of the location's use, so it is visible to exactly those who may see the location's use
and cost: the owner, viewers and Admins.

**Why this priority**: It is the same rule as every other figure for a location, and it must hold from the first
release.

**Independent Test**: As a viewer of Hjemme, ask for the window: it is answered. As a user without access, ask for
it: the answer is the same as for a location that does not exist.

**Acceptance Scenarios**:

1. **Given** an owner, a viewer or an Admin, **When** they ask, **Then** they get the window.
2. **Given** a user with no access to the location, or a location that does not exist, **When** they ask, **Then**
   the answer is "not found", the same in both cases.
3. **Given** no sign-in, **When** someone asks, **Then** the request is refused as unauthenticated.

---

### Edge Cases

- **Gaps in sampling**: the same rule as spec 012. Each sample's power holds until the next sample, for at most 2
  minutes, and time not covered that way is not sampled. A 5-minute step with partial coverage averages over its
  sampled seconds only, and says how many there were.
- **A hold that crosses a 5-minute boundary**: it is split between the two steps, as spec 012 splits holds between
  hours.
- **Heater removed or replaced**: the samples belong to the location and are kept (spec 011), so past windows still
  answer.
- **Inactive location**: no new samples; past windows still answer to those who may see it.
- **Clock changes**: 5-minute steps are on whole 5 minutes in UTC, which are whole 5 minutes in Oslo time too, so the
  night the clocks change has no odd step.
- **Samples above the meter's power**: not capped here. The 5-minute step has no meter figure to compare with in this
  endpoint. The hourly figures (spec 012) still cap the heater at the hour's metered kWh.

## Requirements _(mandatory)_

### Functional Requirements

- **FR-001**: The system MUST answer, for one location and a time window, the heater's average power per 5 minutes,
  computed from the stored power samples. Each sample's power holds until the next sample, for at most 2 minutes, and
  never past the end of the window or now (as in spec 012).
- **FR-002**: The 5-minute steps MUST start on whole 5 minutes of the UTC clock. Each entry MUST give its start (UTC),
  the average kW over its sampled time, and the sampled seconds (1–300).
- **FR-003**: Steps without sampled time MUST be left out, never answered as 0. A window without any MUST answer an
  empty list.
- **FR-004**: Without a window the answer MUST cover the last 3 hours up to now. The window MUST be at most 48 hours,
  MUST end after it starts, and MUST NOT start in the future; otherwise the request MUST be refused with a validation
  message.
- **FR-005**: The answer MUST be visible to exactly those who may see the location's use (owner, viewer, Admin), with
  the same "not found" for a location that does not exist and one that is not visible.
- **FR-006**: No existing endpoint or field MAY change.
- **FR-007**: A 3-hour window MUST be answered fast enough to refresh every minute from the app (see SC-003).

### Key Entities

- **Water heater power step**: start (UTC, on whole 5 minutes), average kW over its sampled time, and sampled seconds.
  It is derived from the stored power samples, and nothing new is stored.

## Success Criteria _(mandatory)_

### Measurable Outcomes

- **SC-001**: For 5 minutes of steady heating at a known power, all of them sampled, the step's power is within 2 % of
  the known power.
- **SC-002**: The kWh implied by the steps of an hour (Σ kW × sampled seconds) equals spec 012's uncapped heater kWh for
  that hour, within 0,001 kWh.
- **SC-003**: A 3-hour window is answered in under 300 ms at the 95th percentile on the production server.
- **SC-004**: Over a day of normal sampling, at least 95 % of the 5-minute steps have 240 or more sampled seconds.

## Assumptions

- The samples are taken about once a minute (spec 011). With a hold of up to 2 minutes, a 5-minute step usually has
  about 300 sampled seconds; a finer step would show mostly sampling noise.
- 5 minutes matches the frontend's "Last 3 hours" chart. Other step sizes are out of scope until a chart needs them.
- The average power is over the sampled time, not the whole 5 minutes, so a gap does not pull the power down. The
  sampled seconds tell the client how much to trust it.
- The frontend asks for the last 3 hours and for the same 3 hours yesterday, as it does for the meter, in two requests.
  The 48-hour limit leaves room for that and keeps a single answer small: 576 entries at most.
- The car charger is out of scope. Zaptec gives completed sessions only, so there is no live car power to show.
- A frontend spec follows, which draws water heating under the line of the "Last 3 hours" card.
