# Manual test: location roles and owner editing (task T029)

**Status: NOT DONE. Roy must test this before the PR is merged.**

The automated tests (262) and the checks against the local database all pass, but nothing has yet exercised the
real HTTP layer: routing, authorisation, JSON shapes and the `PUT` link call without a body. That is what this
test is for. It takes about 20 minutes.

## Before you start

1. **Run this branch.** In Rider, stop the debug session and start the API again from the branch
   `feature/006-location-roles-and-owner-edit` (it is already checked out). It runs on
   `https://localhost:7130`. The migration `AddUserLocationRole` is already applied to your local database
   (and would be applied again at startup if it were not).
2. **Use throwaway data.** Locations and links cannot be deleted through the API. Create test users and a test
   location for this (`TEST-ROLES-1`), and use the clean-up SQL at the bottom afterwards. Do not leave the
   `Home` location edited.
3. **Pick a way to call the API.**
   - **Scalar** (easiest): open `https://localhost:7130/scalar/v1`, sign in with `POST /api/auth/login`, and
     use the returned token under *Authentication > Bearer*. Repeat as each user.
   - **or the web app**: check out the frontend branch `feature/location-editing-sharing`, run `npm run dev`
     (`.env` already points at `https://localhost:7130`). Its admin pages show owners, viewers and roles.
4. **Accounts you need:** an admin (you), a user **A** (will be owner) and a user **B** (will be viewer).
   Create A and B with `POST /api/users` or the admin Users page.

## The test

Write down what you see. "Expected" is what should happen.

| # | Who | Do this | Expected |
|---|---|---|---|
| 1 | Admin | Create a location `TEST-ROLES-1` (`POST /api/admin/locations`). Then `PUT /api/users/{A}/locations/{loc}` **with no body at all** (this is also what the web app's "Add location for user" does). | `204`. A is an **Owner** (check step 9). *This is the most important check: it proves a bodiless `PUT` still binds.* |
| 2 | Admin | `PUT /api/users/{B}/locations/{loc}` with body `{ "role": "Viewer" }` | `204` |
| 3 | Admin | Same call with `{ "role": "Admin" }` and with `{ "role": "1" }` | `400`, message "Role must be Owner or Viewer" |
| 4 | Admin | Try to make A a viewer: `PUT .../users/{A}/locations/{loc}` with `{ "role": "Viewer" }` while A is the only owner. Then `DELETE` A's link. | Both `409` with a message about keeping at least one owner. Nothing changes. |
| 5 | Admin | `GET /api/admin/locations` and `GET /api/users` | Locations list `users` with A = Owner, B = Viewer. Users list `locationAccess` with the role. The old `locations` / `locationIds` are still there. |
| 6 | **B** (viewer) | `GET /api/locations` | `TEST-ROLES-1` is listed with `"role": "Viewer"`, plus `serialNumber`, `isActive`, `hasNorgesPriceAgreement`. Data, usage and cost for it load normally. |
| 7 | **B** (viewer) | `PUT /api/locations/{loc}` with a name. `POST /api/meters` for the location. | Both **`404`** (not 403) |
| 8 | **A** (owner) | `PUT /api/locations/{loc}` with `{ "name": "New name", "address": "New address", "isActive": true }` | `200`. `GET /api/locations` shows the new name. Serial number, zone and Norgespris are **unchanged**. |
| 9 | **A** (owner) | Same call also sending `"serialNumber": "HACK"` and `"zone": "NO5"`. Then the same call **without** `isActive`. | The extra fields are ignored (serial and zone unchanged). Without `isActive`: `400` "The active flag is required". |
| 10 | **A** (owner) | Set `"isActive": false`. Then `GET /api/locations` as A and as B. | A still sees it, with `"isActive": false`. B no longer sees it. |
| 11 | **A** (owner) | Set `"isActive": true` again. | `200`. B sees it again, as Viewer. |
| 12 | **A** (owner) | `POST /api/meters` (`locationId`, `deviceId` `TEST:ROLES:01`), then `PUT /api/meters/{id}` with `{ "comment": "Hello" }` | `200` and `200`; the comment is saved. Device id and location unchanged. |
| 13 | **B** (viewer) | `PUT /api/meters/{id}` with a comment | **`404`** |
| 14 | Admin | Make B an owner (`{ "role": "Owner" }`), then demote A (`{ "role": "Viewer" }`) | Both `204` (a second owner is allowed; A can be demoted because B is now an owner). |
| 15 | Anyone | Any of the new calls without a token | `401` |
| 16 | A normal user | Any `PUT`/`DELETE` on `/api/users/{id}/locations/...` | `403` |

Also check once that **normal things still work** for an existing user (for example you on `Home`): the dashboard
loads, `GET /api/locations` works, and the web app's admin "Add location for user" still creates a location with a
key.

## If something fails

Note the step number, the URL and body you sent, the status code and the message, and tell Claude. Do not merge.

## Clean up afterwards (local database only)

```bash
docker exec -i nosanddataamsapi-db-1 psql -U amsadmin -d amsdb <<'SQL'
-- put the original data back
UPDATE "UserLocation" SET "Role" = 'Owner' WHERE "LocationId" = '22222222-2222-2222-2222-222222222222';
UPDATE "Location" SET "IsActive" = true WHERE "Id" = '22222222-2222-2222-2222-222222222222';
-- remove the test location, its meters, links and key, and the test users
DELETE FROM "Meter" WHERE "LocationId" IN (SELECT "Id" FROM "Location" WHERE "SerialNumber" = 'TEST-ROLES-1');
DELETE FROM "UserLocation" WHERE "LocationId" IN (SELECT "Id" FROM "Location" WHERE "SerialNumber" = 'TEST-ROLES-1');
WITH k AS (DELETE FROM "Location" WHERE "SerialNumber" = 'TEST-ROLES-1' RETURNING "ApiKeyId")
DELETE FROM "ApiKey" WHERE "Id" IN (SELECT "ApiKeyId" FROM k);
-- delete the two test users (adjust the e-mail addresses to the ones you created)
DELETE FROM "User" WHERE "Email" IN ('role.a@example.com', 'role.b@example.com');
SQL
```

## When it passes

Tell Claude "T029 passed". Then T030 follows: open the PR, merge, and check that the deploy workflow succeeded and
the migration ran on the server. After that the web app can drop its compatibility code (its T040).
