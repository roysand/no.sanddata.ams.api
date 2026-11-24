# Feature Specification: The Car Charger's State Right Now

**Feature Branch**: `feature/021-ev-charger-live`

**Created**: 2026-11-24

**Status**: Draft

**Input**: From the frontend session. Spec 016 reads Zaptec's **finished** charging sessions every 15 minutes, so a
charge in progress shows nowhere in the app until it ends ("A charge in progress is not shown until it ends", spec 016
assumptions). Roy wants to see a running charge.

Spec 016 assumed that needed Zaptec's live event feed (Service Bus), which is set up through Zaptec Support. Zaptec's
REST API also has `GET /api/chargers/{id}/state`, which gives a charger's latest reported values. Roy tested it with
his own login on 2026-11-24, during a charge on his Zaptec Go ("Lader Garasje Nr 11"), three reads a minute apart:

- It answered `200` for a homeowner login.
- Each value is a `{ StateId, Timestamp, ValueAsString }` entry, with the time it last changed (UTC, no `Z`).
- The values Zaptec documents for the Pro and Home also hold for the Go:

  | Id  | Meaning (Zaptec docs)                                                                       | Seen                          |
  | --- | ------------------------------------------------------------------------------------------- | ----------------------------- |
  | 710 | Charger operation mode: 1 no car, 2 connected, requesting to charge, 3 charging, 5 finished | `3` since 15:37 UTC           |
  | 513 | Total instant charge power, W                                                               | `4414.704`, unchanged 40 min  |
  | 553 | Energy delivered in the current session, kWh                                                | `12.962`, 2 min old           |
  | 721 | Current session id                                                                          | set at 14:52 UTC (plugged in) |
  | -2  | Online, 1/0                                                                                 | `1`                           |
  | -1  | The charger's check-in (heartbeat)                                                          | about every 3 minutes         |

- The figures agree: charging since 15:37 UTC at about 4,4 kW is about 12,9 kWh by 18:34 UTC.

Zaptec's usage guidelines say "Do not use the API for aggressive polling of charger or installation states", without
a number, and its limit is 10 requests per second per account. So the API reads the state **only when the app asks**,
at most once every 3 minutes per charger, matching the charger's own check-ins.

Chosen with Roy on 2026-11-24:

- The **Car tab** shows the charger's state with its details: no car, connected but not charging, charging, finished
  charging, offline, and when it was last updated.
- The **Now page** shows a line while the car is charging: "Car charging now: 4,4 kW · 13,0 kWh this charge · since
  16:37".
- An update **every 3 minutes** is enough.

## User Scenarios & Testing _(mandatory)_

### User Story 1 - See the charger's state right now (Priority: P1)

The app asks for a location's charger state and gets: whether the charger is online, its state (no car, connected and
waiting to charge, charging, finished), the power now, the kWh delivered in this charge so far, when the car was plugged
in, when charging started, and when the charger last reported.

**Why this priority**: It is everything the Car tab and the Now page show.

**Independent Test**: As Roy, while the car charges at Hjemme, ask for the state: it says charging, with about the
power and kWh the Zaptec app shows, within one charger check-in.

**Acceptance Scenarios**:

1. **Given** a car charging, **When** the state is read, **Then** it says charging, with the power now in kW, the
   charge's kWh so far, and when charging started.
2. **Given** a car connected but not charging (waiting or paused), **When** the state is read, **Then** it says
   connected and not charging, with the charge's kWh so far if it has any.
3. **Given** a charge finished with the car still connected, **When** the state is read, **Then** it says finished,
   with the charge's kWh.
4. **Given** no car connected, **When** the state is read, **Then** it says no car, with no power or kWh.
5. **Given** a charger that is offline, **When** the state is read, **Then** it says offline, with when it last
   reported, and no power or kWh are presented as current.
6. **Given** any answer, **When** it is read, **Then** it says when the values were read from Zaptec and when the
   charger last reported, so the app can say how old they are.

---

### User Story 2 - Kind to Zaptec (Priority: P1)

The API reads Zaptec only when someone asks, at most once every 3 minutes per charger, and shares that answer with
everyone who asks for the same charger in that time. With nobody looking at the app, Zaptec gets no extra calls.

**Why this priority**: Zaptec may rate-limit or revoke access for aggressive polling; losing access would also stop
spec 016's session reads.

**Acceptance Scenarios**:

1. **Given** several requests for the same charger within 3 minutes, from one or several users, **When** they are
   answered, **Then** Zaptec is called once.
2. **Given** nobody asks, **When** time passes, **Then** Zaptec's state is not read at all.
3. **Given** Zaptec answers with an error, rate-limits or times out, **When** the state is asked for, **Then** the
   answer says the state is unavailable, the error is not retried at once, and spec 016's session reads are not
   affected.

---

### User Story 3 - Only people who may see the location (Priority: P1)

**Acceptance Scenarios**:

1. **Given** an owner, viewer or Admin of the location, **When** they ask, **Then** they get its charger's state.
2. **Given** no access to the location, **When** someone asks, **Then** it is refused as the session list is.
3. **Given** a location without a charger, **When** someone with access asks, **Then** the answer says there is no
   charger (as the session list does), and Zaptec is not called.

---

### Edge Cases

- **A value Zaptec doesn't send**, or an operation mode the API doesn't know (such as 4): the state is "unknown" and
  the figures it has are still given.
- **Stale values**: Zaptec keeps a value until it changes. Steady power can be 40 minutes old while still true. The
  answer gives the charger's last check-in, so a charger that stopped reporting shows as offline or old, not as
  charging.
- **A new charge**: the charge's kWh start again from 0 with a new session id.
- **The finished session**: once a charge ends, spec 016 reads it within 15 minutes and stores its hours as today. The
  live figures are never stored and never added to any usage, cost, bill or Norgespris figure, so nothing is counted
  twice.
- **A Zaptec Pro or Home**: the same state ids are documented for them; only the Go is tested.
- **The Zaptec login not set up**: unavailable, as spec 016's reads.

## Requirements _(mandatory)_

### Functional Requirements

- **FR-001**: The system MUST answer a location's car charger state: online or not, one of no car / connected, not
  charging / charging / finished / unknown, the power now in kW, the charge's kWh so far, when the car was plugged in,
  when charging started, when the charger last reported, and when the values were read from Zaptec.
- **FR-002**: The system MUST read Zaptec's state only when asked, at most once every 3 minutes per charger, and share
  that answer between all who ask.
- **FR-003**: A failure to read the state MUST give an "unavailable" answer, MUST NOT be retried at once, and MUST NOT
  affect the session reads of spec 016.
- **FR-004**: The live figures MUST NOT be stored or added to any usage, cost, bill or Norgespris figure.
- **FR-005**: Access MUST follow the location's rules, as the charging session list.
- **FR-006**: Only the state values needed for FR-001 MAY be passed on; the charger's other values (device ids, SIM
  numbers, signed meter data, PIN) MUST NOT be.
- **FR-007**: The Zaptec user name, password and tokens MUST NOT appear in any log, as in spec 016.

### Key Entities

- **Charger state now**: per location with a charger: online, state, power kW, charge kWh, plugged in at, charging
  since, last reported at, read at. Derived from Zaptec; nothing is stored beyond the 3-minute shared answer.

## Success Criteria _(mandatory)_

### Measurable Outcomes

- **SC-001**: During a charge, the state's power and kWh match the Zaptec app within one charger check-in (about
  3 minutes).
- **SC-002**: With the app open on two devices for an hour during a charge, Zaptec's state is read at most 20 times
  for the charger.
- **SC-003**: With nobody using the app, Zaptec's state is never read.
- **SC-004**: The answer comes in under 1 second when Zaptec has to be read, and under 100 ms otherwise, at the 95th
  percentile.

## Assumptions

- The values and their meanings are as tested on 2026-11-24 on Roy's Zaptec Go (firmware 2.6.0.7) and as documented
  by Zaptec (https://docs.zaptec.com, state observation reference).
- "Plugged in at" is when the current session id was set (721); "charging since" is when the operation mode last
  changed to charging (710). The plan may use Zaptec's session details for the start instead, if it is more exact.
- Zaptec's live event feed (Service Bus) stays out of scope: it needs a request through Zaptec Support, and a read
  every 3 minutes while someone looks is enough.
- How the app shows the state is the frontend's spec 017, which follows this one.
