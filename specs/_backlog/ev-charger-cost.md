# Backlog: The car charging cost on the bill (frontend spec 016)

Status: the API side is built (spec `specs/019-ev-charger-cost`, PR #62, merged and deployed 2026-11-23, checked in
production by Roy). What is left is the frontend spec in `no.sanddata.ams.frontend`, from its `backlog.md` entry "Car
charging cost". That entry is out of date: see "Correct the frontend backlog" below. Remove this note once the
frontend's spec 016 has been specified.

## For the frontend

Full shape and field meanings:
[`specs/019-ev-charger-cost/contracts/endpoints.md`](../019-ev-charger-cost/contracts/endpoints.md) and
[`data-model.md`](../019-ev-charger-cost/data-model.md). The pricing rules are in
[`research.md`](../019-ev-charger-cost/research.md).

### The two new fields

`GET /api/electricity-cost/bill` → `evCharger` gets two fields. Nothing else in the bill changed.

```json
"evCharger": {
  "kwh": 212.7, "estimated": false, "included": false, "hoursWithoutMeterReading": 0,
  "firstHour": "2026-11-01T06:00:00Z", "lastHour": "2026-11-20T21:00:00Z",
  "cost": 106.35,
  "pricedAt": "NorgesPrisPrice"
}
```

- **`cost`:** what the car's kWh so far cost, in NOK including VAT, not rounded (round to whole kroner or øre in the
  UI). It covers exactly the hours in `kwh`: the hours so far, never projected, so an estimated bill shows the car's
  cost so far.
- **`pricedAt`:** which rule priced it. It follows `included` and is set whenever `evCharger` is.
  - **`NorgesPrisPrice`** (`included: false`, the car on its own agreement): each hour's kWh × the Norgespris price
    including VAT in effect then. Energy only: no grid fees, no taxes other than VAT, no markup. It is the same
    price as `norgesPrisPricePerKwh` in `GET /api/electricity-cost/norgespris-status`.
  - **`HousePrice`** (`included: true`, the car on the house's agreement): each hour's kWh at the house's own price
    per kWh in that hour. That includes the energy under the house's pricing model, the markup, the grid energy
    charge, the taxes and VAT. Car kWh past the house's Norgespris limit are priced at spot terms. It never includes
    the capacity charge or the monthly fees. Within the limit, this equals kWh × the hour's `ratePerKwh` from
    `/prices`.
- **`cost` is null** when any hour can't be priced: a missing Norgespris rate, VAT, spot price, exchange rate or grid
  rate. The rest of the part is still set. Show the kWh without a cost, or a "not priced" hint, never 0 kr.
- **No charging in the month:** `evCharger` is null, as before. There is nothing to show.

### What to watch for

- **Added to no sum.** `cost` is not in any line, in `strom`, `nettleie`, `total` or `projection`. If the app shows
  it "as its own line", that line must sit outside the invoice and its totals, like the car note today. With
  `HousePrice`, the house's bill already includes the effect of the car on the Norgespris limit, not the car's kWh.
- **Say which rule applied.** The note can say "at the house's price" or "at the Norgespris price", next to
  "counted / not counted with the house". If the garages' agreement changes, a new rule may come with a new
  `pricedAt` value, so treat an unknown value as "priced" and show the cost without a rule.
- **Past months** are priced with the setting as it is now, like `included`. Changing the setting re-prices every
  month's car cost.
- **`estimated: true`** means the car's kWh were spread evenly over a session. The cost is priced the same way;
  saying "about" is enough.
- **Where it is shown today:** `EvChargerBillNote` in `src/features/dashboard/BillView.tsx` prints the car's kWh and
  whether they are counted with the house. It is used by the Now page's expected-bill card and the History bill. The
  cost fits on that line: "Car charging so far this month: 212,7 kWh · 106 kr at the Norgespris price · not counted
  with the house".
- **Types:** `EvChargerBillPart` in `src/features/dashboard/types.ts` gets `cost: number | null` and `pricedAt`.
  An app without them reads the bill as before (FR-007).

### Correct the frontend backlog

The entry "Car charging cost" in the frontend's `backlog.md` says the cost is always the car's kWh × the Norgespris
price including VAT. Roy chose differently when API spec 019 was specified on 2026-11-22:
- **Not counted with the house:** the Norgespris price including VAT, as the entry says.
- **Counted with the house:** the house's own price per kWh, so the cost is right once the house is over its limit.

The frontend spec should follow the API and remove or update that entry.

## Out of scope

- The car's cost on the Car tab, the hourly and the daily figures: the API sends it on the bill only (spec 019).
- Projecting the car's cost for the rest of the month: the car is never projected.
