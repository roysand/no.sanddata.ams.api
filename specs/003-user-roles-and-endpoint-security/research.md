# Research: User Roles and Endpoint Security

Findings from reading the current code (2026-10-03) and the decisions they lead to. There were no
open `NEEDS CLARIFICATION` items in the plan's Technical Context; this records the non-obvious facts.

## 1. Roles never reach the token today (must fix)

- `JwtTokenService.GenerateToken(user, roles)` already writes one `ClaimTypes.Role` claim per role,
  and `/api/auth/me` already reads them.
- But `LoginCommandHandler` loads the user with `FindAsync(predicate)` and passes `user.Roles` to the
  token service, and `RefreshTokenCommandHandler` loads with `GetByIdAsync`. **Neither loads the
  `Roles` navigation**: there is no `Include`, no `AutoInclude`, and no lazy loading configured. So
  `user.Roles` is empty and tokens carry no roles. (Same for `GetUsers`, which shows empty `Roles` and
  `Locations` arrays.)
- `GetByIdAsync` uses `DbContext.FindAsync`, which **ignores `AutoInclude`** (it looks up by key and
  returns the tracked instance or a plain row).

**Decision**: configure `AutoInclude()` on `User.Roles` and `User.Locations` in `UserConfiguration`
(one line each; the user table is tiny), and switch the places that need them from `GetByIdAsync` to
`FindAsync(u => u.Id == id)` (refresh handler, get-user handler, update/delete handlers where roles are
read). **Alternatives considered**: an explicit `Include` repository method (more API surface for the same
result); loading roles via `IUserRoleRepository` in the login handler (extra query and a second code
path to keep in sync).

## 2. How role-gated endpoints are expressed

- FastEndpoints supports `Roles("Admin")` next to `AuthSchemes(JwtBearerDefaults.AuthenticationScheme)`:
  unauthenticated gives 401, authenticated without the role gives 403. This matches FR-013.
- The role must arrive as `ClaimTypes.Role` on the validated principal. The token service writes it
  with that claim type and `MapInboundClaims` is on by default, so `User.IsInRole("Admin")` should work.
  This is **verified end to end in the quickstart** (login, then `/api/auth/me` and an Admin-only call)
  rather than assumed.

## 3. Own-account vs Admin rules (FR-003, FR-004, FR-013)

- Own-account actions (view, update, change password) only require a signed-in caller. The endpoint
  reads the caller id (`NameIdentifier`) and `IsInRole("Admin")` from the claims and passes them to the
  query/command as `CallerId` / `CallerIsAdmin`; the handler decides.
- A non-Admin addressing someone else's account gets **404 `User.NotFound`**, identical to a missing
  account, so the response does not reveal whether the account exists (FR-013). Admin-only actions
  return 403 from the endpoint's role check.
- **Update**: a non-Admin may not change `IsActive` (that would let a user reactivate or deactivate
  themselves): sending a different value returns 400 `User.IsActiveAdminOnly`. Email and name changes
  are allowed.
- **Change password**: the current password is required when the caller changes their own password
  (Admin included). An Admin resetting **another** user's password does not supply the current one
  (they cannot know it).

## 4. Admin role management and the last-Admin rule (FR-008, FR-009)

- Grant/revoke is `PUT` / `DELETE /api/users/{id}/roles/admin`: small, idempotent, and it avoids a
  generic role-editing API that nothing needs (Constitution VIII). The `User` role is never removed.
- "Last Admin": count **active** users holding the Admin role. Revoking Admin, deleting, or
  deactivating a user is refused with 409 `User.LastAdmin` when it would leave zero. Done in the handler
  with `IUserRoleRepository.FindAsync(...)` plus a user lookup; no new repository method. Concurrent
  requests racing to remove the last two Admins is accepted as out of scope for a one-owner system.

## 5. Seeding roles and the first Admin (FR-005, FR-006, FR-007)

- **Roles**: seeded with EF Core `HasData` in `RoleConfiguration` using fixed GUIDs, so the migration
  contains the `InsertData` (constitution: schema/data changes only through migrations). `Name` is
  matched exactly (`"Admin"`, `"User"`).
- **Existing users**: the same migration backfills the `User` role for every user without a role with a
  `Sql(...)` `INSERT ... SELECT`, so nobody loses access when endpoints are locked down.
- **First Admin**: a startup-only hosted service (`AdminBootstrapService`, in the Users slice) reads
  `Bootstrap:OwnerEmail` (default `roy@sanddata.no` in `appsettings.json`). If **no user holds Admin**
  and a user with that email exists, it grants Admin (and `User` if missing) and logs it. If any Admin
  exists it does nothing, so the setting cannot be used to take over a running system. It runs on every
  start, which also covers an owner account inserted later.
- **Known limitation**: Admin-only user creation means a completely empty database has no way to create
  the first account through the API. The owner account must be inserted once out of band for a brand-new
  environment. The current dev database already has the owner. Accepted for a one-owner hobby system.
  The documented, tested procedure (BCrypt hash generator plus the `INSERT`) is in
  [quickstart.md](./quickstart.md#appendix-creating-the-first-owner-account-in-an-empty-database); the
  implementation also copies it into `AuthenticationGuide.md`.

## 6. Meters (FR-012)

`CreateMeterCommandHandler` only checks that the location exists, and `GetMeterQueryHandler` does no
location check at all, so any signed-in user can register or read meters anywhere. **Decision**: add
`UserId` to `CreateMeterCommand` and `GetMeterQuery`; handlers call
`ILocationRepository.IsUserAssociatedAsync` and return the same `Location.NotFound` / `Meter.NotFound`
as measurements do. The location-membership check is duplicated in the Meters slice, not shared
(Constitution II).

## 7. Location links (FR-015)

`UserLocation` already exists with a composite key and FKs. New Admin-only endpoints
`PUT` / `DELETE /api/users/{id}/locations/{locationId}` insert/delete the row through
`IUserLocationRepository`. Both are idempotent (linking twice or unlinking a missing link is 204);
unknown user or location is 404. Lives in the Users slice (it is user administration).

## 8. Logging (FR-014)

New security events stay inside the existing Users range (1000-1099): refused attempts (the role/forbidden
refusals come from the framework pipeline, so the endpoint logs the unknown-user and last-admin
refusals), role granted/revoked, user deleted (existing), location linked/unlinked, bootstrap promotion.
Stable reason codes, no secrets. No new EventId range is needed.

## 9. Things deliberately not done

- No generic roles CRUD (the unmerged `origin/feature/roles_crud` is reference only; it predates the
  vertical-slice layout).
- No immediate token revocation (spec decision Q1-A): roles are read at login/refresh.
- Admin does not bypass location membership for measurement/cost data.
- No self-signup, password reset by email, lockout, or MFA.
- Constitution V still says handlers "MUST be registered explicitly in `AddInfrastructureToDI.cs`";
  the generator now registers them automatically (CLAUDE.md is correct). Not changed here; worth a
  separate small constitution PATCH amendment.
