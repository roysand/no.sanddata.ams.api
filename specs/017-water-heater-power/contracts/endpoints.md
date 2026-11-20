# Contract: The Water Heater's Power per 5 Minutes

## `GET /api/locations/{locationId}/water-heater/power`

The heater's average power per 5 minutes in a time window, from the stored power samples. JWT only. Tag `WaterHeater`.

### Query

| Name | Type | Left out | Rule |
|---|---|---|---|
| `from` | ISO 8601 date-time | `to` − 3 hours | Before `to`, at most 48 hours before it, not after now |
| `to` | ISO 8601 date-time | now | May be in the future; nothing is counted past now |

A time with an offset is converted to UTC. Send whole 5 minutes: a `from` of 10:02 gives a first step starting at
10:00 that covers 10:02–10:05 only.

### 200

```json
{
  "locationId": "6a1c…",
  "from": "2026-11-20T09:00:00Z",
  "to": "2026-11-20T12:00:00Z",
  "stepSeconds": 300,
  "items": [
    { "start": "2026-11-20T09:00:00Z", "powerKw": 0.012, "sampledSeconds": 300 },
    { "start": "2026-11-20T11:00:00Z", "powerKw": 2.031, "sampledSeconds": 300 },
    { "start": "2026-11-20T11:55:00Z", "powerKw": 2.018, "sampledSeconds": 134 }
  ]
}
```

- `from` and `to` are the window actually used, in UTC, so the client can see the defaults.
- `items` is ordered by `start`. It has one item per 5 minutes with any sampled time.
- `start` is on whole 5 minutes of the clock, which are the same in UTC and Oslo time.
- A step without samples has no item. Draw a gap, never 0 kW.
- `powerKw` is the average over the sampled time only, not over the whole 5 minutes.
- `sampledSeconds` (1–300) says how much of the step was covered. The step going on now covers only up to now.
- No samples in the window (no heater, before the heater was connected, a long outage) gives `"items": []`.
- The heater's power is not capped at the meter's here. The hourly cost figures (spec 012) still cap it.

### Errors

| Status | When |
|---|---|
| 400 | `from` is not before `to`, the window is longer than 48 hours, or `from` is in the future |
| 401 | Not signed in |
| 404 `Location.NotFound` | The location does not exist, or the caller is not its owner, a viewer or an Admin. An inactive location answers only to Admins |

No connected heater is needed. Samples belong to the location, so a removed heater's past windows still answer.

## Unchanged

No existing endpoint or field changes (FR-006). This includes the hourly, daily, current-hour and bill responses of
spec 012, whose heater kWh now comes from the same query with a 1-hour step.
