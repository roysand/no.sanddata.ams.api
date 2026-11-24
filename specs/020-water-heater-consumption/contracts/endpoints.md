# Contract: Hot Water Used per Hour, Day and Month

## `GET /api/locations/{locationId}/water-heater/hot-water`

The litres of 40 °C water (V40) used per part of a period, counted from the stored heater samples, with the period's
total. JWT only. Tag `WaterHeater`.

### Query

| Name | Type | Left out | Rule |
|---|---|---|---|
| `period` | `day` \| `week` \| `month` \| `year` (any case) | `day` | Anything else gives a 400 |
| `date` | `yyyy-MM-dd`, an Oslo calendar date | today in Oslo | Must be a real date. The period is the one that holds it |

What each period covers, and its parts:

| `period` | Covers (Oslo time) | One item per |
|---|---|---|
| `day` | `date` 00:00 to the next 00:00 | hour: 24, or 23 and 25 on the days the clocks change |
| `week` | Monday 00:00 of `date`'s ISO week to the next Monday | Oslo day (7) |
| `month` | the 1st of `date`'s month to the 1st of the next | Oslo day (28–31) |
| `year` | 1 January of `date`'s year to the next 1 January | calendar month (12) |

### 200

```json
{
  "locationId": "6a1c…",
  "period": "day",
  "from": "2026-11-20T23:00:00Z",
  "to": "2026-11-21T23:00:00Z",
  "totalLitres": 317,
  "sampledMinutes": 1431,
  "items": [
    { "start": "2026-11-20T23:00:00Z", "litres": 0, "sampledMinutes": 60 },
    { "start": "2026-11-21T07:00:00Z", "litres": 38, "sampledMinutes": 60 },
    { "start": "2026-11-21T12:00:00Z", "litres": null, "sampledMinutes": 0 },
    { "start": "2026-11-21T17:00:00Z", "litres": 100, "sampledMinutes": 59 }
  ]
}
```

(The example is cut short. A full day has one item per hour.)

**The period:**
- `from` and `to` are the whole period in UTC, also when `to` is after now, so the client can label the axis.
- `start` is in UTC. Show it in Oslo time: `2026-11-20T23:00:00Z` is 00:00 on 21 November.

**The items:**
- `items` has one item per part that starts before now, in time order. A part after now has no item, and the part
  going on now counts up to now.
- `litres` is the whole litres of 40 °C water used in the part. The heating done while water was drawn is counted back
  in (the power correction).
- `litres` is `null` when the part has no sampled time: draw a gap, never 0. `0` means it was sampled and no water was
  used.
- `sampledMinutes` says how much of the part was sampled (0–60 for an hour, up to 1500 for a day, and so on). A part
  with only some sampled time counts that time only.

**The total:**
- `totalLitres` is the sum of the items' `litres`, so the bars add up to it.
- It is `null` when no item has litres: before the litres were first stored, with no heater, or for a period wholly in
  the future (`"items": []`).
- `sampledMinutes` at the top is the sum of the items' `sampledMinutes`.

### Errors

| Status | When |
|---|---|
| 400 | `period` is not one of the four, or `date` is not a real `yyyy-MM-dd` date |
| 401 | Not signed in |
| 404 `Location.NotFound` | The location does not exist, or the caller is not its owner, a viewer or an Admin. An inactive location answers only to Admins |

No connected heater is needed. The samples belong to the location, so a removed heater's past periods still answer.

## Unchanged

No existing endpoint or field changes (FR-010). The water heater's state, its power (spec 017) and its kWh and cost
(spec 012) answer exactly as before.
