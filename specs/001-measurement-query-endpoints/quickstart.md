# Quickstart: Validating Measurement Query Endpoints

Manual end-to-end validation (see research.md §5 — no automated test project exists in this repo
yet). Run against local dev (`dotnet run --project src/api/api.csproj`, DB via
`docker compose -f compose.db.yaml up`).

## Prerequisites

- A logged-in JWT (`POST /api/auth/login`) for a user associated with at least one `Location`
  that has registered `Meter`(s) and ingested `Measurement` data (see existing
  `POST /api/meters` + `POST /api/measurements` flows to seed this if needed).
- `$TOKEN` = the returned access token, `$LOCATION_ID` = a location the user is associated with.

## Scenarios

**US1 — recent usage, no explicit range**:
```bash
curl -H "Authorization: Bearer $TOKEN" "http://localhost:5231/api/measurements?locationId=$LOCATION_ID"
```
Expect `200`, `items` containing only readings from the last 24 hours, ordered oldest→newest.

**US2 — explicit historical range**:
```bash
curl -H "Authorization: Bearer $TOKEN" \
  "http://localhost:5231/api/measurements?locationId=$LOCATION_ID&from=2026-09-01T00:00:00Z&to=2026-09-08T00:00:00Z"
```
Expect `200`, only readings within that window.

**US2 — invalid range rejected**:
```bash
curl -H "Authorization: Bearer $TOKEN" \
  "http://localhost:5231/api/measurements?locationId=$LOCATION_ID&from=2026-09-08T00:00:00Z&to=2026-09-01T00:00:00Z"
```
Expect `400 Validation.InvalidRange`.

**US3 — list my locations**:
```bash
curl -H "Authorization: Bearer $TOKEN" "http://localhost:5231/api/locations"
```
Expect `200`, an array containing exactly the locations this user is associated with, each with
its `meters`.

**FR-009 — latest reading**:
```bash
curl -i -H "Authorization: Bearer $TOKEN" "http://localhost:5231/api/measurements/latest?locationId=$LOCATION_ID"
```
Expect `200` with one reading if data exists, else `204`.

**FR-001 — authorization boundary**:
```bash
curl -i -H "Authorization: Bearer $TOKEN" "http://localhost:5231/api/measurements?locationId=00000000-0000-0000-0000-000000000000"
```
Expect `404 Location.NotFound` — same response whether the ID is garbage or belongs to another
user (verify with a real other-user location ID too, if a second test user is available).

## Done when

All six scenarios above return the expected status code and payload shape.
