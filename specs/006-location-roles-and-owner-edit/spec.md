# Feature Specification: Location Roles and Owner Editing

**Feature Branch**: `feature/006-location-roles-and-owner-edit`

**Created**: 2026-10-09

**Status**: Draft

**Input**: Frontend feature `no.sanddata.ams.frontend` -> `specs/002-location-editing-sharing/` (contract in its `contracts/backend-required.md`). User description: "Every link between a user and a location gets a role, owner or viewer. The user an administrator allocates to a location is its owner. Other users given access to see its data, usage and cost are viewers. Owners can edit the non-system fields of their location (name, address, active flag) and the comment of its meters, but never the serial number, price zone, Norgespris agreement or sensor key; those stay administrator-only. Viewers can only read. Administrators can add and remove viewers and change roles. Sensor key rotation is out of scope."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Every user-location link has a role (Priority: P1)

An Admin links a user to a location as either owner or viewer. The user an Admin allocates to a location through the existing link call is its owner, so nothing changes for callers that do not mention a role. An Admin can also add another user as a viewer, change a link's role, and remove a link. All links that exist before this feature become owner links.

**Why this priority**: Every other story depends on the role.

**Independent Test**: As Admin, link user A to a location without a role (owner), link user B as viewer, read both back, then change B to owner and back, and remove B.

**Acceptance Scenarios**:

1. **Given** an Admin, **When** they link a user to a location without stating a role, **Then** the link is created as owner (existing behaviour preserved).
2. **Given** an Admin, **When** they link a user to a location as viewer, **Then** the link is created as viewer and the user can see the location.
3. **Given** a user already linked, **When** the Admin links them again, **Then** nothing is duplicated; sending a different role changes the role.
4. **Given** a location that has an owner, **When** an Admin links another user as owner, **Then** both are owners. A location may have several owners; the rule is that it never has none. To transfer ownership the Admin adds the new owner, then demotes or removes the old one.
5. **Given** a location's only owner, **When** an Admin tries to remove that link or change it to viewer, **Then** it is refused with `409` and a clear message.
6. **Given** existing data, **When** the change is applied, **Then** every existing link becomes an owner link and every user keeps seeing the same locations.
7. **Given** a regular user, **When** they try to link, unlink or change a role, **Then** it is refused with `403`.

---

### User Story 2 - An owner edits their location (Priority: P1)

An owner changes the name, address and active flag of a location they own. They cannot change anything else: the serial number, price zone, Norgespris agreement and sensor key are not part of this request at all. Turning the active flag off has the same effect as when an Admin does it: the location stops accepting sensor readings. An owner can turn it on again.

**Why this priority**: This is what the feature is for: users fix their own data without an Admin.

**Independent Test**: As an owner, change the name and address and confirm `GET /api/locations` shows them; as a viewer, try the same and confirm it is refused.

**Acceptance Scenarios**:

1. **Given** an owner and valid values, **When** they update their location, **Then** name, address and active flag are saved and the updated location is returned.
2. **Given** the request, **When** it is sent, **Then** it contains and can change only name, address and active flag; serial number, zone, Norgespris agreement and key are untouched.
3. **Given** an empty name or address, or one longer than 100 characters, **Then** `400`.
4. **Given** a viewer, a user not linked to the location, or a location that does not exist, **When** they try to update it, **Then** the answer is `404` in all three cases, so non-owners learn nothing about it.
5. **Given** an unauthenticated caller, **Then** `401`.
6. **Given** an owner who turns the location off, **When** sensor readings arrive for it, **Then** they are rejected as for an Admin deactivation; **and when** the owner turns it on again, **Then** readings are accepted.
7. **Given** a change to the active flag, **Then** it is logged with the acting user, as for the Admin edit.
8. **Given** an Admin, **When** they use the existing Admin edit, **Then** it keeps working unchanged for all fields.

---

### User Story 3 - An owner edits a meter's comment, and only owners register meters (Priority: P1)

An owner changes the comment of a meter at their location. Registering a new meter at a location is limited to owners (and Admins); a viewer can no longer register one.

**Why this priority**: Without it, a viewer could change a location's data, which defeats the role.

**Independent Test**: As owner, change a meter's comment; as viewer, try the same and try to register a meter; both are refused.

**Acceptance Scenarios**:

1. **Given** an owner and a comment of at most 200 characters (or none), **When** they update a meter at their location, **Then** the comment is saved and the meter returned.
2. **Given** a comment longer than 200 characters, **Then** `400`.
3. **Given** a viewer, a stranger, or a meter that does not exist, **When** they update a meter's comment, **Then** `404`.
4. **Given** a viewer, **When** they register a meter at the location, **Then** `404` (same answer as for a location they cannot manage).
5. **Given** an owner or an Admin, **When** they register a meter, **Then** it works as today.
6. **Given** a meter update, **Then** nothing else about the meter (device id, location) can change.

---

### User Story 4 - Read models show roles, shared locations and inactive ones (Priority: P2)

Users and the web app can see who has access and in which role. A user's own location list includes the locations shared with them, their role, and the facts the edit screen needs. Owners also see their locations when inactive, so they can turn them on again; viewers see only active locations. Admins see, per location, who has access with which role, and per user, which locations they have with which role.

**Why this priority**: The screens cannot show roles or offer the edit and re-activate actions without it.

**Independent Test**: Create an owner and a viewer for a location; read `GET /api/locations` as each; deactivate the location and read again; read the admin lists.

**Acceptance Scenarios**:

1. **Given** a signed-in user, **When** they list their locations, **Then** each item carries `role`, `isActive`, `serialNumber` and `hasNorgesPriceAgreement` in addition to today's fields, and never any sensor key information.
2. **Given** an inactive location, **When** its owner lists their locations, **Then** it is included with `isActive: false`; **when** a viewer lists theirs, **Then** it is not included.
3. **Given** a location shared with a viewer, **When** the viewer reads its data, usage and cost, **Then** they get the same data as the owner (read access follows the link, not the role).
4. **Given** an Admin, **When** they list all locations, **Then** each carries `users` with user id, email, first and last name and role.
5. **Given** an Admin, **When** they list users, **Then** each user carries their locations with `locationId`, `name` and `role`; the existing `locations` and `locationIds` fields remain.
6. **Given** a regular user, **When** they call the admin lists, **Then** `403`.

---

### User Story 5 - The limit of self-created locations counts owned locations only (Priority: P3)

The limit of 4 locations a user can create for themselves counts only locations they own. Being a viewer of other locations does not use it up.

**Why this priority**: Prevents sharing from silently blocking a user from creating their own locations.

**Independent Test**: Make a user viewer of 4 locations; they can still create their own location; with 4 owned locations the fifth is refused.

**Acceptance Scenarios**:

1. **Given** a user who is viewer of 4 locations, **When** they create their own, **Then** it succeeds.
2. **Given** a user who owns 4 locations, **When** they create another, **Then** `409` as today.
3. **Given** an Admin adding locations for a user, **Then** no limit applies, as today.

---

### Edge Cases

- A location with two links before the change (the local database already has this): both become owners; neither can be removed or demoted until the other is an owner, so a location never ends with none.
- An Admin removes a viewer who is signed in: the location disappears from their next list; an access token already issued does not matter because access is checked per request.
- An owner deactivates their location: it still appears for them, not for viewers; sensor readings are rejected.
- Linking a user who does not exist or a location that does not exist: `404`, as today.
- Two requests change the same location at once: the last save wins; neither corrupts the role rules.
- A user who is deleted: their links go with them (existing behaviour); if that removes the only owner, the location is left without an owner and an Admin can allocate a new one.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every user-location link MUST carry a role, `Owner` or `Viewer`.
- **FR-002**: `PUT /api/users/{userId}/locations/{locationId}` (Admin) MUST accept an optional role and MUST default to `Owner` when none is sent. It MUST remain idempotent; sending a different role to an existing link changes the role.
- **FR-003**: A location MUST always have at least one owner. The API MUST refuse removing or demoting the last owner with `409` and a clear message. Several owners are allowed, so ownership can be transferred by adding the new owner before demoting or removing the old one.
- **FR-004**: The migration MUST turn every existing link into an owner link and MUST NOT remove any link.
- **FR-005**: An owner MUST be able to update the name, address and active flag of their location through a request that contains only those fields; name and address are required and at most 100 characters.
- **FR-006**: No request by an owner MUST be able to change serial number, price zone, Norgespris agreement or the sensor key.
- **FR-007**: A user who is not an owner of a location (viewer, unlinked, or location missing) MUST receive `404` for owner update, meter update and meter registration at it; unauthenticated callers receive `401`.
- **FR-008**: An owner MUST be able to update the comment (optional, at most 200 characters) of a meter at their location; nothing else on the meter changes.
- **FR-009**: Registering a meter MUST require owner (or Admin) rights at the location; viewers MUST be refused.
- **FR-010**: Viewers MUST keep read access to the location's data, usage and cost; read access MUST follow the link, not the role.
- **FR-011**: `GET /api/locations` MUST return, per location, `role`, `isActive`, `serialNumber` and `hasNorgesPriceAgreement` in addition to today's fields, MUST include inactive locations for their owners only, and MUST NOT include any sensor key information.
- **FR-012**: `GET /api/admin/locations` MUST return, per location, the users with access (`userId`, `email`, `firstName`, `lastName`, `role`).
- **FR-013**: `GET /api/users` MUST return, per user, the locations with `locationId`, `name` and `role`, and MUST keep `locations` and `locationIds`.
- **FR-014**: The limit of self-created locations MUST count owner links only; the Admin path stays unlimited.
- **FR-015**: Changes to a link's role and owner edits of a location or meter MUST be logged with the acting user, using the existing structured logging and without secrets.
- **FR-016**: The existing Admin location edit, link and unlink calls MUST keep working for callers that do not use the new features.

### Key Entities

- **User-location link**: Connects a user to a location. Now carries a role, `Owner` or `Viewer`. A location has at least one owner (usually exactly one) and any number of viewers.
- **Location**: Has name, address (owner-editable), active flag (owner-editable) and serial number, price zone, Norgespris agreement and sensor key (Admin-only).
- **Meter**: A reader at a location; its comment is owner-editable, its device id and location are not.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every user keeps seeing exactly the locations they saw before the change, immediately after it is applied.
- **SC-002**: 100% of attempts by viewers, strangers or unauthenticated callers to change a location or meter are refused, and none of the refusals reveals whether the location exists.
- **SC-003**: 100% of owner updates leave serial number, zone, Norgespris agreement and sensor key unchanged, verified by tests that submit and then re-read them.
- **SC-004**: A location never ends up with zero owners through the link calls, verified by tests for remove and demote, and ownership can be transferred without ever having zero owners.
- **SC-005**: A viewer receives the same measurement and cost data as the owner for the shared location.
- **SC-006**: The frontend's owner and administrator screens (feature 002 of the web app) work against this API without further backend changes.

## Assumptions

- The web app is a separate repository; its contract with this API is `specs/002-location-editing-sharing/contracts/backend-required.md` there. This spec follows it.
- Roles are two values for now (`Owner`, `Viewer`); more roles are out of scope.
- Sharing is done by Admins only. Owners cannot add viewers themselves.
- Sensor key rotation, deleting locations or meters, and owners inviting viewers are out of scope. Key rotation by non-admins is in the web app's `backlog.md`.
- The default error body lists messages but not the error code; making the code available to clients is a separate improvement and not required here (the web app distinguishes errors by status and message).
- A schema change is needed (a role on the link); it ships as an EF Core migration, per the constitution.
