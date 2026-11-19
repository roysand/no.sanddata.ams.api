# Contracts: Read the OSO Water Heater

All endpoints use JWT Bearer (`Authorization: Bearer <token>`). Tag in Scalar: `WaterHeater`. JSON is camelCase. All
times are UTC (`…Z`). Error bodies are FastEndpoints' standard error response with the error code below.

## Admin: connect or replace a heater

`PUT /api/admin/locations/{locationId}/water-heater` (Admin)

```json
{ "subscriptionKey": "…", "deviceId": "4b88147ab006…" }
```

Checks the pair with OSO (`GET Status`) before saving. Connecting again replaces the old key and device id.

| Status | When | Code |
|---|---|---|
| `200` | Saved; body: [settings](#settings-response) | |
| `400` | Missing or invalid key or device id (validator) | |
| `400` | OSO rejected the key or device id; nothing saved | `WaterHeater.Rejected` |
| `401` | Not signed in | |
| `403` | Not an Admin | |
| `404` | Location does not exist | `Location.NotFound` |
| `409` | The device is connected to another location | `WaterHeater.DeviceInUse` |
| `502` | OSO answered in an unexpected way; nothing saved | `WaterHeater.InvalidResponse` |
| `503` | OSO unavailable or no answer within 10 s; nothing saved | `WaterHeater.Unavailable` |
| `503` | `WaterHeater:KeyEncryptionKey` not configured on the server | `WaterHeater.EncryptionNotConfigured` |

The request body is never logged (research R3).

## Admin: read the connection

`GET /api/admin/locations/{locationId}/water-heater` (Admin)

| Status | When |
|---|---|
| `200` | [settings](#settings-response) |
| `401` / `403` | Not signed in / not an Admin |
| `404` | Location does not exist (`Location.NotFound`) or no heater connected (`WaterHeater.NotConnected`) |

Does not call OSO.

### Settings response

```json
{
  "locationId": "…",
  "deviceIdHint": "4b8814…49499d",
  "connectedAt": "2026-11-20T10:00:00Z",
  "updatedAt": "2026-11-20T10:00:00Z"
}
```

There is no field for the key.

## Admin: remove the connection

`DELETE /api/admin/locations/{locationId}/water-heater` (Admin)

| Status | When |
|---|---|
| `204` | Removed; the key and device id are deleted, sampling stops, stored samples are kept |
| `401` / `403` | Not signed in / not an Admin |
| `404` | Location does not exist or no heater connected |

## Read the heater's state

`GET /api/locations/{locationId}/water-heater` (owner or viewer of the location, or Admin)

Reads OSO at the time of the request (four calls in parallel, research R2).

```json
{
  "locationId": "…",
  "readingTime": "2026-11-19T18:06:48.703Z",
  "ageSeconds": 103,
  "isStale": false,
  "isOnline": true,
  "isHeating": false,
  "setPointCelsius": 72,
  "temperatures": { "topCelsius": 72.8, "midCelsius": 72.3, "lowCelsius": 70.5, "fourthCelsius": null },
  "hotWater": { "availableLitres40": 344, "maxLitres40": 360, "showers": 3, "baths": 1, "storedEnergyKwh": 13.97741 },
  "powerKw": 0.011774,
  "specialMode": {
    "mode": "Supercharge",
    "start": "2026-11-18T19:40:14.591Z",
    "end": "2026-11-19T19:40:14.591Z",
    "profileName": "SuperchargeMode"
  },
  "osoCodes": { "controlMode": 0, "optimizationOption": 1, "optimizationSubOption": 2, "profileId": 3 }
}
```

- `specialMode.mode` is `None`, `Holiday`, `Supercharge` or `BlueBattery`; `start` and `end` are null for `None` and
  `BlueBattery`.
- A field OSO does not give is `null`, never `0` (`fourthCelsius` when OSO reports 0).
- `osoCodes` are OSO's undocumented numbers, passed on as they are.
- `isStale` is true when `ageSeconds` > 900.

| Status | When | Code |
|---|---|---|
| `200` | State read | |
| `401` | Not signed in | |
| `404` | Location does not exist, is inactive, or the caller is not linked to it (same answer for all) | `Location.NotFound` |
| `404` | No heater connected | `WaterHeater.NotConnected` |
| `502` | OSO rejected the key or device id: "OSO rejected the key or device id; an Admin must reconnect the water heater" | `WaterHeater.Rejected` |
| `502` | OSO answered in an unexpected way | `WaterHeater.InvalidResponse` |
| `502` | The stored key cannot be decrypted (encryption key changed): an Admin must reconnect | `WaterHeater.KeyUnreadable` |
| `503` | OSO unavailable or no answer within 10 s | `WaterHeater.Unavailable` |

`Rejected` is `502` here but `400` on connect: on connect the Admin sent the bad value; here the stored value has gone
bad at OSO's end, which is an upstream failure, not the caller's request.
