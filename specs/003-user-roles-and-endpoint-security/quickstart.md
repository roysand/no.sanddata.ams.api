# Quickstart: validating roles and endpoint security

Prerequisites: dev database with the existing owner account (`roy@sanddata.no`) and at least one other user;
migration applied (`dotnet ef database update ...` per `DatabaseMigrations.md`); API running
(`dotnet run --project src/api/api.csproj`). Use Scalar (`/scalar/v1`) or `curl`. Contracts:
[contracts/endpoints.md](./contracts/endpoints.md). Rules: [data-model.md](./data-model.md).

## 0. Roles are seeded and the owner is Admin

1. Start the API; the log shows the bootstrap promotion once (owner gains Admin).
2. Database check: `Role` has exactly `Admin` and `User`; every user has a `UserRole` row; the owner has both.

## 1. Anonymous access is closed (User Story 1)

For each of `GET /api/users`, `GET /api/users/{id}`, `POST /api/users`, `PUT /api/users/{id}`,
`DELETE /api/users/{id}`, `PUT /api/users/{id}/password`, `POST /api/meters`, `GET /api/meters/{id}`
without a token: expect **401**. `POST /api/auth/login` and `POST /api/auth/refresh` still work anonymously.

## 2. Roles reach the token (User Story 2)

1. Log in as the owner; `GET /api/auth/me` shows `roles` containing `Admin` and `User`.
2. Log in as an ordinary user; `me` shows only `User`.
3. Call an Admin-only action (`GET /api/users`) with each token: owner **200**, ordinary user **403**.
4. Refresh the owner's token via `/api/auth/refresh` and repeat step 1 with the new access token.

## 3. Own account vs other accounts (User Story 1)

As the ordinary user: view / update / change password of **own** id: success. Same actions on **another** id:
**404 `User.NotFound`**, identical to a non-existent id. Sending `isActive` changed on own update: **400**.

## 4. Managing admins (User Story 3)

As owner: create a user (gets `User` only); `PUT .../roles/admin` for them; they log in and `me` shows `Admin`.
Revoke it again. With exactly one active Admin, try to revoke, delete and deactivate that Admin: each **409
`User.LastAdmin`**. Ordinary user calling these: **403**.

## 5. Meters respect membership (User Story 4)

As a user not linked to the `Home` location: `POST /api/meters` and `GET /api/meters/{id}` for it: **404**.
As a linked user: success.

## 6. Linking users to locations (User Story 5)

As owner: `PUT /api/users/{id}/locations/{locationId}` for the new user; they can now read that location's
measurements. `DELETE` the link; they can no longer. Repeating either call is **204**.

## Appendix: creating the first owner account in an empty database

Only needed for a brand-new environment. Creating users through the API requires an Admin, so the very
first account has to be inserted once by hand. The API then promotes it to Admin on startup (see
[research.md](./research.md) §5); you do not insert any role rows yourself.

**1. Generate the password hash** with the same library and cost the API uses (BCrypt, work factor 12).
Save as `hash.cs` anywhere and run it (needs the .NET 10 SDK; no project required):

```csharp
#:package BCrypt.Net-Next@4.0.3

Console.WriteLine(BCrypt.Net.BCrypt.HashPassword(args[0], 12));
```

```bash
dotnet run hash.cs -- "your-chosen-password"
# prints something like: $2a$12$56x0zWi2yW.2V5Fqygosde5s1Tkj4D9qiix3qCW4duLwWzaceh.qK
```

Use a strong password. Do not reuse the example hash above; it belongs to a throwaway test password.

**2. Insert the user** (PostgreSQL; paste the hash from step 1 and use the same email as the
`Bootstrap:OwnerEmail` setting, which defaults to `roy@sanddata.no`):

```sql
INSERT INTO "User" ("Id", "FirstName", "LastName", "PasswordHash", "Email", "IsActive", "CreatedAt", "UpdatedAt")
VALUES (gen_random_uuid(), 'Roy', 'Sand', '<hash from step 1>', 'roy@sanddata.no', true, now(), now());
```

For the dev container: `docker exec -i <db-container> psql -U <user> -d <database>` and paste the statement.
The migration must already have run so the `Role` rows exist (`dotnet ef database update ...`).

**3. Start (or restart) the API.** With no Admin in the system it grants the owner `Admin` (and `User`) and
logs it. Log in and check `GET /api/auth/me` shows both roles. From then on, create all other users with
`POST /api/users` as that Admin.

The bootstrap only acts while **no** Admin exists, so re-running or restarting later changes nothing.

## 7. Endpoint review (SC-007)

List every endpoint (Scalar / OpenAPI document) and confirm the only anonymous ones are login, refresh and the
documentation pages. Run `dotnet test`; run `dotnet format --verify-no-changes` on touched files.
