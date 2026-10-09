# Data Model: Location Roles and Owner Editing

## LocationRole (Domain)

`Owner` | `Viewer`. Stored as text (`"Owner"`, `"Viewer"`) so the database stays readable.

## UserLocation (join entity, changed)

| Column | Type | Notes |
|---|---|---|
| UserId | uuid | part of the key (unchanged) |
| LocationId | uuid | part of the key (unchanged) |
| Role | text, not null | **new**; default `Owner`, so every existing row becomes an owner link |

- `new UserLocation(userId, locationId)` keeps working and creates an `Owner` link; an overload takes a role.
- `ChangeRole(role)` sets the role.
- Invariant (enforced by handlers, not by a database constraint): a location always has at least one `Owner`.
  Several owners are allowed (legacy data and ownership transfer).
- `Location.Users` and `User.Locations` (skip navigations through this entity) are unchanged.

## Location (Domain, changed)

New method `UpdateDetails(name, address)`; `Update(...)` (admin, all fields) and `SetActive` are unchanged.

## Meter (Domain, changed)

New method `SetComment(string? comment)`; nothing else on a meter can change after registration.

## Repository surface (Application)

`IUserLocationRepository<T>`:

| Method | Returns |
|---|---|
| `IsOwnerAsync(userId, locationId)` | true if the user has an `Owner` link to the location |
| `CountOwnersAsync(locationId)` | number of `Owner` links |
| `GetForUsersAsync(userIds)` | link rows with location name for those users (for `GET /api/users`) |
| `GetForLocationsAsync(locationIds)` | link rows with user details for those locations (for `GET /api/admin/locations`) |

`ILocationRepository<T>`:

| Method | Change |
|---|---|
| `CountForUserAsync` | renamed `CountOwnedForUserAsync`, counts `Owner` links only |
| `GetForUserWithRoleAsync(userId)` | new; the user's locations with the link role; owner links always, viewer links only when the location is active; meters included |

## Response shapes

- `LocationSummaryResponse` (`GET /api/locations`, owner edit result): `Id, Name, Address, Zone, SerialNumber, HasNorgesPriceAgreement, IsActive, Role, Meters`.
- `AdminLocationResponse`: existing fields plus `Users: LocationUserResponse[]`.
- `LocationUserResponse`: `UserId, Email, FirstName, LastName, Role`.
- `UserListResponse`: existing fields plus `LocationAccess: UserLocationAccessResponse[]`.
- `UserLocationAccessResponse`: `LocationId, Name, Role`.
- `MeterResponse`: unchanged.

## State transitions

```text
Link:    (none) --PUT, no role or role Owner--> Owner
         (none) --PUT, role Viewer-----------> Viewer
         Owner <--PUT with the other role----> Viewer     (409 Location.LastOwner when it would leave no Owner)
         any    --DELETE-----------------------> (none)    (409 Location.LastOwner for the last Owner)

Active:  owner or admin sets IsActive false -> location rejects sensor readings, hidden from viewers,
         still listed for its owners; setting it true reverses this.
```
