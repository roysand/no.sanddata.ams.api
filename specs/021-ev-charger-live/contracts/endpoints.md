# Endpoints: The Car Charger's State Right Now

**Spec**: [spec.md](../spec.md) | **Data model**: [data-model.md](../data-model.md)

## New

### `GET /api/locations/{locationId}/ev-charger/state`: the charger's state right now

- **Who:** an owner, a viewer or an Admin, with JWT only. The rule is the same as for
  `GET /api/locations/{locationId}/ev-charger/sessions`.
- **How often Zaptec is read:** at most once every 3 minutes per charger. Everyone who asks in that time shares the
  answer, so the app can ask every minute without adding calls to Zaptec.
- **`readAt`** is when Zaptec was read, and **`lastReportedAt`** is the charger's last check-in. The app can show
  "updated 2 min ago" from either.

**Charging** (`200`):

```json
{
  "state": "charging",
  "online": true,
  "powerKw": 4.41,
  "sessionKwh": 12.962,
  "pluggedInAt": "2026-11-24T14:52:03Z",
  "chargingSince": "2026-11-24T15:37:41Z",
  "lastReportedAt": "2026-11-24T18:33:10Z",
  "readAt": "2026-11-24T18:34:02Z"
}
```

| `state`      | `online` | `powerKw` | `sessionKwh` | `pluggedInAt` | `chargingSince` | `lastReportedAt` | `readAt` |
| ------------ | -------- | --------- | ------------ | ------------- | --------------- | ---------------- | -------- |
| `charging`   | true     | ✓         | ✓            | ✓             | ✓               | ✓                | ✓        |
| `connected`  | true     | null      | ✓ (0 for a new charge; null if Zaptec sent none) | ✓ | null            | ✓                | ✓        |
| `finished`   | true     | null      | ✓            | ✓             | null            | ✓                | ✓        |
| `noCar`      | true     | null      | null         | null          | null            | ✓                | ✓        |
| `unknown`    | true     | null      | ✓ if sent    | null          | null            | ✓                | ✓        |
| `offline`    | false    | null      | null         | null          | null            | ✓ if any         | ✓        |
| `noCharger`  | null     | null      | null         | null          | null            | null             | null     |

- **`connected`** means a car is connected but not charging: waiting to charge, or paused by the car or the charger.
- **`chargingSince`** is when charging last started. After a pause it is when charging resumed.
- **`offline`** means Zaptec says the charger is offline, or the charger hasn't checked in for more than 10 minutes.
- **`noCharger`**: no charger is connected to the location. Zaptec isn't called.
- **Rounding:** power in kW to 2 decimals, kWh to 3. Times are UTC with `Z`.
- **What these figures are for:** showing what's happening now. They are never stored, and never added to any usage,
  cost, bill or Norgespris figure. The finished charge shows up in those within 15 minutes of ending (spec 016).

| Status | Code                        | When                                                                     |
| ------ | --------------------------- | ------------------------------------------------------------------------ |
| 200    |                             | The state, including `noCharger`                                         |
| 401    |                             | Not signed in                                                            |
| 404    | `Location.NotFound`         | Not linked to the location, or no such location                          |
| 502    | `EvCharger.InvalidResponse` | Zaptec answered in a way the API can't read. Held for 1 minute.          |
| 503    | `EvCharger.Unavailable`     | Zaptec failed, rate-limited, or took more than 3 s. Held for 1 minute.   |
| 503    | `EvCharger.SignInRejected`  | Zaptec rejected the server's login. Held for 1 minute.                   |
| 503    | `EvCharger.NotConfigured`   | The server has no Zaptec login                                           |

On a `502` or `503` the app should show the state as unavailable and ask again on its next update. Asking sooner
gets the same answer for up to a minute.

## Unchanged

All of spec 016's endpoints, and every usage, cost, bill and Norgespris response.
