# API Contracts: Electricity Cost Tracking

All endpoints require `AuthSchemes(JwtBearerDefaults.AuthenticationScheme)` and are scoped to
the caller's own locations (`Location.NotFound` for anything else — see data-model.md).

Every cost response includes both `actual` (the location's enrolled pricing model) and
`comparison` (the other model, nullable if its underlying price/FX data isn't available) —
backing User Story 4.

---

## `GET /api/consumption`

Query params: `locationId` (Guid, required), `meterId` (Guid, optional), `granularity`
("minute" | "hour", required), `from`/`to` (ISO-8601 UTC, optional — default last 24h, same
convention as the existing measurements endpoint). Backs User Story 3.

**Response `200 OK`**:
```json
{
  "granularity": "minute",
  "items": [
    { "periodStart": "2026-09-27T10:00:00Z", "consumptionKwh": 0.0183 }
  ]
}
```

---

## `GET /api/electricity-cost/current`

Query params: `locationId` (Guid, required). Backs User Story 1.

**Response `200 OK`**:
```json
{
  "hourStart": "2026-09-27T10:00:00Z",
  "consumptionKwhSoFar": 0.42,
  "pricingModel": "Spot",
  "actual": { "ratePerKwh": 1.85, "costSoFar": 0.777 },
  "comparison": { "ratePerKwh": 0.40, "costSoFar": 0.168 }
}
```
`actual`/`comparison` are `null` individually when their required price/FX data isn't available
yet (FR-011) — the rest of the response still returns.

---

## `GET /api/electricity-cost/hourly`

Query params: `locationId` (Guid, required), `from`/`to` (ISO-8601 UTC, optional — default last
24h), `page`/`pageSize` (optional, same paging convention as measurements). Backs User Story 2.

**Response `200 OK`**:
```json
{
  "items": [
    {
      "hourStart": "2026-09-27T09:00:00Z",
      "consumptionKwh": 1.12,
      "pricingModel": "Spot",
      "actual": { "ratePerKwh": 1.79, "cost": 2.0048 },
      "comparison": { "ratePerKwh": 0.40, "cost": 0.448 }
    }
  ],
  "page": 1,
  "pageSize": 500,
  "totalCount": 1
}
```

---

## `GET /api/electricity-cost/daily`

Query params: `locationId` (Guid, required), `from`/`to` (date range, optional — default last 7
days). Backs User Story 2. Each entry's `consumptionKwh`/`cost` values are the sum of that day's
hourly figures (SC-002).

**Response `200 OK`**:
```json
{
  "items": [
    {
      "date": "2026-09-27",
      "consumptionKwh": 18.4,
      "pricingModel": "Spot",
      "actual": { "cost": 31.72 },
      "comparison": { "cost": 7.36 }
    }
  ]
}
```
