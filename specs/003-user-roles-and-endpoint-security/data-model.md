# Data Model: User Roles and Endpoint Security

No new tables. This feature fills tables that already exist (`Role`, `UserRole`) and adds seed data, one
backfill and two EF navigation settings. All of it ships in **one migration**.

## Existing entities (unchanged shape)

| Entity | Key | Relevant fields |
|---|---|---|
| `Role` | `Id` (Guid) | `Name` (max 100), `Description` (max 100), `IsActive` |
| `UserRole` | (`UserId`, `RoleId`) | `AssignedAt`; many-to-many join between `User` and `Role` |
| `UserLocation` | (`UserId`, `LocationId`) | many-to-many join between `User` and `Location` |
| `User` | `Id` | `IsActive`, `Roles`, `Locations` navigations |

## Seed data (migration)

| Role | Fixed Id | Description |
|---|---|---|
| `Admin` | stable GUID chosen at implementation time, hard-coded in the migration | Manages users, roles and location links; also a normal user |
| `User` | stable GUID chosen at implementation time | Uses own account and own locations |

- Names are matched **exactly** (`"Admin"`, `"User"`); they are exposed as constants
  (`Domain.Common.RoleNames`) so handlers and tests never use string literals.
- Seeded through `HasData` in `RoleConfiguration` so the migration contains the `InsertData`; `CreatedAt`
  and `UpdatedAt` are set explicitly because the audit columns are filled by `SaveChanges`, which seed data
  bypasses.

## Backfill (same migration)

```text
for every existing User with no UserRole row  ->  insert UserRole(User, role "User", now)
```

Written as one `migrationBuilder.Sql(...)` `INSERT ... SELECT ... WHERE NOT EXISTS`, idempotent. After this
runs, every existing user is an ordinary user (FR-006); the owner becomes Admin at first startup (see below),
not in the migration, because that depends on configuration.

## Model configuration changes (no schema impact)

- `UserConfiguration`: `Navigation(u => u.Roles).AutoInclude()` and
  `Navigation(u => u.Locations).AutoInclude()`, so queries loaded with `FindAsync(predicate)` return
  roles/locations. Does **not** change the schema, but is included in the model snapshot if EF records it
  (it normally does not).

## State and rules

- A user always holds `User`. `Admin` is an additional role; Admin therefore implies everything `User` can do.
- **Last-Admin invariant**: the number of **active** users holding `Admin` must never reach 0 through any
  accepted request (revoke Admin, delete user, deactivate user all check it).
- **Bootstrap**: at every application start, if no user holds `Admin` and a user with the configured owner
  email exists, that user gains `Admin` (and `User` if missing). Otherwise nothing changes.
- Role and location changes take effect at the user's next sign-in or token refresh (tokens are not revoked).

## Configuration

| Key | Default | Purpose |
|---|---|---|
| `Bootstrap:OwnerEmail` | `roy@sanddata.no` (in `appsettings.json`) | Account promoted to Admin when no Admin exists |
