# API Contracts: User Roles and Endpoint Security

Auth legend: **anon** = no authentication; **user** = any signed-in user (JWT); **self-or-admin** = signed-in
and either the account owner or an Admin; **admin** = signed-in with the `Admin` role.

Status codes for protected actions: no/invalid token = **401**; signed in but missing the `Admin` role on an
admin-only action = **403**; self-or-admin action on someone else's account by a non-Admin = **404
`User.NotFound`** (same as a missing account, so existence is not revealed).

## Changed endpoints

| Method and path | Before | After | Notes |
|---|---|---|---|
| `POST /api/users` | anon | **admin** | New user gets the `User` role. Body unchanged. |
| `GET /api/users` | anon | **admin** | Response now actually lists each user's `roles` and `locations`. |
| `GET /api/users/{id}` | anon | **self-or-admin** | |
| `PUT /api/users/{id}` | anon | **self-or-admin** | A non-Admin sending a changed `isActive` gets 400 `User.IsActiveAdminOnly`. |
| `DELETE /api/users/{id}` | anon | **admin** | 409 `User.LastAdmin` if it would leave no active Admin. |
| `PUT /api/users/{id}/password` | anon | **self-or-admin** | `currentPassword` required when changing your own; an Admin changing another user's password omits it. |
| `POST /api/meters` | user | **user + member of the location** | 404 `Location.NotFound` if not linked. |
| `GET /api/meters/{id}` | user | **user + member of the meter's location** | 404 `Meter.NotFound` if not linked. |
| `POST /api/auth/login`, `POST /api/auth/refresh` | anon | anon (unchanged) | Now return/issue the user's real roles. |

Unchanged: `GET /api/auth/me`, `GET /api/locations`, measurements, consumption and electricity-cost endpoints,
sensor ingestion by API key.

## New endpoints (all **admin**)

### `PUT /api/users/{id}/roles/admin`
Grant the Admin role. Idempotent. **204** on success; **404** `User.NotFound` if the user does not exist.

### `DELETE /api/users/{id}/roles/admin`
Revoke the Admin role. Idempotent (revoking from a non-Admin is 204). **204** on success; **404**
`User.NotFound`; **409** `User.LastAdmin` if the user is the last active Admin.

### `PUT /api/users/{id}/locations/{locationId}`
Link a user to a location. Idempotent. **204**; **404** `User.NotFound` or `Location.NotFound`.

### `DELETE /api/users/{id}/locations/{locationId}`
Remove the link. Idempotent. **204**; **404** `User.NotFound` or `Location.NotFound`.

## Response shape changes

`GET /api/users` items and `GET/PUT /api/users/{id}` already declare `roles: string[]` and
`locations: string[]`; they now contain data instead of always being empty. No field is added or removed.

## Error codes added

| Code | HTTP | When |
|---|---|---|
| `User.NotFound` | 404 | Existing code; now also returned to non-Admins for other people's accounts |
| `User.LastAdmin` | 409 | Revoke/delete/deactivate would leave no active Admin |
| `User.IsActiveAdminOnly` | 400 | A non-Admin tried to change `isActive` |
| `Location.NotFound` / `Meter.NotFound` | 404 | Existing codes; now also for non-members |
