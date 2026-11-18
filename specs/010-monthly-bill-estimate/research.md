# Research: Estimated Bill for the Month

No `NEEDS CLARIFICATION` was left in the Technical Context; the projection methods were settled in the spec
(2026-11-18). These are the design decisions behind the plan, read from the existing code
(`CostCalculator`, `HourlyCostProvider`, `CapacityCharge`, `SavingCalculator`, `GetCapacityChargeQueryHandler`).

## R1. Markup under Norgespris

- **Decision**: `CostCalculator.TermsAsync` adds `location.SupplierMarkupPerKwh` on both terms. Update the doc
  comments that say "spot terms only" (`Location.SupplierMarkupPerKwh`, `PriceCalculation.Calculate`, the location
  pricing endpoint description) and the tests that expect 0 markup on Norgespris terms.
- **Rationale**: the July invoice charges påslag on Norgespris kWh. The markup is per location, so a supplier without
  it is a 0 on that location.
- **Effect**: the hourly, daily, current-hour and day-price figures of Norgespris locations with a markup rise by
  markup × (1 + VAT) per kWh. The saving per kWh up to the cap becomes (spot − Norgespris) × (1 + VAT); `SavingCalculator`
  needs no change because it subtracts the two sides.
- **Alternatives**: a per-location "markup under Norgespris" flag — rejected (no supplier needs it yet, VIII).

## R2. Where the bill's line parts come from

- **Decision**: add `ElectricityTax`, `EnovaFee` and `VatRate` to the internal `PriceBreakdown` record
  (`Taxes` stays their sum). The API's existing `parts` mapping (`CostMapper`) is unchanged.
- **Rationale**: the invoice has separate lines for elavgift and Energifondet, and each line needs its VAT. Reading them
  from the breakdown the hour was priced with guarantees the bill equals the hourly figures; looking the rates up again
  in a second place could drift.
- **Alternatives**: deriving VAT as `Vat / (Total − Vat)` — fails when the net is 0; a second rate lookup in the bill —
  two calculations of one thing.

## R3. The Norgespris credit and the spot energy line

- **Decision**: per hour of a Norgespris location, with `within`/`above` from `CostResult.Actual.Cap` (all kWh within
  when `Cap` is null) and `spot` = `Comparison.Breakdown` (spot terms for the whole hour):
  - Spot energy line += kWh × spot.Energy, VAT kWh × spot.Energy × VatRate.
  - Credit line −= within × (spot.Energy − norgesPris.Energy) × (1 + VatRate), with VAT rate 0 on the line.
  - Markup, grid, taxes from the Actual breakdown(s), split by `within`/`above`.
  For a spot location there is no credit line and the spot energy line is the Actual energy.
- **Rationale**: spot energy + credit = within × Norgespris × (1 + v) + above × spot × (1 + v), which is exactly the
  energy part of the hourly cost (FR-006, SC-003). The credit carries no VAT, so the bill's VAT matches the invoice's
  (185.43 kr in July).
- **Hour without a spot price on a Norgespris location** (Comparison null, Actual set within the cap): the hour's net
  energy (within × Norgespris × (1 + v)) goes into the spot energy line, the credit gets 0, and both lines are marked
  incomplete with the hour counted as not priced for the split. The total stays right, because on Norgespris it does
  not depend on spot.
- **Hour the calculator cannot price at all** (Actual null): left out of every line; `hours.priced` is lower than
  `hours.withReadings` and the bill is incomplete.

## R4. Unit price, quantity and VAT rate of a line

- **Decision**: kWh lines have quantity = Σ kWh and unitPrice = amount / quantity including VAT (null when quantity is
  0), which is how the invoice shows "155.52 øre" for the spot energy. Monthly lines have quantity 1, unit "Month".
  `vatRate` is the VAT valid on the first Oslo day of the month (0 for a VAT-exempt location, 0 for the credit). Amounts
  themselves use each hour's VAT, so a mid-month VAT change is still priced correctly.
- **Alternatives**: one line per distinct rate when a rate changes mid-month — rejected, the invoice reference does not
  need it.

## R5. "So far" and "projected"

- **Decision**: the boundary is the start of the current Oslo hour. Hours before it are "so far" (priced from
  readings by `HourlyCostProvider`); the current hour and the rest of the month are projected. For a finished month
  every hour is so far and `projection` is null. Fixed monthly lines (fee, capacity) put their full amount in `soFar`
  and 0 in `projected`.
- **Rationale**: the current hour's readings are partial; projecting it whole avoids a half-hour counted as a full one.
  Fixed amounts are known for the month, so they belong to the known part.

## R6. Consumption projection (spec Q1: A)

- **Decision**: window = the 168 hours before the start of the current hour. Profile key = (Oslo weekday, Oslo hour of
  day); value = average kWh of the window's hours with that key (one hour normally, two for the repeated hour on the
  25-hour day). A remaining hour gets its key's value; a missing key (23-hour day, readings gap, new location) gets
  the average kWh per hour of all window hours with readings; no readings at all → 0 kWh. Basis reported as
  `basedOnHours` (window hours with readings, 0–168) and `basedOnDays` = `basedOnHours / 24`.
- **Data**: one extra `GetHourlyAsync` over the window (it can reach into the previous month).
- **Pure service**: `ConsumptionProjection.Project(windowHours, remainingHourStarts)`.

## R7. Projected spot price (spec Q2: A)

- **Decision**: `CostCalculator.CalculateAsync` gets an optional `decimal? fallbackSpotEnergy`, used only when the
  spot price row of the hour is missing. The handler computes it as the plain average of the spot energy (NOK/kWh
  excluding VAT, converted with the exchange rate the calculator would use) over the window hours that have a price,
  via a new public `CostCalculator.SpotEnergyAsync(location, hourStartUtc)` (the existing private method made public).
  No price in the window → no fallback; such projected hours are unpriced on spot terms (incomplete).
- **Preload**: after the so-far pass, `PreloadAsync(location, windowStart, monthEnd)` so the window average and the
  projected hours cost one price query and one exchange-rate query. Tomorrow's published prices are used where the
  fetch job has stored them; a missing exchange rate for tomorrow falls back to the latest one, as today.
- **Norgespris cap**: the projected hours continue the running month kWh from the so-far total, so the projected kWh
  above the cap get spot terms and no credit.

## R8. Capacity charge

- **Decision**: call the existing `CapacityCharge.Calculate` with the so-far hours (the provider's `HourCost` list),
  the month's step table and the VAT on the first day. Provisional until the month ends (US3 scen. 7). Not set →
  line `set: false`, bill incomplete.

## R9. Testing the July 2026 invoice

- **Decision**: a unit test of `MonthlyBillBuilder` builds July 2026 as synthetic priced hours: 95.58 kWh over weekday
  06–22 hours and 144.73 kWh over night and weekend hours, spot energy 1.24416 NOK/kWh (155.52 øre / 1.25) on every hour,
  Norgespris 0.40, markup 0.03192, fee 27.20, grid 0.2899 / 0.1699, tax 0.0713, Enova 0.0100, VAT 0.25, and three days
  with a 6 kWh peak hour (step 3, 336 kr). Expected lines are those of the spec's reference table, within its
  tolerances; total 673.55 ± 0.10.
- **Manual check**: the real readings, spot prices and rates on the production data (quickstart step 6). This needs
  July's data in the database; confirm first.

## R10. Supplier monthly fee setting

- **Decision**: `decimal? SupplierMonthlyFee` on `Location`, `numeric(9,4)`, null = not set. On
  `SetLocationPricingRequest` an optional `supplierMonthlyFee`; omitted keeps the current value (as the flags do).
  Validation: 0 ≤ fee ≤ 10,000. `IsComplete` = grid tariff set and fee set. The fee is logged as a property of
  `LocationPricingChanged` (1706).
- **Not dated**: like the markup, a change applies to every month, past ones included (US4 scen. 5).
- **Alternatives**: a dated `PriceRate` kind — rejected; the fee is per location, not national, and the markup set the
  precedent.

## R11. Refusals and access

- **Decision**: same access as the capacity charge: a user not linked to the location → 404 `Location.NotFound`
  (logged `LocationAccessDenied`). A month whose first Oslo instant is after now → 400 `Bill.MonthNotStarted`. Omitted
  month → current Oslo month.
