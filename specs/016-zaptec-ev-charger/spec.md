# Feature Specification: Read the Zaptec EV Charger and Optionally Count It with the House

**Feature Branch**: `feature/016-zaptec-ev-charger`

**Created**: 2026-11-20

**Status**: Draft

**Input**: User description: "New feature, read charging from my Zaptec car charger. This charger is not coupled to my
house. I want to be able to have checkbox to show this as a part of the house electricity cost and usage. Zaptec has an
api." Expanded: read charging sessions from the Zaptec cloud; the charger has its own supply, so its energy is not in
the house's meter readings today; store the charging energy per location; a per-location setting (a checkbox in the
frontend, exposed by the API) decides whether the charger's kWh are counted in the house's usage
figures (hourly, daily, current hour and the monthly bill). The frontend is a separate project; this spec covers the
API side. Follows the pattern of specs 011 (OSO water heater) and 012 (heater kWh and cost).

Clarified with Roy 2026-11-20: **kWh only, no price.** The charger's energy is not priced in this version. The
checkbox adds the charger's kWh to the house's usage figures; every cost and the bill's amounts stay the house's own.

Clarified with Roy 2026-11-20, after reading Zaptec's API docs (https://docs.zaptec.com): **sign in with Roy's own
Zaptec login, kept in the server's `.env`.** The API never takes the Zaptec password through an endpoint and never
stores it in the database. Zaptec's Fair Use Policy forbids integrators storing users' passwords and points them to a
consent flow (a UserGroup that Zaptec Support sets up, which the owner grants access to an installation). That is the
intended later step: switch the sign-in to that route and keep the installation id and charger id as they are, without
changing anything else in this feature. **Completed sessions only:** Zaptec's session history returns finished
sessions; a charge in progress shows up when it ends.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An Admin connects a Zaptec charger to a location (Priority: P1)

The Zaptec sign-in is set up once on the server, out of the API's reach. An Admin asks the API for the chargers that
sign-in can see (name, id, installation), picks one and connects it to a location, so nobody has to look up an id.
The Admin can see which charger is connected to a location and replace or remove the connection.

**Why this priority**: Nothing else works without it.

**Independent Test**: With Roy's Zaptec login in the server's `.env`, as Admin list the chargers, connect Roy's
charger to Hjemme, and read the location's charger settings back: they say "connected" with the charger's name and
installation. Remove the login from `.env` and restart: listing chargers answers that the Zaptec sign-in is not set up.

**Acceptance Scenarios**:

1. **Given** a Zaptec sign-in on the server that Zaptec accepts, **When** an Admin asks for the chargers, **Then**
   they get each charger's name and id and the installation it belongs to (name and id), and nothing is saved.
2. **Given** a charger from that list, **When** the Admin connects it to a location, **Then** the system checks with
   Zaptec that the sign-in can still see that charger, saves the connection (charger id, name, installation id and
   name), and answers that it is connected.
3. **Given** a charger id the sign-in cannot see, **When** the Admin connects it, **Then** `400` with a message that
   Zaptec does not know that charger for this sign-in, and nothing is saved.
4. **Given** no Zaptec sign-in on the server, **When** an Admin lists or connects chargers, **Then** a clear message
   that the Zaptec sign-in is not set up on the server, and nothing is saved.
5. **Given** a sign-in Zaptec rejects (for example the password was changed in the Zaptec app), **Then** a clear
   message that Zaptec rejected the server's sign-in, and nothing is saved.
6. **Given** Zaptec cannot be reached, **Then** a message that Zaptec is unavailable and to try again, and nothing is
   saved.
7. **Given** a connected charger, **When** the Admin connects another charger to the location, **Then** the old
   connection is replaced after the same check; charging data already stored is kept.
8. **Given** a connected charger, **When** the Admin removes the connection, **Then** reading from Zaptec stops for that
   location; the charging data already stored is kept, and the "count with the house" setting is turned off.
9. **Given** a regular user (owner or viewer of the location), **When** they try to list, connect, replace or remove
   a charger, **Then** `403`.
10. **Given** no charger id in the request, **Then** `400`.

---

### User Story 2 - The charger's energy is read and stored per hour (Priority: P1)

While a charger is connected, the system regularly reads the charger's completed charging sessions from Zaptec and
stores the energy per hour for the location, plus a list of the sessions (start, end, kWh). Zaptec keeps
history, so a period missed while Zaptec or the API was down is filled in on the next successful read, and when a
charger is first connected its earlier history is read back as well.

**Why this priority**: Without stored hourly kWh there is nothing to count with the house or to price; it is useful on
its own too ("the car used 212 kWh this month").

**Independent Test**: Connect the charger, wait for the first read, and compare the stored sessions and their kWh with
the Zaptec app's charge history for the last month: same sessions, same kWh (± 0,01 kWh each). Then charge the car for
a while, stop, and confirm the hours of that session appear within 15 minutes of it ending.

**Acceptance Scenarios**:

1. **Given** a newly connected charger, **When** the first read runs, **Then** the charging history back to the start
   of the previous calendar month (Europe/Oslo) is read and stored, so the current and the last bill can include it.
2. **Given** a connected charger, **When** a read runs, **Then** every session that ended since the last read is
   stored with its start, end and kWh, and its energy is stored per clock hour (UTC hours, shown in Oslo time like the
   meter's figures).
3. **Given** a session that spans several hours, **When** it is stored, **Then** its energy is split over the clock
   hours from Zaptec's energy readings within the session (the kWh delivered in each hour); only if Zaptec gives no
   such readings for a session is its kWh spread evenly over its minutes, and such hours are marked as estimated.
4. **Given** a session read again (for example because Zaptec corrected it or a read overlapped the last one),
   **When** it is stored, **Then** it replaces the earlier version of the same session and its hours, so nothing is
   counted twice.
5. **Given** a charge in progress, **When** the figures are read, **Then** it is not yet included; it appears once the
   session has ended and been read (see Assumptions).
6. **Given** Zaptec is down, slow (no answer within 15 seconds) or rejects the sign-in, **When** a read is due,
   **Then** nothing is stored or deleted, the failure is logged with a stable reason code, the next read tries again,
   and the gap is filled once Zaptec answers.
7. **Given** Zaptec keeps failing, **Then** the rest of the API keeps working, and the failure is logged when it starts,
   when it recovers, and at most once per hour in between.
8. **Given** a user who may see the location, **When** they ask for the charging sessions of a period, **Then** they
   get each session's start, end and kWh, newest first.

---

### User Story 3 - A checkbox decides whether the charger counts with the house (Priority: P1)

Each location with a charger has a setting "count the charger with the house". When it is on, the location's hourly,
daily and current-hour usage (kWh), and the monthly bill's usage, include the charger's kWh, and every hour and day
also says how much of it was car charging, so the app can show "car" and "house" side by side. When it is off, all
those figures are exactly what they are today, and the charger's kWh are still available separately. Costs are never
changed by the setting: the charger's kWh are not priced in this version.

**Why this priority**: This is what Roy asked for: to see the whole household's electricity, car included, with one
click, or the house alone.

**Independent Test**: For Hjemme, read the hourly figures of a day with a charging session with the setting off, turn
it on, and read them again: the kWh of the session's hours rose by the charger's kWh, each hour says how much was
charging, every cost is unchanged, and hours without charging are unchanged. Turn it off again: the figures are back
to the first reading.

**Acceptance Scenarios**:

1. **Given** a location with a connected charger, **When** an owner of the location or an Admin turns the setting on or
   off, **Then** it is saved and the location's settings show its current value; a viewer gets `403`.
2. **Given** a location with no charger connected (now or ever), **When** someone tries to turn the setting on,
   **Then** `400` with a message that no charger is connected.
3. **Given** the setting is on, **When** hourly figures are read, **Then** each hour's total kWh is the meter's kWh
   plus the charger's kWh, and the hour carries the charger's kWh separately so "car" and "house" can be shown side by
   side. The two parts always add up to the hour's total kWh. The meter's kWh stays available as it is today, because
   the hour's cost is computed from it; the cost is the house's cost, unchanged.
4. **Given** the setting is on, **When** daily figures are read, **Then** the day's charger kWh is the sum of its
   hours, never a separate calculation, and the day's total kWh includes it; the day's cost is unchanged.
5. **Given** the setting is on, **When** the monthly bill is read, **Then** it carries the month's charger kWh as its
   own "car charging" usage, and the bill's total kWh includes it; every amount on the bill (lines, subtotals, total,
   capacity step) is unchanged, and the bill says the charger's kWh are included but not priced.
6. **Given** the setting is off, **When** any of these figures are read, **Then** they are exactly what they would be
   without this feature (no charger kWh in totals), but each hour, day and the bill still carry the charger's kWh
   separately, marked as not included, so the app can show it next to the house without adding it.
7. **Given** either setting, **When** the house's costs are worked out (hourly cost, Norgespris monthly cap, capacity
   step, bill), **Then** they use the meter's kWh only, never the charger's, so turning the setting on can never change
   a cost.
8. **Given** an hour with charging but no meter reading for the house (a gap in the house's data), **Then** the
   hourly list is unchanged (it lists hours with a meter reading, as today), but the day and the month still count
   that hour's charger kWh and say how many such hours there were, so no charging is lost and no cost changes.
9. **Given** an hour before the charger was connected and before the history read back, or a location that never had
   a charger, **Then** the hour has no charger part at all (not zero), so "unknown" is never shown as "no charging".
10. **Given** the setting is changed, **When** figures for past hours and months are read afterwards, **Then** they
    follow the current setting (the setting is a view choice, not a history of when it was on).
11. **Given** the water heater split of spec 012, **When** the setting is on, **Then** the heater part is unchanged
    (the heater is behind the house's meter, the charger is not), and "everything else" is still the house's kWh minus
    the heater's, never including the charger.

---

### Edge Cases

- **Zaptec unavailable when listing chargers or connecting.** A clear "the charger service is unavailable" error, never
  a generic server error; the rest of the API is unaffected.
- **Password changed in the Zaptec app.** Reads fail with a stable reason code ("Zaptec rejected the sign-in"); the
  stored data is kept, and the gap is filled once the server's `.env` is updated and the API restarted.
- **Sign-in not set up on the server.** The API starts and runs normally; charger reads are skipped (logged once at
  start), and the charger endpoints answer that the Zaptec sign-in is not set up.
- **Account with several chargers or installations.** The Admin picks one charger per location; the others are ignored.
- **Zaptec's rate limits.** Zaptec allows 10 requests per second per account and 1 sign-in per second per address, and
  asks clients not to poll aggressively; the system reuses its Zaptec sign-in until it expires instead of signing in
  for every call, and backs off when Zaptec says it is busy.
- **Two chargers at one location.** Not supported in this version: a location has at most one charger.
- **The same charger at two locations.** Refused: a charger can be connected to one location only.
- **Daylight saving changes.** Hours are stored in UTC; days and months follow Oslo time as the meter's figures do, so
  the 23- and 25-hour days are handled the same way.
- **A session Zaptec later corrects or removes.** The next read that covers the session replaces it; a session no
  longer in Zaptec's history for a period that is re-read is removed with its hours.
- **Very long session (car left plugged in for days).** Only the hours Zaptec reports are stored; Zaptec leaves out
  hours with no charging, so an hour within the session without a reading has no charger kWh.
- **Charger connected to a location with Norgespris.** The charger's kWh never count toward the Norgespris monthly
  cap, since only the meter's kWh are used for costs.
- **Logging.** The Zaptec user name and password, and the access tokens Zaptec hands out, are never logged, not even in
  part, nor in request and response logs; logs refer to the location.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: An Admin MUST be able to list the chargers the server's Zaptec sign-in can see, and to connect, replace
  and remove one charger per location by its charger id.
- **FR-002**: The system MUST check with Zaptec that the sign-in can see the charger before saving a connection, and
  MUST refuse and save nothing when Zaptec rejects the sign-in, does not know the charger, or cannot be reached.
- **FR-003**: The Zaptec user name and password MUST come from the server's configuration (`.env`) only; no endpoint
  MUST accept, store or return them, and they MUST NOT be stored in the database.
- **FR-004**: A charger MUST be connected to at most one location, and a location MUST have at most one charger.
- **FR-005**: Only Admins MUST be able to list, connect, replace or remove chargers; owners and viewers MUST get `403`.
- **FR-006**: While a charger is connected and its location is active, the system MUST read its charging sessions
  from Zaptec at least every 15 minutes and store each completed session (start, end, kWh) and its energy per clock
  hour for the location.
- **FR-007**: On first connection, the system MUST read the history back to the start of the previous Oslo calendar
  month; after a failure or downtime, it MUST fill the gap from the last successful read.
- **FR-008**: Storing a session again MUST replace its earlier version and hours, so no kWh is counted twice.
- **FR-009**: A failed read MUST store and delete nothing, MUST NOT stop the rest of the system, and MUST be logged
  when failing starts, when it recovers, and at most once per hour in between, with stable reason codes.
- **FR-010**: Each location MUST have a setting "count the charger with the house", off by default; an owner of the
  location or an Admin MUST be able to change it; viewers MUST get `403`; turning it on without a charger ever
  connected MUST be refused with `400`. Removing the charger MUST turn it off.
- **FR-011**: With the setting on, the location's hourly, daily, current-hour and monthly-bill kWh totals MUST include
  the charger's kWh; with it off, those totals MUST be exactly as without this feature.
- **FR-012**: Every hour, day and bill of a location with charger data MUST carry the charger's kWh and whether they
  are included in the totals separately, whatever the setting; days and months MUST be sums of their hours.
- **FR-013**: Hours before any charger data exists MUST carry no charger part (not zero).
- **FR-014**: The charger's kWh MUST NOT be priced in this version, and no cost, Norgespris cap, capacity step, fee
  or bill amount MUST change because of the charger or the setting; costs use the meter's kWh only.
- **FR-015**: A user who may see the location (owner, viewer, Admin) MUST be able to list its charging sessions for a
  period; others MUST get `404`, as for the location's other data.
- **FR-016**: The water heater split (spec 012) MUST be unchanged by the charger setting.
- **FR-017**: The system MUST NOT call any Zaptec call that changes the charger (start, stop, current limits,
  settings) in this version.
- **FR-018**: The Zaptec user name, password and access tokens MUST NOT appear in any log.

### Key Entities

- **Charger connection**: belongs to one location (at most one per location); holds the charger's id and name and its
  installation's id and name (no secrets), when it was connected and last changed, and up to when charging data was
  last read successfully.
- **Charging session**: belongs to a location's charger; Zaptec's session id, start and end time (UTC) and kWh. Kept
  when the connection is removed.
- **Charger hourly energy**: belongs to a location; the clock hour (UTC), the kWh charged in it, and whether it is
  estimated (spread evenly) rather than reported per hour by Zaptec. Derived from the sessions; kept when the
  connection is removed.
- **Location setting "count the charger with the house"**: on or off per location, off by default.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An Admin can connect the charger at Hjemme without looking up any id, and a charger the sign-in cannot
  see is refused before it is saved in 100% of attempts.
- **SC-002**: The sessions and the monthly kWh the system shows match the Zaptec app's charge history (± 0,01 kWh per
  session, ± 0,1 kWh per month).
- **SC-003**: A finished charging session shows up in the figures within 15 minutes of ending when Zaptec is working.
- **SC-004**: Turning the setting on changes the location's kWh totals by exactly the charger's kWh and changes no cost
  by a single øre; turning it off restores today's figures exactly.
- **SC-005**: A Zaptec outage of any length never stops the API or its other endpoints, and the missed charging data is
  filled in within 15 minutes after Zaptec recovers.
- **SC-006**: A search of all logs for the Zaptec user name, password or tokens finds nothing.

## Assumptions

- "Zaptech" is Zaptec, the Norwegian charger maker. From its docs (read 2026-11-20): the API signs in with a Zaptec
  portal user name and password and gives a sign-in valid for about an hour; it lists installations and chargers; and
  it returns completed sessions per installation or charger, each with its kWh and a list of energy readings within
  the session. The older charge-history call is deprecated and ends on 2027-01-01, so its replacement (archived
  sessions) is used. The docs do not say how often the energy readings within a session come (per hour or per 15
  minutes); this, and the exact calls, are to be tried by hand with Roy's login before the plan, as was done for OSO.
- Using Roy's own login for his own charger is a grey zone in Zaptec's Fair Use Policy, which is written for
  integrators; it is accepted as the first step. The later step is Zaptec's consent route (a UserGroup from Zaptec
  Support, which the installation owner grants access to); then the sign-in changes and the stored installation and
  charger ids stay. Roy mentioned having seen a way to sign in with the installation and charger ids; this was not
  found in the docs read and is to be checked when that step is taken.
- A charge in progress is not shown until it ends: Zaptec's session history holds completed sessions only. Showing a
  running charge would need Zaptec's live event feed (Service Bus) and is a later feature.
- The charger is at Hjemme (to be confirmed when connecting); it has its own supply, so none of its energy is in the
  house's meter readings, and adding it never counts anything twice.
- kWh only. Pricing the charger's kWh (and whether they would raise the capacity step) is left for a later feature;
  the stored hourly kWh are what such a feature would need.
- Read only. Controlling the charger (charging in the cheapest hours, pausing at a capacity peak) is a later feature.
- Reading every 15 minutes is enough: charging history is what matters, not live power. Zaptec's stated rate limits
  are respected; one household's charger is a handful of calls per hour.
- The setting is a view choice for the whole location (all its users see the same), not per user. It affects the
  API's figures only; nothing is changed in the stored meter readings.
- The CSV export, if it shows the hourly figures, follows the same rule as the hourly figures (charger columns always
  present when there is charger data; totals follow the setting).
- Who may see a location (owner, viewer, Admin) is as in spec 006; owners may change the location's own settings as in
  spec 006.
- The Zaptec login lives in the server's `.env` on the Hetzner VPS like the other secrets, never in the image, git or
  the database (unlike the OSO key of spec 011, which an Admin enters through the API). Changing it means editing
  `.env` and restarting the API.
- Charging data is kept indefinitely, as meter readings are.
