# Backlog: User roles and endpoint security (future feature 003)

Status: idea, not specified yet. Written 2026-10-03 while finishing 002. Start with `speckit-specify`
on a new branch (suggested name: `feature/003-user-roles-and-endpoint-security`).

## Why this matters

Found while answering "should the system have an admin role?":

- The `Role` and `UserRole` tables exist, but the dev database has **zero roles** and **no endpoint is
  gated by role** anywhere in the code.
- **Every user-management endpoint is `AllowAnonymous()`** (`src/Features/Users/Endpoints/`):
  `POST /api/users`, `GET /api/users`, `GET /api/users/{id}`, `PUT /api/users/{id}`,
  `DELETE /api/users/{id}`, `PUT /api/users/{id}/password`. Anyone who can reach the API can list or
  delete users or change anyone's password without logging in.
- Harmless on localhost. **Must be closed before the API is reachable from the internet** (Docker /
  Azure deployment is planned), so this should come before any deploy.

## Proposed scope (to be refined in the spec)

- **Roles:** keep it small - `Admin` and `User`. (`Manager` from the CLAUDE.md example is not needed yet.)
- **User endpoints:**
  - `GET /api/users` and `DELETE /api/users/{id}`: Admin only.
  - Read / update / change password for **own account** (`{id}` equals the JWT user id), or Admin for anyone.
  - Create user: Admin only (self-signup can come later if wanted).
- **Location assignment:** linking a user to a location (`UserLocation`) is probably an Admin action; today
  it can only be done directly in the database.
- **Bootstrap:** seed the `Admin` and `User` roles in a migration and give `roy@sanddata.no` the Admin role,
  since no endpoint can create the first admin.
- **JWT:** the login token must carry the role claim so `Roles("Admin")` works. Not yet checked whether
  `JwtTokenService` already adds it.

## Related decision (parked, not part of 003)

Time zone: not adding a per-user time zone. Daily cost currently uses a fixed `Europe/Oslo` day
(`Features/ElectricityCost/Services/CostTime.cs`). If a location outside Norway is ever added, add
`Location.TimeZone` (default `Europe/Oslo`) with a migration - on the location, not the user, because a cost
day belongs to where the power is used. Users' display time zone is a frontend concern (API returns UTC).
