# API Contracts: Measurement Query Endpoints

All endpoints require `AuthSchemes(JwtBearerDefaults.AuthenticationScheme)` — these are
user-facing reads, not sensor ingestion (which stays API-Key-only and unchanged). The caller's
`UserId` comes from `ClaimTypes.NameIdentifier`, per the existing handler pattern.

---

## `GET /api/locations`

Lists the locations the authenticated user is associated with, and their registered meters.
Backs User Story 3.

**Response `200 OK`**:
```json
[
  {
    "id": "guid",
    "name": "string",
    "address": "string",
    "zone": "string",
    "meters": [
      { "id": "guid", "deviceId": "string", "meterId": "string|null", "meterType": "string|null", "comment": "string|null", "isActive": true }
    ]
  }
]
```
An empty array (not an error) is returned for a user with no associated locations.

---

## `GET /api/measurements`

Query params: `locationId` (Guid, required), `meterId` (Guid, optional), `from` (ISO-8601 UTC,
optional), `to` (ISO-8601 UTC, optional), `page` (int, optional, default 1), `pageSize` (int,
optional, default 500, max 2000). Backs User Story 1 (no `from`/`to`) and User Story 2 (explicit
range).

**Response `200 OK`**:
```json
{
  "items": [
    { "timestamp": "2026-09-24T10:00:00Z", "meterId": "guid", "powerWatts": 1450 }
  ],
  "page": 1,
  "pageSize": 500,
  "totalCount": 1
}
```
`items` ordered oldest → newest. Empty `items` (not an error) when nothing matches.

**Response `400 Bad Request`** — `Validation.InvalidRange` when `to` < `from`.

**Response `404 Not Found`** — `Location.NotFound` when `locationId` doesn't exist or isn't the
caller's; `Meter.NotFound` when `meterId` doesn't belong to `locationId`.

---

## `GET /api/measurements/latest`

Query params: `locationId` (Guid, required), `meterId` (Guid, optional). Backs FR-009.

**Response `200 OK`**:
```json
{ "timestamp": "2026-09-24T10:00:00Z", "meterId": "guid", "powerWatts": 1450 }
```

**Response `204 No Content`** — no measurement exists yet for this location/meter.

**Response `404 Not Found`** — same `Location.NotFound` / `Meter.NotFound` semantics as above.
