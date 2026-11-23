# Data Model: What the Car Charging Costs, on the Bill

Nothing is stored, and there is no migration.

## EvChargerBillResponse (changed: two fields added)

The bill's `evCharger` part (spec 016). Existing fields are unchanged: `kwh`, `estimated`, `included`,
`hoursWithoutMeterReading`, `firstHour`, `lastHour`.

| Field | Type | Meaning |
|---|---|---|
| `cost` | decimal? | **New.** What the car's kWh so far cost, NOK including VAT, not rounded. Null when any of its hours can't be priced. |
| `pricedAt` | string | **New.** `HousePrice` (counted with the house: the house's price per kWh in each hour) or `NorgesPrisPrice` (its own agreement: the Norgespris price including VAT, energy only). |

Rules:
- The hours priced are exactly the hours summed in `kwh`: the hours so far with charging, including those without a
  house reading. They are never projected.
- `cost` is added to no line, part or total.
- With no charging in the month, `evCharger` is null, as before.

## EvChargerCostResult (internal)

`(decimal? Cost, string PricedAt)` from `EvChargerCost.PriceAsync`, handed to `BillMapper.ToResponse`.

## How an hour is priced

| Setting | Cost of hour *h* |
|---|---|
| Off | car kWh(*h*) × Norgespris rate in effect at *h* × (1 + VAT at *h*); VAT 0 when exempt |
| On | `CalculateAsync(location, h, car kWh(h), house kWh and car kWh of the month before h + house kWh(h)).Actual.Cost` |
