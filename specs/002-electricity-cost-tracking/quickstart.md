# Quickstart: Validating Electricity Cost Tracking

Manual end-to-end validation (no automated test project in this repo — same convention as
001). Run against local dev (`dotnet run --project src/api/api.csproj`, DB via
`docker compose -f compose.db.yaml up`).

## Prerequisites

- An ENTSO-E security token and a location whose `Zone` is one of `NO1`-`NO5` (see research.md
  §4 for how to request a token — this is a manual one-time signup step, not something this
  guide can automate).
- A logged-in JWT for a user associated with a `Location` that has ingested `Measurement` data
  spanning at least a full recent hour.
- `$TOKEN` = access token, `$LOCATION_ID` = that location's id.

## Scenarios

**US1 — live current-hour cost**:
```bash
curl -H "Authorization: Bearer $TOKEN" "http://localhost:5231/api/electricity-cost/current?locationId=$LOCATION_ID"
```
Expect `200`, `consumptionKwhSoFar` reflecting only this hour's readings so far. Run it again a
minute later and confirm the value increased (assuming ongoing consumption).

**US2 — historical hourly and daily cost**:
```bash
curl -H "Authorization: Bearer $TOKEN" \
  "http://localhost:5231/api/electricity-cost/hourly?locationId=$LOCATION_ID&from=2026-09-26T00:00:00Z&to=2026-09-27T00:00:00Z"
curl -H "Authorization: Bearer $TOKEN" \
  "http://localhost:5231/api/electricity-cost/daily?locationId=$LOCATION_ID&from=2026-09-26&to=2026-09-26"
```
Expect the daily entry's `consumptionKwh`/`cost` to equal the sum of the 24 hourly entries.

**US3 — minute-level consumption**:
```bash
curl -H "Authorization: Bearer $TOKEN" \
  "http://localhost:5231/api/consumption?locationId=$LOCATION_ID&granularity=minute&from=2026-09-27T09:00:00Z&to=2026-09-27T10:00:00Z"
```
Expect up to 60 entries, one per minute with data.

**US4 — pricing model comparison**:
In any of the above responses, confirm both `actual` and `comparison` are present (or explicitly
`null` if the comparison model's data isn't available), not just the enrolled model's figures.

**FR-011 — graceful unavailability**:
Before the background price/FX fetch has run (or for a `Zone` with no price data yet), confirm
`actual`/`comparison` come back `null` rather than a wrong number, and the rest of the response
still returns `200`.

**Authorization boundary**:
```bash
curl -i -H "Authorization: Bearer $TOKEN" "http://localhost:5231/api/electricity-cost/current?locationId=00000000-0000-0000-0000-000000000000"
```
Expect `404 Location.NotFound`, same as the existing measurement endpoints.

## Done when

All scenarios above return the expected status codes, and the daily-equals-sum-of-hourly
invariant (SC-002) holds.
