# Contracts: The Water Heater's kWh and Cost, Split from the Rest

No new endpoint. Three existing responses gain one field, `waterHeater`, and no existing field changes (FR-008). JSON
is camelCase, times are UTC (`…Z`), and amounts are NOK including VAT, unrounded, like the other cost fields. Access
and errors are unchanged: owner, viewer or Admin of the location, and `404 Location.NotFound` otherwise.

`waterHeater` is `null` when the hour, day or month has no heater samples: no heater at the location, before it was
connected, or a sampling outage that covers the whole period. It is never an object of zeros.

## GET /api/electricity-cost/hourly

Each item in `items` gains:

```json
{
  "hourStart": "2026-11-20T05:00:00Z",
  "consumptionKwh": 3.412,
  "pricingModel": "NorgesPris",
  "actual": { "ratePerKwh": 0.9134, "cost": 3.1166, "parts": { "…": "…" }, "cap": null },
  "comparison": { "…": "…" },
  "waterHeater": { "kwh": 2.104, "cost": 1.9218, "sampledMinutes": 60, "capped": false }
}
```

| Field | Meaning |
|---|---|
| `kwh` | The heater's kWh in the hour, at most `consumptionKwh` |
| `cost` | `actual.cost × kwh / consumptionKwh`; `null` when `actual` is `null` |
| `sampledMinutes` | 0–60; less than 60 means part of the hour wasn't sampled (or the hour is still going on) |
| `capped` | True when the samples added up to more than the meter measured, so `kwh` was cut to `consumptionKwh` |

The rest of the hour = `consumptionKwh − kwh` and `actual.cost − cost`. Clients compute it themselves.

## GET /api/electricity-cost/daily

Each item gains the sums of its hours:

```json
{
  "date": "2026-11-20",
  "consumptionKwh": 61.87,
  "pricingModel": "NorgesPris",
  "actual": { "cost": 55.21 },
  "comparison": { "cost": 71.04 },
  "waterHeater": { "kwh": 9.41, "cost": 8.47, "sampledMinutes": 1431, "capped": false }
}
```

| Field | Meaning |
|---|---|
| `kwh` | Σ the hours' `kwh` |
| `cost` | Σ the hours' `cost`; `null` if any hour with a heater part has `cost: null` (the same rule as `actual`) |
| `sampledMinutes` | Σ the hours' minutes (1440 for a full day; 1380 / 1500 on clock-change days) |
| `capped` | Any hour capped |

## GET /api/electricity-cost/bill

The response gains, at the top level:

```json
"waterHeater": {
  "kwh": 104.6,
  "cost": 94.12,
  "sampledMinutes": 15180,
  "capped": false,
  "firstHourUtc": "2026-11-19T18:00:00Z",
  "lastHourUtc": "2026-11-30T22:00:00Z",
  "estimate": false
}
```

| Field | Meaning |
|---|---|
| `kwh`, `cost`, `sampledMinutes`, `capped` | As for a day, over the month's hours so far (`hours.passed`), or the whole month once it has ended |
| `firstHourUtc`, `lastHourUtc` | The first and last hour that has a heater part |
| `estimate` | Always `false`: this part is never projected, even when the bill's `status` is `Estimate` |

The heater's cost is part of the energy cost (power, grid energy part, taxes and VAT). It isn't a bill line, and no
amount in `strom`, `nettleie` or `total` changes.

## Unchanged

`/api/electricity-cost/current`, `/consumption`, `/prices`, `/capacity` and `/saving` are not changed (research R5,
R6).
