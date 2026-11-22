# Contract: The Norgespris Limit Status and the Spot Energy Price

## `GET /api/electricity-cost/norgespris-status` (new)

The Norgespris limit status of every active location the caller can see that has a Norgespris agreement. JWT only.
Tag `ElectricityCost`. No parameters.

### 200

```json
{
  "asOf": "2026-11-22T14:37:12Z",
  "items": [
    {
      "locationId": "6a1c…",
      "month": "2026-11",
      "limitKwh": 1000,
      "kwhSoFar": 1043.62,
      "projectedKwh": 1391.8,
      "overLimit": true,
      "overLimitKwh": 43.62,
      "norgesPrisPricePerKwh": 0.5,
      "resetsAt": "2026-11-30T23:00:00Z"
    },
    {
      "locationId": "0f3e…",
      "month": "2026-11",
      "limitKwh": 5000,
      "kwhSoFar": 1820.4,
      "projectedKwh": 2615.07,
      "overLimit": false,
      "overLimitKwh": 0,
      "norgesPrisPricePerKwh": 0.5,
      "resetsAt": "2026-11-30T23:00:00Z"
    }
  ]
}
```

- **Which locations:** those of `GET /api/locations` (the caller's own links as owner or viewer, also for an Admin)
  that are active and have a Norgespris agreement, in that order. None: `"items": []`.
- **`limitKwh`:** the holiday-home or home limit valid for the month, as the cost calculation uses it. Null when no
  limit is set; such a location is never `overLimit`.
- **`kwhSoFar`:** the meter's kWh from 00:00 Oslo time on the 1st up to now, including the running hour's readings.
  When the location counts its car with the house, plus the charger's kWh of the same hours (completed sessions
  only). 0 without readings.
- **`projectedKwh`:** the same figure as the current month's bill: `totalKwh + projection.kwh`. The car's kWh so far
  are in it when counted; the car is never projected. Null when the last 7 days have no readings.
- **`overLimit` / `overLimitKwh`:** `kwhSoFar ≥ limitKwh`, and by how much (0 when not over).
- **`norgesPrisPricePerKwh`:** the Norgespris rate in effect now, including VAT (0 VAT when the location is exempt),
  without grid charge, taxes or supplier markup. Null when the rate or VAT is not set.
- **`resetsAt`:** 00:00 Oslo time on the 1st of next month, in UTC.
- Figures are not rounded.

### 401

No or invalid token.

## `GET /api/electricity-cost/prices` (changed: one field added)

Each item gets `spotEnergyPricePerKwh`:

```json
{ "hourStart": "2026-11-23T16:00:00Z", "ratePerKwh": 2.8113, "spotRatePerKwh": 2.8113, "spotEnergyPricePerKwh": 2.1675 }
```

- The day-ahead price of the location's zone in NOK per kWh (the exchange rate the cost calculation uses), plus VAT
  (none when the location is VAT exempt). Nothing else: no grid charge, taxes or supplier markup.
- The same for every pricing model, under or over the limit.
- Null when the hour's price or exchange rate is missing. Negative when the price is negative.
- `ratePerKwh` and `spotRatePerKwh` are unchanged in meaning.

## Changed behaviour: the car in the Norgespris limit (FR-004)

For a location with "count the car with the house" on (spec 016's `includeEvChargerInTotals`), the charger's kWh of
the month's earlier hours count toward the Norgespris limit in every cost figure:

- `/api/electricity-cost/hourly`, `/daily`, `/current-hour`, `/saving`, `/bill` and `/prices`: the house's kWh pass
  the limit sooner, and the kWh after it are priced on spot terms.
- On the bill, the `NorgesPrisCredit` line gets smaller; the quantities of the kWh lines stay the meter's.
- The car's kWh are still never priced, and with the setting off nothing changes.

This replaces spec 016's rule that "every line, subtotal, `total` … do not change, whatever the setting"
([016 contract](../016-zaptec-ev-charger/contracts/endpoints.md)).
