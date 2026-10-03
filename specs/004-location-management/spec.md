# Feature Specification: Location Management and Hashed Sensor Keys

**Feature Branch**: `feature/004-location-management`

**Created**: 2026-10-03

**Status**: Draft

**Input**: User description: "Location management and hashed sensor API keys. Today locations can only be added by editing the database: a location must have exactly one sensor key (the key identifies the location for the sensor) and there are no endpoints or screens to create or edit locations or keys. Add admin-only location management across the API and the web app (an admin 'Locations' page next to the existing admin Users page), generate each location's sensor key at creation and show it exactly once, let an admin rotate a key, and store sensor keys hashed so a leaked database cannot be used to impersonate a sensor. The existing key used by the running sensor must keep working."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Add a location and get its sensor key (Priority: P1)

An Admin adds a new location (for example a cabin) from the web app without touching the database. They enter its name, address, serial number and price zone, say whether it has a Norgespris agreement, and save. The system creates the location together with its sensor key and shows the full key exactly once, with a copy button, so the Admin can configure the sensor. After closing that screen the key can never be shown again.

**Why this priority**: This is the reason for the feature: today a new location needs hand-written database statements.

**Independent Test**: As Admin, create a location in the web app, copy the key, configure a sensor (or call the ingestion endpoint) with it, and confirm readings are accepted for that location; reopen the location and confirm the key is not shown.

**Acceptance Scenarios**:

1. **Given** an Admin on the Locations page, **When** they fill in name, address, serial number, a price zone from NO1-NO5 and save, **Then** the location exists and the full sensor key is displayed once with a copy button.
2. **Given** the key was displayed, **When** the Admin closes the dialog and later opens the location, **Then** the key is not shown anywhere, only non-secret information about it (see User Story 2).
3. **Given** a new location and its key, **When** the sensor sends readings with that key, **Then** they are attributed to that location (once a reader is registered, see User Story 5).
4. **Given** a serial number already used by another location, **When** an Admin tries to create a location with it, **Then** it is refused with a clear message and nothing is created.
5. **Given** a price zone other than NO1-NO5, **When** an Admin submits the form, **Then** it is refused.
6. **Given** a regular user, **When** they try to create a location, **Then** the request is refused.

---

### User Story 2 - Sensor keys are stored hashed (Priority: P1)

Sensor keys are never kept in readable form. The system keeps only a one-way fingerprint of each key, so someone who obtains a copy of the database (a backup, a leaked export) cannot use it to send fake readings as a sensor. The key that the running sensor uses today keeps working after the change without any reconfiguration.

**Why this priority**: It is a security requirement the owner explicitly asked to include now, and it shapes how keys are created and checked in every other story.

**Independent Test**: After the change, inspect the stored keys and confirm none is readable; confirm the sensor that was running before the change still has its readings accepted with its existing key.

**Acceptance Scenarios**:

1. **Given** the system after the change, **When** the stored key data is inspected, **Then** no sensor key can be read or reconstructed from it.
2. **Given** the sensor key in use before the change, **When** the change is applied and the sensor keeps sending the same key, **Then** its readings are still accepted.
3. **Given** a key that is wrong, expired or deactivated, **When** a sensor sends a reading, **Then** it is rejected.
4. **Given** any location screen or response, **When** an Admin looks at a location's key, **Then** only non-secret details are shown: its description, expiry date, whether it is active, and the last few characters to recognise it.
5. **Given** the system's logs, **When** keys are created, rotated or used, **Then** no key (and no key fingerprint beyond the non-secret hint) is ever written to a log.

---

### User Story 3 - Rotate or deactivate a sensor key (Priority: P2)

If a key is lost, exposed, or the sensor is replaced, an Admin generates a new key for the location. The new key is shown once; the old key stops working immediately. An Admin can also deactivate a key, and a key that has expired is rejected.

**Why this priority**: Without rotation, a lost key could only be fixed by editing the database, which defeats the purpose of managing keys in the app.

**Independent Test**: Rotate a location's key, then send a reading with the old key (rejected) and with the new key (accepted).

**Acceptance Scenarios**:

1. **Given** a location with a working key, **When** an Admin rotates it, **Then** a new key is displayed once and the old key is rejected from that moment on.
2. **Given** a rotated key, **When** the sensor still sends the old key, **Then** its readings are rejected until the sensor is updated; the screen warns the Admin about this before they confirm.
3. **Given** a key whose expiry date has passed, **When** a sensor uses it, **Then** it is rejected; the Locations page flags it as expired.
4. **Given** an Admin deactivates a location's key, **When** a sensor uses it, **Then** it is rejected; the Admin can generate a new key to restore service.
5. **Given** a newly created or rotated key, **When** it is stored, **Then** it expires two years after creation by default.

---

### User Story 4 - See and edit all locations (Priority: P2)

An Admin sees every location in the system, not only the ones they are linked to, and can correct a location's name, address, serial number, price zone and Norgespris flag. The Users page uses the same complete list, so an Admin can link any user to any location.

**Why this priority**: Needed to keep the data correct and to remove today's limit where the Users page only offered the Admin's own locations.

**Independent Test**: Create a second location the Admin is not linked to; confirm it appears in the Admin's Locations list and as a linking option on the Users page; edit its address and confirm the change is saved.

**Acceptance Scenarios**:

1. **Given** several locations, **When** an Admin opens the Locations page, **Then** all of them are listed with name, address, price zone, Norgespris flag, active state and key status (expiry, active, hint).
2. **Given** an Admin edits a location's details, **When** they save, **Then** the changes are stored and shown; the sensor key is unaffected.
3. **Given** a location is linked to no one, **When** an Admin opens the Users page, **Then** it is offered as a linking option.
4. **Given** a regular user, **When** they list locations, **Then** they still see only the locations they are linked to, and cannot edit any.
5. **Given** an Admin changes a location's price zone or Norgespris flag, **When** they confirm, **Then** the screen has warned them that costs shown for past hours will be recalculated with the new setting.
6. **Given** an Admin deactivates a location, **When** its sensor sends readings, **Then** they are rejected and nothing is stored.
7. **Given** a deactivated location that a regular user is linked to, **When** the user lists their locations or asks for its readings, costs or readers, **Then** it is not listed and is reported as "not found", exactly as if they had no access.
8. **Given** a deactivated location, **When** an Admin opens the Locations page, **Then** it is still listed and clearly marked inactive, with its history intact.
9. **Given** an Admin reactivates the location, **When** the sensor sends readings and the user opens the dashboard, **Then** readings are accepted again and the location is visible to its linked users again.

---

### User Story 5 - Manage a location's readers (Priority: P3)

An Admin registers and lists the readers (meters, identified by device id) of any location from the Locations page. A reader must be registered before the readings it sends are accepted.

**Why this priority**: Completes the path to a working new location, but registration is already possible by other means today (members can register readers for their own locations).

**Independent Test**: Register a reader for a new location from the web app and confirm its readings are then accepted; try registering the same device id twice and confirm it is refused.

**Acceptance Scenarios**:

1. **Given** an Admin viewing a location, **When** they register a reader with a device id, **Then** it appears in that location's reader list.
2. **Given** a device id already registered at the location, **When** an Admin registers it again, **Then** it is refused with a clear message.
3. **Given** a reader that is not registered, **When** the sensor sends its readings, **Then** they are rejected, as before.
4. **Given** a regular user linked to a location, **When** they register a reader there, **Then** it still works as before; users not linked to the location still get "not found".

---

### Edge Cases

- The Admin closes the key dialog without copying the key: the key cannot be recovered; the only remedy is to rotate the key. The dialog warns about this before it is closed.
- Two Admins rotate the same key at nearly the same time: the last rotation wins; only the key shown by the last one works.
- A location is deactivated: its sensor readings are no longer accepted and it disappears for regular users (lists and data both behave as "not found"), while Admins still see it, flagged inactive, and its history is kept. Reactivating restores everything immediately (decision recorded in Assumptions).
- A reading arrives for a deactivated location with an otherwise valid key: it is rejected with the same kind of refusal as a bad key, and nothing is stored.
- Changing a location's price zone or Norgespris flag changes how all of its past hours are priced, because costs are always calculated from the current settings, not stored. The screen warns about this; it is not blocked.
- Creating a location fails halfway (for example a validation error after the key is generated): nothing is stored and no key is shown; the Admin can retry.
- Very long or empty name, address or serial number: refused by validation with a message per field.
- A key is lost after the old plain-text copy has been converted: there is no way to read it back; rotate it.
- The sensor key of a deleted or never-existing location: rejected like any wrong key.
- The existing sensor key is converted automatically when the change is applied; if the conversion is skipped, the running sensor would be rejected, so the conversion is part of the same change, not a separate step.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST let an Admin create a location with name, address, serial number, a price zone (only NO1, NO2, NO3, NO4 or NO5), a Norgespris-agreement flag and an active flag.
- **FR-002**: Creating a location MUST also create its sensor key and display the full key exactly once, with a way to copy it; the key MUST NOT be retrievable afterwards by any means.
- **FR-003**: Serial numbers MUST be unique across locations; a duplicate MUST be refused without creating anything.
- **FR-004**: The system MUST let an Admin list all locations, including ones they are not linked to, with name, address, price zone, Norgespris flag, active state and key status.
- **FR-005**: The system MUST let an Admin edit a location's name, address, serial number, price zone and Norgespris flag, and activate or deactivate the location, without changing its sensor key.
- **FR-006**: Regular users MUST continue to see only the locations they are linked to, and MUST NOT be able to create, edit, deactivate or rotate keys of locations.
- **FR-006a**: While a location is deactivated, the system MUST reject its sensor readings and MUST treat the location as nonexistent for regular users everywhere (location list, readings, consumption, costs, readers); Admins MUST still see it, marked inactive, in location management. Reactivating MUST restore normal behavior with no data lost.
- **FR-007**: Sensor keys MUST be stored only as a one-way fingerprint; no stored value may allow a key to be read or reconstructed.
- **FR-008**: Existing sensor keys stored in readable form MUST be converted to fingerprints as part of applying this change, so that the sensor already running keeps working without reconfiguration.
- **FR-009**: The system MUST accept a reading only when its key matches a stored fingerprint of a key that is active and not expired, and MUST reject it otherwise.
- **FR-010**: The system MUST let an Admin rotate a location's key: a new key is generated and shown once, and the previous key stops being accepted immediately.
- **FR-011**: The system MUST let an Admin deactivate a location's key; an inactive key is rejected, and a new key can be generated to restore service.
- **FR-012**: Keys MUST expire two years after they are created or rotated; an expired key MUST be rejected and shown as expired to Admins.
- **FR-013**: For each key the system MUST show Admins only non-secret information: description, expiry date, active state and the last few characters.
- **FR-014**: Keys MUST NOT appear in any log, error message or response other than the single one-time display at creation or rotation.
- **FR-015**: The system MUST let an Admin register and list readers (device ids) of any location; a duplicate device id at a location MUST be refused. Users linked to a location keep their existing ability to register readers there.
- **FR-016**: The system MUST warn an Admin before they save a change to a location's price zone or Norgespris flag, because past cost figures will be recalculated.
- **FR-017**: Every location-management action (create, edit, activate/deactivate, key rotation or deactivation, reader registration) MUST be recorded in the log with who did it and which location, without secrets.
- **FR-018**: The web app MUST provide an Admin-only Locations page for these actions, and the Users page MUST offer every location for linking.

### Key Entities

- **Location**: A place with one or more readers. Name, address, unique serial number, price zone (NO1-NO5), Norgespris-agreement flag, active flag. Has exactly one sensor key.
- **Sensor key**: The secret a sensor uses to prove which location its readings belong to. Stored only as a fingerprint; has a description, a short hint (last few characters), an active flag and an expiry date. Replaced, not edited, when rotated.
- **Reader (meter)**: A device at a location, identified by device id (unique per location). Must be registered before its readings are accepted.
- **User-location link**: Existing; decides which locations a regular user can see.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An Admin can add a new location and obtain its sensor key from the web app in under 2 minutes, with no database access.
- **SC-002**: After the change, 0 sensor keys can be read from stored data, and 100% of readings from the sensor that was running before the change continue to be accepted with no sensor reconfiguration.
- **SC-003**: After a key is rotated, 100% of readings sent with the previous key are rejected from that moment on.
- **SC-004**: A key can be displayed exactly once: for every location, 0 screens or responses after the first display show any part of the key other than the last few characters.
- **SC-005**: An Admin sees 100% of locations in the Locations page and as linking options on the Users page, including locations they are not linked to; regular users see 0 locations they are not linked to.
- **SC-006**: Regular users cannot perform any location create, edit, key or admin-reader action (0 successful attempts in tests).
- **SC-007**: No key appears in logs: a search of logs produced while creating, rotating and using keys finds 0 occurrences.

## Assumptions

- Only Admins create and manage locations; there is no self-service for regular users. The system is a one-owner hobby system, so there is no approval workflow.
- Locations cannot be deleted in this feature (out of scope); they can be deactivated instead. Deactivating stops readings and hides the location from regular users while keeping all history; Admins can still see it and reactivate it. (Decision, 2026-10-03.)
- The two-year expiry is fixed for now; an Admin cannot choose a custom expiry, only deactivate or rotate.
- A key's description is generated automatically (for example "Sensor key for <location name>") and is not edited by the Admin.
- Price zones are limited to NO1-NO5 because those have a supported price source; a price-zone table, per-location time zones and changes to sensor behaviour are out of scope.
- The serial number identifies a physical installation and is therefore unique across locations.
- The "last few characters" hint of an existing key is taken from the readable key during the conversion, before it is discarded.
- Rotation takes effect immediately; there is no overlap period in which both old and new keys work.
- The web app is a separate project; this feature changes both the API and the web app, and the API is delivered first.
