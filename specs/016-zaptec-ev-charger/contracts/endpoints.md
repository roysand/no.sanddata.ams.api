# Endpoints: Read the Zaptec EV Charger and Optionally Count It with the House

All endpoints need a JWT (`401` without one). Times are UTC ISO 8601. kWh are unrounded decimals.

## New: admin connection

### `GET /api/admin/ev-chargers`: chargers the server's Zaptec sign-in can see

Admin only. This endpoint stores nothing.

```json
[
  {
    "chargerId": "8b1c…",
    "name": "Garasje",
    "installationId": "e2f0…",
    "installationName": "Hjemme"
  }
]
```

| Status | When |
|---|---|
| 403 | Not Admin |
| 502 | `EvCharger.InvalidResponse` |
| 503 | `EvCharger.NotConfigured`, `EvCharger.SignInRejected` or `EvCharger.Unavailable` |

### `PUT /api/admin/locations/{locationId}/ev-charger`: connect or replace

Admin only. The request body is `{ "chargerId": "8b1c…" }`.

**200 response:** the settings, as in the GET below.

| Status | When |
|---|---|
| 400 | Missing `chargerId`, `EvCharger.UnknownCharger`, or `EvCharger.SignInRejected` |
| 403 | Not Admin |
| 404 | Location not found |
| 409 | `EvCharger.ChargerInUse` |
| 502 / 503 | As above |

Replacing the charger resets the read watermark, so the new charger's history is read back.

### `GET /api/admin/locations/{locationId}/ev-charger`: settings

Admin only.

```json
{
  "locationId": "…",
  "chargerId": "8b1c…",
  "chargerName": "Garasje",
  "installationId": "e2f0…",
  "installationName": "Hjemme",
  "connectedAt": "2026-11-21T10:00:00Z",
  "readUpTo": "2026-11-21T10:15:00Z",
  "includeInTotals": false
}
```

| Status | When |
|---|---|
| 404 | Location not found, or `EvCharger.NotConnected` |

### `DELETE /api/admin/locations/{locationId}/ev-charger`: remove

Admin only. Returns `204`, or `404` if the location or the connection is not found. Stored sessions are kept, and
`includeInTotals` becomes false.

## New: for users of the location

### `PUT /api/locations/{locationId}/ev-charger/include`: the checkbox

Owner of the location or Admin. The request body is `{ "include": true }`.

**200 response:** `{ "locationId": "…", "includeInTotals": true }`.

| Status | When |
|---|---|
| 400 | `include: true` with no charger ever connected (`EvCharger.NotConnected`) |
| 403 | Viewer of the location |
| 404 | Not linked to the location, or no such location |

### `GET /api/locations/{locationId}/ev-charger/sessions?from=…&to=…`: charging sessions

Owner, viewer or Admin. Sessions are filtered by end time.
- **Defaults:** `to` is now and `from` is 30 days before it.
- **Limits:** the range may be at most 366 days, and `from < to`; otherwise `400`.

The answer is newest first:

```json
{
  "items": [
    {
      "start": "2026-11-20T17:02:11Z",
      "end": "2026-11-20T21:40:03Z",
      "kwh": 23.418,
      "estimated": false
    }
  ]
}
```

| Status | When |
|---|---|
| 404 | Not linked to the location, or no such location |

## Changed: added fields only, and no existing field changes meaning

### `GET /api/locations`: each `LocationSummaryResponse`

Adds:
- `"hasEvCharger": bool`: a charger is connected now.
- `"includeEvCharger": bool`: the checkbox.

### `GET /api/electricity-cost/hourly`: each item

Adds:

```json
"totalKwh": 4.612,
"evCharger": { "kwh": 3.200, "estimated": false, "included": true }
```

- **`consumptionKwh`:** still the meter's kWh. Every cost field is computed from it, as before.
- **`totalKwh`:** `consumptionKwh + evCharger.kwh` when `included`, otherwise equal to `consumptionKwh`.
- **`evCharger`:** `null` when the hour has no charging data.

### `GET /api/electricity-cost/current`

Adds `totalKwhSoFar` and `evCharger`, the same as the hourly item. The charger only has data here if a session ended
in the current hour, because Zaptec returns completed sessions only.

### `GET /api/electricity-cost/daily`: each item

Adds:

```json
"totalKwh": 41.20,
"evCharger": { "kwh": 23.418, "estimated": false, "included": true, "hoursWithoutMeterReading": 0 }
```

- **`evCharger.kwh`:** the sum of all the charger's hours in the Oslo day, including hours that have no meter reading.
  `hoursWithoutMeterReading` counts those hours.
- **`totalKwh`:** includes them when `included`.

### `GET /api/electricity-cost/bill`

Adds:

```json
"totalKwh": 812.4,
"evCharger": {
  "kwh": 212.7,
  "estimated": false,
  "included": true,
  "hoursWithoutMeterReading": 0,
  "firstHour": "2026-11-01T06:00:00Z",
  "lastHour": "2026-11-20T21:00:00Z"
}
```

- **Hours counted:** only the hours so far (`soFar`), never projected.
- **What does not change:** every line, subtotal, `total`, the capacity step and `projection`, whatever the setting.
- **`totalKwh`:** the meter's kWh so far, plus `evCharger.kwh` when the charger is included.

## Unchanged

`/api/consumption`, `/api/electricity-cost/saving`, `/capacity`, `/prices`, the measurement endpoints and the water
heater endpoints.
