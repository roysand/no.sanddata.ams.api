# Backlog: The car charger's state right now (frontend spec 017)

Status: the API side is built (spec `specs/021-ev-charger-live`, PR #68, merged and deployed 2026-11-24). Its
production checks (T029 in its `tasks.md`) are still to be run by Roy. What is left is the frontend spec in
`no.sanddata.ams.frontend`. It would be spec 017 there, since 016 is the latest. Remove this note once the frontend's
spec 017 has been specified.

## For the frontend

Full shape, field meanings and status codes:
[`specs/021-ev-charger-live/contracts/endpoints.md`](../021-ev-charger-live/contracts/endpoints.md). The rules behind
each field are in [`research.md`](../021-ev-charger-live/research.md) R2, and in
[`data-model.md`](../021-ev-charger-live/data-model.md), "Rules".

### What Roy chose (API spec 021, 2026-11-24)

- **The Car tab** (`EvChargerPage`) shows the charger's state with its details: no car, connected but not charging,
  charging, finished, offline, and when it was last updated.
- **The Now page** shows a line only while the car is charging: "Car charging now: 4,4 kW · 13,0 kWh this charge ·
  since 16:37".
- **An update every 3 minutes** is enough.

### The state: `GET /api/locations/{locationId}/ev-charger/state`

JWT only. The same people may read it as the session list: an owner, a viewer or an Admin.

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

| `state`     | `online` | `powerKw` | `sessionKwh`               | `pluggedInAt` | `chargingSince` |
| ----------- | -------- | --------- | -------------------------- | ------------- | --------------- |
| `charging`  | true     | ✓         | ✓                          | ✓             | ✓               |
| `connected` | true     | null      | ✓ (0 for a new charge)     | ✓             | null            |
| `finished`  | true     | null      | ✓                          | ✓             | null            |
| `noCar`     | true     | null      | null                       | null          | null            |
| `unknown`   | true     | null      | ✓ if Zaptec sends it       | null          | null            |
| `offline`   | false    | null      | null                       | null          | null            |
| `noCharger` | null     | null      | null                       | null          | null            |

`lastReportedAt` and `readAt` are set in every state except `noCharger`. `lastReportedAt` may be missing when
offline.

- **The Now page line:** show it only with `state === "charging"`. Use `powerKw` (2 decimals; show 1, "4,4 kW"),
  `sessionKwh` (3 decimals; show 1, "13,0 kWh") and `chargingSince` in Oslo time ("since 16:37"). Never show a line
  from any other state.
- **`connected`** means a car is connected but not charging: waiting to charge, or paused by the car or the charger.
- **`chargingSince`** is when charging last started. After a pause it is when charging resumed, not when the car was
  plugged in. That time is `pluggedInAt`.
- **`offline`** means Zaptec says the charger is offline, or the charger hasn't checked in for more than 10 minutes.
  Show "offline" with `lastReportedAt`. No figures are given, so none can look current.
- **`unknown`** is an operation mode the API doesn't know. Show something neutral such as "status unknown", with the
  kWh if there is one.
- **Treat a `state` value the app doesn't know as `unknown`,** in case a new one is added later.
- **"Updated … ago":** `readAt` is when the API read Zaptec, and `lastReportedAt` is the charger's last check-in
  (about every 3 minutes). `useNow` in `src/features/dashboard/hooks.ts` already gives a time that moves on between
  fetches.
- **Times** are UTC with `Z`. Show them in Oslo time.

### How often to ask

- **Ask every 3 minutes** while a page that shows it is open. The API reads Zaptec at most every 3 minutes per charger,
  shares that answer with everyone, and doesn't call Zaptec when nobody asks. Asking more often gets the same
  `readAt`, so it adds no Zaptec calls, but it doesn't add anything either.
- **Don't ask for locations without a charger:** `GET /api/locations` already has `hasEvCharger` on each location.
  If you do ask, the answer is `200` with `noCharger`, and Zaptec isn't called.
- **The pattern to copy** is `useWaterHeaterState` in `src/features/dashboard/hooks.ts`: `retry: false`, and a
  `refetchInterval` that stops on a `404`. Use 3 minutes rather than 1.

### Errors

| Status | Code                                          | Show                                              |
| ------ | --------------------------------------------- | ------------------------------------------------- |
| 401    |                                               | signed out, as elsewhere                          |
| 404    | `Location.NotFound`                           | nothing, and stop asking                          |
| 502    | `EvCharger.InvalidResponse`                   | "unavailable", and try at the next update         |
| 503    | `EvCharger.Unavailable` / `SignInRejected` / `NotConfigured` | "unavailable", and try at the next update |

The API keeps a failure for 1 minute, so asking sooner gets the same answer. On the Now page, show no line when the
state is unavailable.

### What to watch for

- **Display only.** The figures are never stored or added to any usage, cost, bill or Norgespris figure. The finished
  charge shows up in the sessions list and the cost figures within 15 minutes of ending (spec 016), as today. Don't add
  `sessionKwh` to "today" on the Car tab, or the running charge would be counted twice once it ends.
- **The Car tab's hint** "A charge in progress shows here about 15 minutes after it ends." (`EvChargerPage.tsx`) is
  still true for the sessions, but it can now point to the live state above it.
- **A new charge's kWh** should start at 0. The API's rule for this wasn't checked by hand on a fresh plug-in (API
  research R1, "Not read by hand"). If the tab shows the last charge's kWh on a new charge, tell the API side.

## Not covered by the API

- The car's live state is read only. Starting, stopping or limiting a charge would be a later API spec with its own
  safety rules.
- Zaptec's live event feed (Service Bus) isn't used: a read every 3 minutes while someone looks is enough.
