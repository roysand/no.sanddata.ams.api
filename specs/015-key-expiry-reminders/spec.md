# Feature Specification: Reminders Before Keys Expire

**Feature Branch**: `feature/015-key-expiry-reminders`

**Created**: 2026-11-20

**Status**: Draft

**Input**: Agreed with Roy on 2026-11-20, while preparing to add his cabin as a second location. User description:
"The api key to the heater api is valid for one year. It would be nice to get reminded about the key will soon expire.
The same with the location and sensor key rotation."

There are two kinds of key, and each stops something when it expires:
- **The OSO subscription key** that connects a water heater (spec 011). It is valid for one year. OSO does not report
  when it expires, so the system does not know today. When it expires, the heater can no longer be read or sampled.
  Hjemme's key was connected on 2026-11-19 and is valid until 2026-11-19 + 1 year: 19 November 2027.
- **A location's sensor key**, which the reader's forwarder uses to send readings (spec 004). It is valid for two years
  from when it was issued. When it expires, every reading from that location is refused and lost. Its expiry is already
  stored and shown to administrators in the location list, and an administrator can already replace it
  (`POST /api/admin/locations/{id}/api-key/rotate`).

Choices made with Roy:
- An administrator enters the OSO key's expiry date when connecting or replacing the heater. The form offers one year
  from today.
- Reminders start **30 days** before a key expires.
- They are shown in the app, as a banner for administrators that leads to the location. There are no e-mails or push
  notifications, which the app cannot send yet.
- The frontend also gets a button to replace a sensor key, using the existing endpoint. That work belongs to the
  frontend spec.

## User Scenarios & Testing _(mandatory)_

### User Story 1 - The system knows when the heater's OSO key expires (Priority: P1)

An administrator connects or replaces a location's water heater and enters, next to the key and device id, the date
until which the OSO key is valid. Later, the heater's settings show that date, and the administrator can correct it
without entering the key again. A heater connected before this feature gets a date one year after it was connected,
which for Hjemme is 19 November 2027, as Roy confirmed.

**Why this priority**: Without the date nothing can remind anyone. It is the only fact the system lacks today.

**Independent Test**: Connect a heater with the expiry date 2027-11-19 and read its settings: the date is there. Change
it to 2027-12-01 without a key: the settings show the new date and the key still works. Read Hjemme's settings after
the migration: 2027-11-19.

**Acceptance Scenarios**:

1. **Given** the connect form, **When** an administrator sends a key, a device id and an expiry date, **Then** the date
   is stored with the heater and returned in its settings.
2. **Given** a connect request without an expiry date, from a client older than this feature, **When** it is saved,
   **Then** the date is set to one year from today, so every heater has one.
3. **Given** an expiry date in the past or today, or more than five years ahead, **When** it is sent, **Then** it is
   refused with a clear message and nothing changes.
4. **Given** a connected heater, **When** an administrator changes only its expiry date, **Then** the date changes, and
   the key, the device id and the connection date stay as they are.
5. **Given** a heater connected before this feature, **When** the change is deployed, **Then** its expiry date becomes
   one year after the day it was connected.
6. **Given** the heater is replaced with a new key, **When** it is saved, **Then** the expiry date sent with the new
   key replaces the old one.

---

### User Story 2 - One list of keys that expire soon, for the reminder banner (Priority: P1)

The app asks, once when an administrator opens it, which keys expire within 30 days or have already expired, across
all locations. For each key the answer says which location, which kind of key, when it expires and how many days are
left, so the app can show "The sensor key for Hytta expires in 12 days" and link to the location.

**Why this priority**: This is the reminder itself. Without one list, the app would have to read every location's heater
settings separately.

**Independent Test**: Set a location's sensor key to expire in 10 days and a heater key to have expired 3 days ago.
Read the list: both are in it, with the right kind, location, date and days left (10 and −3). A key that expires in 40
days is not in it.

**Acceptance Scenarios**:

1. **Given** keys expiring within 30 days, **When** an administrator reads the list, **Then** each one is listed with
   the location's id and name, the kind of key (sensor or water heater), its expiry and the whole days left.
2. **Given** a key that has already expired, **When** the list is read, **Then** it is listed as expired, with the days
   since as negative days left, until it is replaced.
3. **Given** a key that expires in more than 30 days, **When** the list is read, **Then** it is not listed.
4. **Given** a deactivated sensor key (switched off by an administrator on purpose), **When** the list is read,
   **Then** it is not listed: it isn't expiring, it is off.
5. **Given** an inactive location, **When** the list is read, **Then** its keys are not listed, because it sends no
   readings and its heater is not sampled.
6. **Given** a user who is not an administrator, **When** they ask for the list, **Then** they are refused, as for the
   other administrator pages.
7. **Given** no keys expire within 30 days, **When** the list is read, **Then** it is empty, and the app shows no
   banner.

---

### Edge Cases

- **Replacing a sensor key** with the existing endpoint gives the new key two more years. It leaves the list at once,
  because the new expiry is far away.
- **Replacing an expired OSO key** with a new key and date removes it from the list in the same way.
- **Clock and calendar:** an OSO date is a calendar day in Oslo. The key counts as valid through that whole day and
  expired from the next midnight in Oslo. Days left are whole Oslo days.
- **A heater removed** (spec 011) takes its key, and its date, with it. It is no longer listed.
- **Several administrators** see the same list. Nothing is marked as "seen"; the reminder stays until the key is
  replaced.
- **The sensor key's lifetime** stays two years. This feature does not change it.

## Requirements _(mandatory)_

### Functional Requirements

- **FR-001**: The system MUST store an expiry date for each connected water heater's OSO key, as a calendar day.
- **FR-002**: Connecting or replacing a heater MUST accept an optional expiry date. Without one, the date MUST be one
  year from today.
- **FR-003**: An expiry date MUST be after today and at most five years ahead. Otherwise it is refused with a field
  message, and nothing changes.
- **FR-004**: An administrator MUST be able to change a connected heater's expiry date without sending the key or the
  device id again.
- **FR-005**: The heater's settings, as administrators read them, MUST include its key's expiry date.
- **FR-006**: Heaters connected before this feature MUST get the expiry date one year after the day they were connected.
- **FR-007**: The system MUST offer administrators one list of the keys that expire within 30 days or have expired.
  - It covers the sensor keys and the OSO keys of active locations.
  - Each entry has the location's id and name, the kind of key, the expiry, the whole days left (negative when
    expired), and whether it has expired.
  - Deactivated sensor keys are left out.
- **FR-008**: The list and the date change MUST be for administrators only, with the same refusal as the other
  administrator endpoints.
- **FR-009**: No key, key hash or device id MUST appear in the list, in the settings or in logs.
- **FR-010**: Existing fields of the heater settings and the location responses MUST NOT change, and existing clients
  MUST keep working without sending a date.

### Key Entities

- **OSO key expiry**: a calendar day stored with the water heater connection. It is set when the heater is connected
  or replaced, can be changed alone, and goes away with the connection.
- **Expiring key**: an entry in the reminder list. It has the location, the kind (sensor key or water heater key), the
  expiry, the days left and whether it has expired. It is derived from what is stored; nothing new is stored for it.

## Success Criteria _(mandatory)_

### Measurable Outcomes

- **SC-001**: Every connected heater has an OSO key expiry date after deploy, and Hjemme's is 19 November 2027.
- **SC-002**: A key expiring in 30 days or less appears in the list on the first read after that point. One expiring in
  31 days or more does not.
- **SC-003**: The list for all locations is answered with one request, at about the same speed as the Admin location
  list.
- **SC-004**: Replacing a key removes it from the list on the next read.
- **SC-005**: No response or log line contains a key, a key hash or a full device id.

## Assumptions

- OSO keys are valid for one year, as Roy says, and OSO offers no way to ask when a key expires. So the date is what the
  administrator enters, and one year is only the default.
- The 30-day window is fixed in this version, not a setting.
- The reminder is shown in the app only. E-mail and push are out of scope; push is part of the "Warnings" idea in the
  frontend backlog.
- Owners of a location are not reminded in this version, because only administrators can replace either key.
- The frontend (its own spec) shows the banner, the expiry details in Admin → Locations, the date in the heater form
  and the button to replace a sensor key. This spec only provides the data and the date.
