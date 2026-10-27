# Contracts: endpoints added or changed

All JSON is camelCase on the wire. Errors use the API's existing error body (messages under `errors`,
a `message`; the error code is not in the body). `LocationRole` is `"Owner"` or `"Viewer"`.

## Changed

### `PUT /api/users/{userId}/locations/{locationId}` (Admin)

- **Body (optional)**: `{ "role": "Owner" | "Viewer" }`. No body, or no `role`, means `Owner`.
- **204**: link created, or already existed with that role, or the role was changed.
- **400**: `role` present but not `Owner` or `Viewer` (case-insensitive accepted, stored canonical).
- **401 / 403 / 404**: as today (404 for an unknown user or location).
- **409** (`Location.LastOwner`): the change would demote the location's last owner.

### `DELETE /api/users/{userId}/locations/{locationId}` (Admin)

- **204 / 401 / 403 / 404**: as today.
- **409** (`Location.LastOwner`): the link is the location's last owner.

### `GET /api/locations` (any signed-in user)

Item: `{ id, name, address, zone, serialNumber, hasNorgesPriceAgreement, isActive, role, meters }`.
Owners also receive their inactive locations; viewers only active ones. No key information.

### `GET /api/admin/locations` (Admin)

Existing item plus `users: [{ userId, email, firstName, lastName, role }]`.

### `GET /api/users` (Admin)

Existing item plus `locationAccess: [{ locationId, name, role }]`. `locations` and `locationIds` stay.

### `POST /api/meters`

Non-admin callers must be an `Owner` of the location; a viewer or stranger gets `404` (as for a missing location).

### `POST /api/locations` (self-service create, feature 005)

The 4-location limit counts `Owner` links only. Everything else is unchanged.

## New

### `PUT /api/locations/{id}` (signed-in owner of the location)

- **Body**: `{ "name": string, "address": string, "isActive": boolean }`. Nothing else is accepted or changed.
- **200**: the updated location, shaped as an item of `GET /api/locations` (role `Owner`).
- **400**: name or address empty or longer than 100 characters.
- **401**: not signed in.
- **404**: the location does not exist, or the caller is not an owner of it (the same answer for both).

### `PUT /api/meters/{id}` (owner of the meter's location, or Admin)

- **Body**: `{ "comment": string | null }`, at most 200 characters.
- **200**: `MeterResponse` (`id, locationId, deviceId, meterId, meterType, comment, isActive`).
- **400**: comment longer than 200 characters.
- **401**: not signed in.
- **404**: the meter does not exist, or the caller is neither an owner of its location nor an Admin.
