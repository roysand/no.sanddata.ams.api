# Feature Specification: Read the OSO Water Heater

**Feature Branch**: `feature/011-oso-water-heater`

**Created**: 2026-11-19

**Status**: Draft

**Input**: Backlog note `specs/_backlog/oso-water-heater.md` (written 2026-11-19; the read calls were tried by hand
the same evening with Roy's real key and heater, and the answers are in the note). User description: "Connect the OSO
water heater (OSO inCharge API) — read-only first version. The heater is at the Hjemme location. An Admin stores the
OSO subscription key (encrypted, never returned) and the device id (128-char hex) per location. The API reads the
heater's state from OSO (Status, CurrentTemperature, ActualLoad, TappingCapacityKwh) and exposes it to signed-in users
who may see that location [...]. A background job samples ActualLoad about every minute [...] and stores it so the
heater's kWh per hour/day can be shown later. No control calls. When OSO is down or the key is wrong, the endpoints
answer with a clear error and sampling skips without stopping the host. Never log the key or device id."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An Admin connects a water heater to a location (Priority: P1)

An Admin enters, for a location, the OSO subscription key and the heater's device id (both found in OSO's app or
website; OSO has no call that lists devices). The system checks them against OSO before saving, so a typo is caught
at once. The key is kept protected and is never shown again, not even to the Admin; the Admin can see that a heater
is connected and replace or remove the connection.

**Why this priority**: Nothing else works without it, and it is where the key, the only secret in this feature, is
handled.

**Independent Test**: As Admin, connect Roy's heater to Hjemme with the real key and device id, read the location's
heater settings back and confirm they say "connected" without the key; try again with a wrong key and confirm it is
refused and nothing is saved.

**Acceptance Scenarios**:

1. **Given** an Admin and a key and device id that OSO accepts, **When** they connect the heater to a location,
   **Then** the connection is saved and the answer says the heater is connected and reachable.
2. **Given** a key or device id that OSO rejects, **When** the Admin tries to connect, **Then** it is refused with a
   clear message ("OSO rejected the key or device id") and nothing is saved.
3. **Given** OSO cannot be reached when the Admin connects, **Then** it is refused with a message that OSO is
   unavailable and to try again, and nothing is saved.
4. **Given** a connected heater, **When** anyone reads the location's heater settings, **Then** the key is never in
   the answer; the device id is shown shortened (its first and last 6 characters) so the Admin can tell which heater
   it is.
5. **Given** a connected heater, **When** the Admin connects again with a new key or device id, **Then** the old
   connection is replaced after the same check.
6. **Given** a connected heater, **When** the Admin removes the connection, **Then** the key and device id are
   deleted, the heater's state can no longer be read and sampling stops; the power samples already stored are kept.
7. **Given** a regular user (owner or viewer of the location), **When** they try to connect, replace or remove a
   heater, **Then** it is refused with `403`.
8. **Given** an empty key, an empty device id, or a device id that is not hexadecimal, **Then** `400`.

---

### User Story 2 - A household member sees the water heater's state (Priority: P1)

A user who may see the location (an owner or a viewer of it, or an Admin) opens the water heater and sees how much hot
water there is and what the heater is doing: the temperatures in the tank, the litres of 40 °C water available now and
at most, the showers and baths left, whether it is heating right now and at what power, the target temperature, the
energy stored as hot water, whether the heater is online, any special mode it is in (holiday or supercharge, with its
start and end), and how old the reading is.

**Why this priority**: This is the value of the first version: the frontend can show the heater next to the meter.

**Independent Test**: As Roy (owner of Hjemme), request the heater state and compare it with the OSO app at the same
moment: the litres, showers, temperatures and heating on/off match, and the reading age is a few minutes at most.

**Acceptance Scenarios**:

1. **Given** a location with a connected heater, **When** an owner, a viewer or an Admin requests its state, **Then**
   the answer has: the top, middle and bottom tank temperatures (°C); litres of 40 °C water available now and the
   maximum; showers and baths available; whether the heater is heating; the current target temperature (°C); the
   current power (kW); the energy stored as usable hot water (kWh); whether it is online; the active special mode,
   if any (holiday or supercharge, each with start and end time); and the time of the reading.
2. **Given** the reading, **When** the answer is built, **Then** it says how old the reading is (now − reading time)
   so the app can show "updated 2 minutes ago", and all times are given in UTC.
3. **Given** a heater model without the fourth temperature sensor (OSO reports it as 0), **When** the state is
   returned, **Then** that temperature is left out rather than shown as 0 °C.
4. **Given** OSO's optimisation and control codes (`mode`, `optimizationOption`, `optimizationSubOption`), whose
   meaning OSO does not document, **When** the state is returned, **Then** they are passed on as plain numbers,
   labelled as OSO's codes, without interpretation.
5. **Given** a user who is not linked to the location, or a location that does not exist, **When** they request the
   heater state, **Then** the answer is `404` in both cases, as for the location's other data.
6. **Given** a location with no heater connected, **When** its heater state is requested, **Then** `404` with a
   message that no water heater is connected.
7. **Given** an unauthenticated caller, **Then** `401`.

---

### User Story 3 - The heater's power is sampled and stored (Priority: P2)

While a heater is connected, the system reads its current power from OSO about once a minute and stores each reading
with its time, as it stores the meter's readings. OSO gives only the power right now, with no history and no energy
counter, so these samples are the only way to later show how many kWh the heater used per hour and day and what share
of the location's consumption and cost it is.

**Why this priority**: The samples are needed for the next version (the heater's kWh and cost), and every day not
sampled is lost for good, so sampling should start as soon as the heater is connected. Showing the kWh is not part of
this version.

**Independent Test**: Connect a heater, wait 10 minutes and confirm there are about 10 stored samples for it with
increasing times; disconnect OSO (wrong key) and confirm samples stop, the error is logged, and the rest of the API
keeps working; restore the key and confirm sampling resumes.

**Acceptance Scenarios**:

1. **Given** a connected heater, **When** a minute passes, **Then** one power sample (kW, with the time it was read)
   is stored for that heater's location.
2. **Given** several locations with a connected heater, **When** sampling runs, **Then** each is sampled, and a
   failure at one does not stop the others.
3. **Given** OSO is down, slow (no answer within 10 seconds), or rejects the key, **When** a sample is due, **Then**
   no sample is stored for that minute (a gap, never a 0 kW sample), the failure is logged with a stable reason code,
   and sampling tries again at the next minute.
4. **Given** OSO keeps failing, **When** many minutes pass, **Then** the system keeps running, keeps serving every
   other endpoint, and does not log the same failure every minute without limit (one entry when failing starts, one
   when it recovers, and at most one per hour in between).
5. **Given** a heater that is removed or a location that is turned off, **When** the next minute passes, **Then** that
   heater is no longer sampled.
6. **Given** the heater is off, **When** it is sampled, **Then** the small standby power OSO reports (about 0.01 kW)
   is stored as it is, not rounded to 0.

---

### Edge Cases

- **OSO unavailable when reading state.** The state request answers with a clear "the water heater service is
  unavailable" error (not a generic server error), and the rest of the API is unaffected.
- **Key revoked or expired at OSO.** Reading the state answers with "OSO rejected the key or device id; an Admin must
  reconnect the heater"; sampling logs it and skips, as in user story 3.
- **Heater offline but OSO up.** OSO still answers with the last reading; the state says the heater is offline and the
  reading age shows how old it is. Sampling stores what OSO answers.
- **Stale reading.** A reading older than 15 minutes is returned as it is, with its age, and marked as stale.
- **OSO's field names and casing.** OSO spells some fields inconsistently (`holidyModeStartTime`, `timeStamp` vs
  `timestamp`); the system reads them as OSO sends them and answers with its own correctly spelt names.
- **Supercharge times without a time zone.** OSO gives the supercharge start and end with no time zone; they are
  treated as UTC (see Assumptions) and answered in UTC.
- **Two heaters at one location.** Not supported in this version: a location has at most one heater.
- **The same heater at two locations.** Refused: a device id can be connected to one location only.
- **Logging.** The key and the device id are never logged, not even in part, nor in request/response logs; logs refer
  to the location.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: An Admin MUST be able to connect, per location, one water heater by its OSO subscription key and device
  id, and MUST be able to replace and remove that connection.
- **FR-002**: The system MUST verify a key and device id with OSO before saving them, and MUST refuse and save nothing
  when OSO rejects them or cannot be reached.
- **FR-003**: The system MUST store the key protected (encrypted at rest) and MUST never return it in any answer.
- **FR-004**: The system MUST NOT return the full device id to anyone; it MAY return its first and last 6 characters.
- **FR-005**: A device id MUST be connected to at most one location.
- **FR-006**: Only Admins MUST be able to connect, replace or remove a heater; owners and viewers MUST get `403`.
- **FR-007**: An owner or viewer of the location, or an Admin, MUST be able to read the heater's current state as
  listed in user story 2, scenario 1, read from OSO at the time of the request.
- **FR-008**: The state MUST include the reading time, its age, and a stale flag when the reading is older than 15
  minutes; all times MUST be in UTC.
- **FR-009**: The state MUST leave out a temperature that OSO reports as 0 for a sensor the heater does not have, and
  MUST pass OSO's undocumented codes on as numbers without interpretation.
- **FR-010**: A user not linked to the location, or a location that does not exist, MUST get `404`; a location with no
  heater MUST get `404` with a "no water heater connected" message.
- **FR-011**: When OSO rejects the key, the state request MUST fail with a clear message that an Admin must reconnect
  the heater; when OSO is unavailable or does not answer within 10 seconds, it MUST fail with a clear "service
  unavailable" message. Neither may be answered as an unexpected server error.
- **FR-012**: While a heater is connected and its location is active, the system MUST sample its current power about
  once a minute and store each sample (kW and time read) for the location.
- **FR-013**: A failed sample MUST store nothing (a gap), MUST NOT stop sampling of other heaters or the rest of the
  system, and MUST be retried at the next minute.
- **FR-014**: Repeated sampling failures MUST be logged when failing starts, when it recovers, and at most once per
  hour in between, each with a stable reason code.
- **FR-015**: The system MUST NOT call any OSO call that changes the heater (profile, turn on/off, optimisation mode,
  holiday mode) in this version.
- **FR-016**: The key and the device id MUST NOT appear in any log, including request and response logs.

### Key Entities

- **Water heater connection**: belongs to one location (at most one per location); holds the OSO subscription key
  (protected), the device id (unique across locations), and when it was connected and last changed.
- **Heater power sample**: belongs to a location's heater; the time it was read (UTC) and the power in kW. Samples
  are kept when the connection is removed, so history is not lost if the heater is reconnected.
- **Heater state** (not stored): what OSO says now, as listed in user story 2, scenario 1.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An Admin can connect the heater at Hjemme in one step, and a wrong key or device id is caught before it
  is saved in 100% of attempts.
- **SC-002**: The litres of 40 °C water, showers, temperatures and heating on/off shown by the system match the OSO
  app at the same moment.
- **SC-003**: The heater state is answered within 3 seconds when OSO is working, and within 11 seconds with a clear
  error when it is not.
- **SC-004**: Over a day with OSO working, at least 95% of the minutes have a stored power sample (at least 1,368 of
  1,440).
- **SC-005**: An OSO outage of any length never stops the API or its other endpoints, and sampling resumes on its own
  within 2 minutes after OSO recovers.
- **SC-006**: A search of all logs for the key or the device id finds nothing.

## Assumptions

- The heater is at Hjemme (spot price). Roy has a working subscription key and device id; all five read calls were
  answered by OSO on 2026-11-19 (see the backlog note).
- Read only. Control (cheapest-hour profiles, pausing during capacity peaks) is a later feature; so is showing the
  heater's kWh and cost per hour and day from the stored samples.
- OSO's answers about once every 2 minutes are fresh (the reading was 2 minutes old when tried), so sampling once a
  minute is enough and faster gains nothing. OSO states no rate limits; one call per heater per minute plus the state
  requests is assumed acceptable.
- The supercharge start and end times OSO sends without a time zone are UTC, like OSO's other times. This could not be
  told from the one reading (both readings fit) and should be checked against the OSO app when a supercharge starts.
- Power samples are kept indefinitely, as meter readings are.
- Each state request reads OSO afresh; no caching is needed for one household.
- Who may see a location (owner, viewer, Admin) is as in spec 006; the heater state follows the same rule.
- The key is protected with the platform's standard means of encrypting data at rest; the choice is left to the plan.
