# Research: Location Roles and Owner Editing

No `NEEDS CLARIFICATION` items remain. The decisions below come from reading the API on `main`
(features 004 and 005 merged) and the web app's contract.

## 1. Where the role lives

- **Decision**: A `Role` column on the existing `UserLocation` join entity (string-converted enum
  `LocationRole { Owner, Viewer }`), migration default `Owner`.
- **Rationale**: `UserLocation` is already the `UsingEntity` for `Location.Users` and `User.Locations`
  (`LocationConfiguration`, `UserConfiguration`). Adding a payload column to the join entity leaves both skip
  navigations untouched, so every read that asks "is this user linked to the location?" (all dashboard
  reads, `GET /api/locations`, `GET /api/meters/{id}`) keeps working and viewers keep read access with no
  code change (spec FR-010).
- **Alternatives considered**: A separate `LocationOwner` table. Rejected: a second source of truth for the
  same relationship and more joins. A role on `User`. Rejected: the role is per location, not global.
- **Migration**: `AddUserLocationRole`; `HasDefaultValue(LocationRole.Owner)` so existing rows become owners
  without a data step. Verified afterwards on the local database (the one with a location linked to two users).

## 2. Owner checks

- **Decision**: New methods on `IUserLocationRepository`: `IsOwnerAsync(userId, locationId)`,
  `CountOwnersAsync(locationId)`, `GetForUsersAsync(userIds)` and `GetForLocationsAsync(locationIds)` (the
  last two return link rows for the read models). `ILocationRepository` gains
  `GetForUserWithRoleAsync(userId)` and `CountForUserAsync` becomes `CountOwnedForUserAsync`.
- **Rationale**: Handlers depend on repository interfaces (Principle I); the existing `FindAsync` on the
  generic repository covers single-link lookups.
- **Non-owners get `404`**: handlers return `Error.NotFound("Location.NotFound")` when `IsOwnerAsync` is
  false, exactly as `CreateMeterCommandHandler` does today for unlinked users, so a viewer cannot tell a
  location they may only view from one that does not exist (spec FR-007).

## 3. The last-owner rule and its race

- **Decision**: Link (role change) and unlink handlers read `CountOwnersAsync` and refuse
  (`Error.Conflict("Location.LastOwner", ...)`, 409) when the change would leave none. Check-then-save, no
  transaction isolation tricks.
- **Rationale**: Only administrators call these; two admins demoting the last two owners in the same
  second is not a realistic case for this system, and the cost of the fix (serialisable transaction or a
  database constraint) is out of proportion (Principle VIII). The spec's edge case says the last save wins
  without corrupting the rules; the worst outcome is a location with no owner that an admin can fix by
  linking a new owner.
- **Several owners are allowed** (decided with the web app, 2026-10-09): existing data already has
  locations with two links, and a "never a second owner" rule would make transferring ownership
  impossible without passing through zero owners.

## 4. Link endpoint compatibility

- **Decision**: `PUT /api/users/{id}/locations/{locationId}` keeps its path and success code. It gains an
  optional body `{ "role": "Owner" | "Viewer" }`; no body, or no `role`, means `Owner`. An existing link with
  a different role gets the new role (idempotent otherwise). The endpoint changes from
  `EndpointWithoutRequest` to `Endpoint<LinkUserLocationRequest>` binding `Id` and `LocationId` from the route.
- **Rationale**: The web app's admin flow (`createLocationForUser`) calls it without a body and must keep
  producing owners.
- **Risk to check in a test**: a PUT with no body and no `Content-Type` must bind (FastEndpoints binds the
  route and ignores the absent body). The web app's client already omits `Content-Type` when there is no body.

## 5. Owner edit shape

- **Decision**: `PUT /api/locations/{id}` with `{ name, address, isActive }`. A new domain method
  `Location.UpdateDetails(name, address)` changes only those two; `SetActive` already exists. The command
  has no serial, zone or Norgespris member, so those cannot be changed through it (FR-006), by shape.
- **Response**: `LocationSummaryResponse` (the same shape as one item of `GET /api/locations`, role `Owner`).
- **Logging**: reuse `LocationUpdated` and `LocationActiveChanged` (acting user = owner). Deactivation
  semantics are unchanged: the ingestion path already rejects readings for inactive locations (feature 004).
- **Administrators**: the owner endpoint is for owners only; an Admin uses `PUT /api/admin/locations/{id}`.
  An Admin who is not an owner gets `404` here by design.

## 6. Meters

- **Decision**: `PUT /api/meters/{id}` with `{ comment }` (`Meter.SetComment`), allowed for an owner of the
  meter's location or an Admin, otherwise `404`. `CreateMeterCommandHandler`: the non-admin branch changes
  from `IsUserAssociatedAsync` to `IsOwnerAsync`, so viewers cannot register meters.
- **`GET /api/meters/{id}`**: stays link-based, so a viewer can still read a meter.
- **Note**: this endpoint did not exist before, even for administrators.

## 7. Read models

- **`GET /api/locations`**: `LocationSummaryResponse` gains `SerialNumber`, `HasNorgesPriceAgreement`,
  `IsActive`, `Role`. The query returns links joined to locations where the link is `Owner`, or the location
  is active (so owners also see their inactive locations, viewers do not). No key information is added.
- **`GET /api/admin/locations`**: `AdminLocationResponse` gains `Users` (`UserId, Email, FirstName,
  LastName, Role`), filled from `GetForLocationsAsync` plus the users already loaded. The create/update
  responses reuse the type; the update handler fills `Users` too, the create handler fills it with the linked
  user when there is one.
- **`GET /api/users`**: `UserListResponse` gains `LocationAccess` (`LocationId, Name, Role`), built from
  `GetForUsersAsync` for the page's user ids (one query per page, not per user). `Locations` and
  `LocationIds` stay.
- **Why not change the `User` entity**: keeps `User.Locations` as is; the role is read from the join rows only
  where a response needs it.

## 8. Limit of self-created locations

- **Decision**: `CountOwnedForUserAsync(userId)` counts owner links only and replaces `CountForUserAsync`
  in `CreateLocationCommandHandler`. Noted on PR #12 on 2026-10-09.

## 9. Deleting a user

- **Finding**: Deleting a user removes their link rows with them (existing behaviour). If that was a
  location's only owner the location is left without one. The spec accepts this; an Admin links a new owner.
  No new rule is added (Principle VIII).

## 10. Out of scope, noted for later

- The error body lists messages but not the error `code` (found while building the web app). Making the code
  available to clients is a separate, cross-cutting improvement.
- Sensor key rotation by non-admins, deleting locations or meters, and owners inviting viewers.
