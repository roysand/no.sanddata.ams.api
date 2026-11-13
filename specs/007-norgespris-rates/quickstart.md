# Quickstart: validating Norgespris rates

Prerequisites: PostgreSQL running, `src/api/local.settings.json` in place, an Admin account, and at least one
location with a Norgespris agreement and some measurements. Contract: [contracts/endpoints.md](contracts/endpoints.md).
Data rules: [data-model.md](data-model.md).

## 1. Build, test, migrate

```bash
dotnet build
dotnet test
dotnet format --verify-no-changes
dotnet ef database update --project src/Infrastructure/Infrastructure.csproj --startup-project src/api/api.csproj
dotnet run --project src/api/api.csproj
```

Expected: tests pass; the migration creates `NorgesPrisRate` with one row (`2020-01-01`, 0.40, 0).

## 2. Nothing changes before a new rate exists (SC-002)

Before applying the migration, note the cost endpoint results for one hour and one day of a Norgespris location. After
the migration, request the same ones. Expected: identical numbers.

## 3. Add a future rate, see costs on both sides of it (US1, US2)

As Admin (token from `POST /api/auth/login`):

1. `GET /api/admin/norgespris` returns one rate with `status: InEffect`.
2. `POST /api/admin/norgespris` with `{ "validFrom": "<a date a few days ahead>", "ratePerKwh": 0.50, "taxPerKwh": 0 }`
   returns `201`.
3. List again: two rates, the seeded one `InEffect`, the new one `Upcoming`.
4. Request hourly cost for an hour before and an hour after that date for the same location. Expected: 0.40 before,
   0.50 after.
5. Check the midnight hour: for a start date of `D`, the hour starting at `D-1 22:00Z` (summer) or `D-1 23:00Z` (winter)
   is 00:00 Oslo time on `D` and uses 0.50; the hour before it uses 0.40.

## 4. Refusals (US2, US3)

| Call | Expected |
|---|---|
| POST the same `validFrom` again | `409 NorgesPris.DateExists` |
| POST a negative rate, or `0.12345` | `400` |
| POST `validFrom` yesterday while a later rate exists | `409 NorgesPris.NotNewest` |
| DELETE the seeded rate | `409 NorgesPris.AlreadyStarted` |
| DELETE the upcoming rate | `204`; list shows one rate; costs unchanged |
| Any of the three with a non-Admin token / no token | `403` / `401` |

## 5. Correcting a started rate (US3)

With a rate that started yesterday and is the newest: POST a new rate dated today. Expected `201`, the earlier one
becomes `Superseded`, and today's costs use the new rate while yesterday's keep the old one.

## 6. Configuration is gone (FR-018)

`src/api/appsettings.json` has no `NorgesPris` section; the API starts and prices hours from the table only.

## 7. Frontend (after the page is built in `no.sanddata.ams.frontend`)

Sign in as Admin: the Norgespris page lists the rates with the one in effect highlighted. Add a rate dated in the past
and confirm the warning appears first. Add a future rate and delete it. Sign in as a regular user: no menu entry, and
the address shows nothing.
