# Feature Specification: User Creates Own Location

**Feature Branch**: `feature/005-user-creates-own-location`

**Created**: 2026-10-08

**Status**: Implemented (pending review)

**Input**: Backlog note `specs/_backlog/user-creates-own-location.md`, needed by the frontend feature
`no.sanddata.ams.frontend` -> `specs/001-location-meter-onboarding/` (User Story 1).

## User Scenarios & Testing

### User Story 1 - A signed-in user creates a location that belongs to them (Priority: P1)

Any signed-in user can create a location (name, address, serial number, price zone, optional Norgespris
agreement and active flag). The location is created together with its sensor key and is linked to the
caller in the same step, so it immediately appears in `GET /api/locations` for that user. The full
sensor key is returned only in this response.

**Independent Test**: As a non-admin user, `POST /api/locations`; expect `201` with the key; then
`GET /api/locations` lists the new location; the user can register a reader at it.

**Acceptance Scenarios**:

1. **Given** a signed-in non-admin user, **When** they create a location with valid data, **Then** `201`
   with the location and the key, and the user is linked to it.
2. **Given** an invalid price zone or missing/over-long field, **Then** `400` (same rules as the admin endpoint).
3. **Given** a serial number already used by another location, **Then** `409` and nothing is created or linked.
4. **Given** an unauthenticated caller, **Then** `401`.
5. **Given** an administrator uses this endpoint, **Then** the location is linked to the administrator
   (they create it for themselves); the existing admin endpoint is unchanged and still does not link.

## Requirements

- **FR-001**: `POST /api/locations` MUST accept any authenticated user (JWT) and the same body as
  `POST /api/admin/locations`.
- **FR-002**: The location, its sensor key and the caller's link MUST be created in a single save
  (all or nothing).
- **FR-003**: The key MUST be returned only in this response and MUST NOT be logged or stored in plain form.
- **FR-004**: Validation and conflict rules MUST be the same as for the admin endpoint.
- **FR-005**: `POST /api/admin/locations` MUST keep its current behaviour (no link, no limit).
- **FR-006**: A user MUST NOT be linked to more than 4 locations through this endpoint. At 4 or more the request is
  refused with `409` and code `Location.LimitReached` and the message "You can have at most 4 locations. Ask an
  administrator to add more." Administrators add more through the admin endpoint plus the link endpoint.

## Out of scope / open questions

- The limit counts every location the user is linked to (active or not), because the data does not record who created a location. Locations an administrator linked to the user therefore count too.
- Non-admins cannot rotate their own key; an administrator still does that.
- Public self-registration (creating the account itself) is a separate feature.
