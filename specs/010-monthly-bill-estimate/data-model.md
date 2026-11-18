# Data Model: Estimated Bill for the Month

## Stored

### Location (changed)

| Field | Type | Rule |
|---|---|---|
| `SupplierMonthlyFee` | `decimal?`, `numeric(9,4)`, nullable | NOK per month excluding VAT. Null = not set (bill line `set: false`, bill and pricing row incomplete). 0 ≤ value ≤ 10,000. Admin-only, set with the other pricing settings; not dated, so a change applies to past months too. |
| `SupplierMarkupPerKwh` | unchanged | Meaning changes: now added on Norgespris terms too (US1). |

`Location.SetPricing(gridTariffId, markup, taxExempt, vatExempt, isHolidayHome, supplierMonthlyFee)`.

Migration `AddSupplierMonthlyFee`: adds the nullable column, no seed. Existing locations start as not set.

## Internal (Application, not persisted)

### PriceBreakdown (changed)

Adds `ElectricityTax`, `EnovaFee` (NOK/kWh excluding VAT, `Taxes = ElectricityTax + EnovaFee`) and `VatRate`
(fraction, 0 when VAT-exempt). The existing identity `Energy + Markup + (Grid ?? 0) + Taxes + Vat = Total` holds.
Not exposed by the existing endpoints.

### CostCalculator (changed)

- `CalculateAsync(..., decimal? fallbackSpotEnergy = null)`: used only when an hour has no spot price row.
- `SpotEnergyAsync(location, hourStartUtc)`: public; the spot energy NOK/kWh excluding VAT, or null.

## Computed (Features/ElectricityCost)

### Projected hour

`(HourStartUtc, Kwh)` for each hour from the start of the current Oslo hour to the end of the month.
`ConsumptionProjection.Project(window, remainingHourStarts)` → projected hours + `BasedOnHours`. Rules: research R6.

### Bill line

| Field | Meaning |
|---|---|
| `Code` | `SupplierMonthlyFee`, `SpotEnergy`, `SupplierMarkup` (Strøm); `GridEnergyDay`, `GridEnergyNight`, `CapacityCharge`, `ElectricityTax`, `EnovaFee`, `NorgesPrisCredit` (Nettleie; credit only for a Norgespris location) |
| `Unit` | `kWh` or `Month` |
| `Quantity` | Σ kWh (so far + projected), or 1 |
| `UnitPrice` | Amount / Quantity including VAT; null when quantity is 0 or the line is not set |
| `VatRate` | VAT on the first Oslo day of the month; 0 when exempt and for the credit |
| `Set` | False when its setting is missing (fee, grid tariff, capacity steps); the amounts are then null |
| `Complete` | False when some hours could not be priced for this line (research R3) |
| `SoFar`, `Projected`, `Amount` | Including VAT; `Amount = SoFar + Projected`. Fixed lines: full amount in `SoFar` |
| `AmountExclVat`, `Vat` | `Amount = AmountExclVat + Vat` |

Signs: the credit is negative when spot is above Norgespris (it lowers the bill), positive in hours where spot is
below.

### Bill part

`Name` (`Strom`, `Nettleie`), `Lines`, and `SoFar`, `Projected`, `AmountExclVat`, `Vat`, `Amount`: sums over the lines
that are set.

### Monthly bill

| Field | Meaning |
|---|---|
| `Month` | `yyyy-MM` |
| `Status` | `Final` (the month has ended) or `Estimate` |
| `PricingModel` | `Spot` or `NorgesPris` |
| `IsComplete` | False when any line is not set or not complete, or `Hours.Priced < Hours.WithReadings` |
| `Hours` | `InMonth`, `Passed`, `WithReadings`, `Priced` |
| `Projection` | Null when final; otherwise `FromUtc`, `Hours`, `Kwh`, `BasedOnHours`, `BasedOnDays`, `AssumedSpotPricePerKwh` (excluding VAT, null when no price in the window) |
| `Capacity` | `Provisional`, `AverageKw`, `Step` (lower/upper kW), or null figures when not set |
| `Strom`, `Nettleie` | Bill parts |
| `Total` | `SoFar`, `Projected`, `AmountExclVat`, `Vat`, `Amount` = Strøm + Nettleie |

### Invariants (tested)

- Each line, part and the total: `Amount = SoFar + Projected = AmountExclVat + Vat`, no rounding.
- `SpotEnergy + NorgesPrisCredit + SupplierMarkup` = Σ (energy + markup) × (1 + VAT) × kWh of the hourly costs (SC-003).
- So-far amounts of the current month = the finished-month calculation of the same hours (SC-004).
- Status `Final` ⇒ `Projection` null and every `Projected` 0; capacity not provisional.
