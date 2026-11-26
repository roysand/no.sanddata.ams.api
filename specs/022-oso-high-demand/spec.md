# Feature Specification: Turn the Water Heater's High Demand On and Off

**Feature Branch**: `feature/022-oso-high-demand`

**Created**: 2026-11-26

**Status**: Draft

**Input**: User description: Roy wants to turn the OSO water heater's "High Demand" on and off from the app, as he can
in the OSO app. High Demand heats the tank extra for a while, for example before guests arrive or a big bath. The
Heater tab already shows the mode as "Supercharge until …" (spec 011), so what's missing is the switch. OSO's public
API has no call for it, but OSO's own Homey app uses two undocumented calls, one to turn it on and one to turn it off.
Roy tested both by hand on 2026-11-26: both answered OK, and OSO's status changed each time. Turned on this way, High
Demand lasts 24 hours. Decisions from Roy on 2026-11-26: only an Admin may switch it; build it now and test it against
the real heater; if it doesn't work, the frontend hides the switch. Background and open questions: backlog note
`specs/_backlog/oso-high-demand.md`, taken up by this spec. The frontend switch is a separate spec in
`no.sanddata.ams.frontend`.

## User Scenarios & Testing _(mandatory)_

### User Story 1 - An Admin turns High Demand on (Priority: P1)

An Admin opens a location with a connected water heater and turns High Demand on. The heater starts heating extra,
and the answer says that High Demand is on and when it ends.

**Why this priority**: It's the reason for the feature. Turning it off (story 2) only matters once it can be turned
on from the app.

**Independent Test**: As an Admin, turn High Demand on for Hjemme. The answer and the heater's state both say High
Demand, with an end time about 24 hours ahead. The OSO status read by hand says the same.

**Acceptance Scenarios**:

1. **Given** a location with a connected heater in normal mode, **When** an Admin turns High Demand on, **Then** the
   answer gives the mode as High Demand with its start and end time, read back from the heater.
2. **Given** High Demand turned on, **When** anyone who may see the location reads the heater's state, **Then** it
   shows High Demand (spec 011's "Supercharge until …") with the same end time.
3. **Given** High Demand already on, **When** an Admin turns it on again, **Then** it stays on, and the answer gives
   the end time the heater now reports.

---

### User Story 2 - An Admin turns High Demand off (Priority: P1)

An Admin turns High Demand off before it ends by itself, for example when the guests cancel. The heater goes back to
its normal mode.

**Why this priority**: Without it, a High Demand turned on by mistake runs for 24 hours at whatever the power costs.
It is as important as turning it on.

**Independent Test**: With High Demand on, turn it off as an Admin. The answer and the heater's state both say normal
mode. Within the next hour the heater stops heating towards the High Demand temperature (FR-009).

**Acceptance Scenarios**:

1. **Given** High Demand on, **When** an Admin turns it off, **Then** the answer gives the mode as normal, read back
   from the heater.
2. **Given** High Demand already off, **When** an Admin turns it off, **Then** nothing changes and the answer gives the
   normal mode. That is not an error.

---

### User Story 3 - Only an Admin may switch it (Priority: P1)

Owners and viewers see the heater's mode, as today, but can't change it. Only an Admin can.

**Why this priority**: High Demand costs money and affects everyone at the location. Roy decided on 2026-11-26 that
only an Admin may switch it.

**Independent Test**: Try both switches as an owner, as a viewer, and without signing in. All are refused, and the
heater's mode doesn't change.

**Acceptance Scenarios**:

1. **Given** a signed-in user who isn't an Admin, even the location's owner, **When** they try to turn High Demand on
   or off, **Then** they are refused as not allowed, and OSO is not called.
2. **Given** no sign-in, **When** someone tries either switch, **Then** they are refused as unauthenticated.

---

### Edge Cases

- **No heater connected**: the switch answers that the location has no heater, the same as the other heater calls.
  OSO is not called.
- **The location doesn't exist**: the answer is not found.
- **OSO rejects the key or the device** (for example a replaced key): the same error as the heater's other calls
  (spec 011). Nothing is retried.
- **OSO no longer offers the call**: the calls are undocumented, so OSO may remove them. OSO's "not found" then gets
  its own error, saying High Demand isn't available from OSO, so it isn't mistaken for a wrong device id.
- **OSO is slow or down**: the switch fails with the existing "unavailable" error, within a few seconds. Whether the
  heater changed is then unknown; the state endpoint will tell.
- **The switch works but the read-back fails**: the answer says the switch was done, with the mode unknown. It's not
  reported as a failure, since the change was made.
- **OSO reports the old mode for a moment after the switch**: the answer gives what OSO reports. The state endpoint
  reads OSO afresh each time, so the next read shows the change.
- **The OSO app shows something else**: on 2026-11-26 the OSO app still showed High Demand after the API turned it
  off, while OSO's status said normal mode. Our Heater tab shows the mode as OSO's status reports it. Whether the
  heater itself follows that status is checked in testing (FR-009).
- **Holiday mode is on**: OSO decides what High Demand does then. The answer gives the mode OSO reports afterwards.
- **Inactive location**: an Admin may still switch its heater, as an Admin may still read it.
- **High Demand ends by itself**: after 24 hours, or at the end time set in the OSO app. Nothing in our app does
  that; the state just shows normal mode again.

## Requirements _(mandatory)_

### Functional Requirements

- **FR-001**: An Admin MUST be able to turn High Demand on for a location with a connected water heater.
- **FR-002**: An Admin MUST be able to turn High Demand off for such a location.
- **FR-003**: Only an Admin MAY switch High Demand. Any other signed-in user MUST be refused as not allowed, and a
  caller who isn't signed in as unauthenticated. OSO MUST NOT be called for a refused request.
- **FR-004**: After a successful switch, the answer MUST give the heater's mode and the mode's start and end time, as
  read back from OSO at once. If that read fails, the answer MUST still say that the switch was done, with the mode
  unknown.
- **FR-005**: A failed switch MUST give the same errors as the heater's existing calls (no heater, OSO rejected,
  OSO unavailable), except that OSO answering "not found" to the switch MUST give its own error saying High Demand
  isn't available from OSO.
- **FR-006**: A switch MUST NOT be retried automatically, so one request never turns the mode on or off twice.
- **FR-007**: Every switch that reaches a connected heater MUST be logged with who made it, the location, on or off,
  and the outcome. Requests refused before that (not an Admin, no location, no heater) are logged by the request
  logging, as for other calls. The OSO key and
  the device id MUST NOT appear in any log (spec 011, FR-016).
- **FR-008**: Spec 011's read-only rule (FR-015) is lifted for High Demand only. Every other OSO call that changes the
  heater (profile, turn on or off, optimisation mode, holiday mode) MUST still never be called.
- **FR-009**: Before the feature is used day to day, a test against the real heater MUST show that the heater follows
  the API's off call: after turning High Demand off while the tank is below the High Demand temperature, the heater's
  set point returns to normal and the heater stops heating towards it. If it doesn't, the frontend hides the switch,
  and the result is written down in this spec.
- **FR-010**: Nothing about the switch is stored by our app. The mode lives at OSO, and the heater's state keeps
  reading it from there.

### Key Entities

- **High Demand switch**: a request by an Admin to turn the mode on or off for one location's heater. It has the
  location, on or off, who asked and when, and the outcome. It is logged, not stored.
- **Heater mode** (existing, spec 011): normal, Holiday, High Demand ("Supercharge") or Blue Battery, with a start and
  end time for Holiday and High Demand. Read from OSO.

## Success Criteria _(mandatory)_

### Measurable Outcomes

- **SC-001**: After an Admin turns High Demand on, the heater's state shows High Demand within 1 minute, in every one of
  3 tries.
- **SC-002**: After an Admin turns High Demand off, the heater's state shows normal mode within 1 minute, in every one
  of 3 tries, and the hand test of FR-009 passes.
- **SC-003**: A switch answers within 5 seconds, including the read-back, unless OSO is down.
- **SC-004**: No user who isn't an Admin can change the mode: every such attempt is refused and leaves the mode as it
  was.
- **SC-005**: The logs of a switch never contain the OSO key or the device id.

## Assumptions

- **Only the Hjemme heater exists today.** It lists High Demand among its features ("SuperChargeMode"). A heater
  without that feature isn't expected, and OSO's own answer is then passed on as an error.
- **The duration is OSO's.** Turned on this way, High Demand lasts 24 hours. Choosing a shorter time, by turning it off
  ourselves at the chosen moment, is a later step if Roy wants it.
- **The calls are undocumented** (seen in OSO's Homey app). OSO can change them without notice. FR-005's own error
  makes such a change easy to recognise.
- **Turning it off in the UI is the frontend's job.** If the real-heater test fails, the frontend hides the switch.
  The API's calls can then stay unused or be removed in a small follow-up.
- **The same OSO key as for reading is used.** Roy's key was allowed both calls on 2026-11-26.
- **No price or cost is shown** next to the switch in this version.
- **The frontend switch** on the Heater tab is a separate spec in `no.sanddata.ams.frontend`, with a handover note in
  `specs/_backlog/` once this is deployed.
