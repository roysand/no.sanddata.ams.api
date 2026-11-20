# Contracts: Reminders Before Keys Expire

- **JSON** is camelCase.
- **Dates** are `"yyyy-MM-dd"`, as Oslo calendar days. **Instants** are UTC (`…Z`).
- **Access:** every endpoint here needs a signed-in administrator. Otherwise `401` (not signed in) or `403` (not an
  administrator), as the other `/api/admin` endpoints answer.

## Changed: PUT /api/admin/locations/{locationId}/water-heater (connect or replace)

The request gains one optional field:

```json
{ "subscriptionKey": "…", "deviceId": "4b8814…", "keyExpiresOn": "2027-11-19" }
```

| Field | Rule |
|---|---|
| `keyExpiresOn` | Optional. `yyyy-MM-dd`; a malformed one gives `400`. It must be after today (Oslo) and at most five years ahead; otherwise `400 WaterHeater.KeyExpiryOutOfRange`. When absent, the date is today + 1 year. |

**The order of the checks:** the range is checked before OSO is called, so a wrong date costs no call to OSO.

**Otherwise unchanged:** every other rule and answer stays as it is, including that the key is checked with OSO first.

## Changed: GET /api/admin/locations/{locationId}/water-heater (settings)

The response gains one field:

```json
{
  "locationId": "…",
  "deviceIdHint": "4b8814…49499d",
  "connectedAt": "2026-11-19T19:32:32Z",
  "updatedAt": "2026-11-19T19:32:32Z",
  "keyExpiresOn": "2027-11-19"
}
```

The connect endpoint and the date endpoint answer with this same shape.

## New: PUT /api/admin/locations/{locationId}/water-heater/key-expiry

It changes only the expiry date. OSO is not called, and the key and the device id stay as they are.

```json
{ "keyExpiresOn": "2027-12-01" }
```

| Answer | When |
|---|---|
| `200` + settings | Changed |
| `400` | The date is missing or malformed, or out of range (`WaterHeater.KeyExpiryOutOfRange`) |
| `404 WaterHeater.NotConnected` | The location has no heater, or does not exist |

## New: GET /api/admin/key-reminders

The keys that expire within 30 days or have expired, for all active locations, soonest first.

```json
{
  "today": "2026-11-20",
  "windowDays": 30,
  "items": [
    {
      "locationId": "…",
      "locationName": "Hytta",
      "kind": "SensorKey",
      "expiresOn": "2026-12-02",
      "expiresAt": "2026-12-02T09:14:03Z",
      "daysLeft": 12,
      "expired": false
    },
    {
      "locationId": "…",
      "locationName": "Hjemme",
      "kind": "WaterHeaterKey",
      "expiresOn": "2026-11-17",
      "expiresAt": null,
      "daysLeft": -3,
      "expired": true
    }
  ]
}
```

| Field | Meaning |
|---|---|
| `today` | The Oslo date the days are counted from |
| `windowDays` | Always 30 in this version |
| `kind` | `SensorKey` (the location's sensor key) or `WaterHeaterKey` (the OSO subscription key) |
| `expiresOn` | The last day the key is valid |
| `expiresAt` | Sensor keys only: the instant the API stops accepting it. null for `WaterHeaterKey`. |
| `daysLeft` | `expiresOn − today`. 0 means it expires today; negative means it has expired. |
| `expired` | `daysLeft < 0` |

`items` is empty when nothing is due.

**Left out:**
- deactivated sensor keys
- inactive locations
- keys with more than 30 days left.

**Never included:** keys, key hashes or device ids.

## Unchanged

- **Sensor key rotation:** `POST /api/admin/locations/{id}/api-key/rotate` is unchanged. A rotated key gets two more
  years, so it leaves the list.
- **Other responses:** the Admin location list (`apiKey.expiresAt`, `status`) and every other endpoint are unchanged.
