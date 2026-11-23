# Contract: The Car Charging Cost on the Bill

## `GET /api/electricity-cost/bill` (changed: two fields added to `evCharger`)

Access and every other field are unchanged (spec 010, spec 016).

```json
"evCharger": {
  "kwh": 212.7,
  "estimated": false,
  "included": false,
  "hoursWithoutMeterReading": 0,
  "firstHour": "2026-11-01T06:00:00Z",
  "lastHour": "2026-11-20T21:00:00Z",
  "cost": 106.35,
  "pricedAt": "NorgesPrisPrice"
}
```

- **`cost`:** what the car's kWh so far cost, in NOK including VAT, not rounded. It covers the same hours as `kwh`:
  the hours so far, never projected.
- **`cost` is null** when any of those hours can't be priced: a missing Norgespris rate or VAT, or with
  `HousePrice` a missing spot price, exchange rate or grid rate. The other fields are still set.
- **`pricedAt`:**
  - `NorgesPrisPrice` when `included` is false: each hour's kWh × the Norgespris rate in effect in that hour,
    including VAT (no VAT when the location is exempt). No grid fees, no taxes other than VAT, no markup. This is
    the same price as `norgesPrisPricePerKwh` from `GET /api/electricity-cost/norgespris-status`.
  - `HousePrice` when `included` is true: each hour's kWh at the house's price per kWh in that hour, as the cost
    calculation prices house kWh used after the house's own kWh of the hour. That includes the energy under the
    house's pricing model (spot terms above the Norgespris limit), the markup, the grid energy charge, the taxes and
    VAT, but no capacity charge and no monthly fees.
- **The bill's sums:** `cost` is added to no line, part, `total` or `projection`. Those are the house's, unchanged.
- **No charging in the month:** `evCharger` is null, as before.
- **The setting:** the current setting prices the whole month, past months too, as `included` already does.
