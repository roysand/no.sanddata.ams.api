# Contracts: High Demand on and off

Two new endpoints, Admin only. Nothing else changes: the heater's state (`GET /api/locations/{id}/water-heater`)
already shows High Demand as `specialMode.mode = "Supercharge"` (spec 011).

## `PUT /api/admin/locations/{locationId}/water-heater/high-demand`

Turns High Demand on. OSO keeps it on for 24 hours, or until it's turned off. No body.

## `DELETE /api/admin/locations/{locationId}/water-heater/high-demand`

Turns High Demand off. Turning it off when it's already off isn't an error. No body.

### Both: the answer (200)

```json
{
  "locationId": "6a1c…",
  "highDemand": true,
  "state": {
    "locationId": "6a1c…",
    "readingTime": "2026-11-26T05:21:20Z",
    "isHeating": true,
    "setPointCelsius": 72,
    "specialMode": {
      "mode": "Supercharge",
      "start": "2026-11-26T05:21:13Z",
      "end": "2026-11-27T05:21:13Z",
      "profileName": "SuperchargeMode"
    }
  }
}
```

(Shortened: `state` is the whole state object, the same as `GET /api/locations/{id}/water-heater` returns.)

- `highDemand` is what was asked for: `true` from `PUT`, `false` from `DELETE`.
- `state` is the heater's state read from OSO just after the switch. Use it to update the Heater tab at once. Its
  `specialMode` is the outcome:
  - `"Supercharge"` with `start` and `end` (UTC) when High Demand is on;
  - `"None"` when it's off, or `"Holiday"` or `"BlueBattery"` if one of those is on.
- `state` is `null` when the switch was done but the read-back failed. Read the state again a little later. It is
  not an error.
- OSO may report the old mode for a moment. Show what `state` says, and the next state poll catches up.

### Errors

| Status | Code | When |
|---|---|---|
| 401 | | Not signed in |
| 403 | | Signed in, but not an Admin |
| 404 | `Location.NotFound` | The location doesn't exist |
| 404 | `WaterHeater.NotConnected` | No water heater is connected to the location |
| 502 | `WaterHeater.Rejected` | OSO rejected the stored key or device id; an Admin must reconnect the heater |
| 502 | `WaterHeater.HighDemandNotOffered` | OSO answered "not found" to the switch: it no longer offers High Demand. Hide the switch |
| 502 | `WaterHeater.InvalidResponse` or `WaterHeater.KeyUnreadable` | As for the state |
| 503 | `WaterHeater.Unavailable` | OSO is down, didn't answer within 10 seconds, or is rate-limiting. Whether the heater changed is unknown: read the state |

A failed switch is never retried by the API. The Admin can press again.

## Unchanged

No existing endpoint or field changes. Owners and viewers still only read the mode.
