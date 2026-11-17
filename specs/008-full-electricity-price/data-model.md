# Data Model: Full Electricity Price per kWh

## Entities (Domain, all derive from `Entity`: `Id`, `CreatedAt`, `UpdatedAt`)

### `PriceRate` (new): a national dated value

| Field | Type | Rules |
|---|---|---|
| `Kind` | `PriceRateKind` enum stored as text | `ElectricityTax`, `EnovaFee`, `Vat`. Required. |
| `ValidFrom` | `DateOnly` | Required. First Oslo calendar day the value applies to. |
| `Value` | `decimal(9,4)` | Required, `>= 0`. NOK per kWh for the two fees (at most 4 decimals, `<= 100`); a fraction for `Vat` (0.2500 = 25 percent, `<= 1`). |
| `AddedByUserId` | `Guid` | Required, no foreign key. `Guid.Empty` for seeded rows. |

Unique index on (`Kind`, `ValidFrom`).

### `GridTariff` (new)

| Field | Type | Rules |
|---|---|---|
| `Name` | `string(100)` | Required, unique (case-insensitive check in the handler). Example: "Elvia standard, household". |

### `GridTariffRate` (new): a dated rate of a tariff

| Field | Type | Rules |
|---|---|---|
| `GridTariffId` | `Guid` | Required, foreign key to `GridTariff`, delete restricted. |
| `ValidFrom` | `DateOnly` | Required. |
| `DayRatePerKwh` | `decimal(9,4)` | Required, `>= 0`, `<= 100`. NOK per kWh excluding taxes and VAT. |
| `NightRatePerKwh` | `decimal(9,4)` | Required, `>= 0`, `<= 100`. The night and weekend rate. |
| `AddedByUserId` | `Guid` | Required, no foreign key. |

Unique index on (`GridTariffId`, `ValidFrom`).

### `Location` (changed)

| Field | Type | Rules |
|---|---|---|
| `GridTariffId` | `Guid?` | Foreign key to `GridTariff`, delete restricted. Null means no tariff set. |
| `SupplierMarkupPerKwh` | `decimal(9,4)` | Required, default 0, `>= 0`, `<= 100`. NOK per kWh excluding VAT, added on spot terms only. |

`Location.SetPricing(gridTariffId, supplierMarkupPerKwh)` changes both. Changing them recalculates past hours, like the Norgespris agreement flag.

## Seed rows (migration `AddFullElectricityPrice`)

| Kind | ValidFrom | Value |
|---|---|---|
| `Vat` | 2020-01-01 | 0.2500 |
| `ElectricityTax` | 2026-01-01 | 0.0713 |
| `EnovaFee` | 2026-01-01 | 0.0100 |

No grid tariff is seeded. Seed ids are fixed constants, `AddedByUserId` is `Guid.Empty`.

## Derived, not stored

For any dated row and "today" (the Oslo date now), the status is `Upcoming` (start date after today), `InEffect` (the row with the latest start date on or before today, per kind or per tariff) or `Superseded`.

## Pricing one hour (`PriceCalculation`)

Inputs: energy (NOK per kWh excluding VAT), markup, grid rate (or null when not set), electricity tax, Enova fee, VAT fraction, consumption in kWh.

```
taxes   = electricityTax + enovaFee
net     = energy + markup + (grid ?? 0) + taxes
vat     = vatFraction * net
total   = net + vat                     # NOK per kWh including VAT
cost    = consumptionKwh * total
```

`PriceBreakdown(Energy, Markup, Grid?, Taxes, Vat, Total, GridSet)`, so `Energy + Markup + (Grid ?? 0) + Taxes + Vat == Total` exactly.

- **Energy**: the Norgespris `RatePerKwh` valid for the hour (spec 007; its `TaxPerKwh` is not used, because taxes are added separately) on Norgespris terms; spot price x EURNOK on spot terms.
- **Markup**: `Location.SupplierMarkupPerKwh` on spot terms; 0 on Norgespris terms.
- **Grid**: the day or night rate of the location tariff valid on the Oslo date of the hour, chosen by `GridPeriod`.

## Grid period (`GridPeriod.For(hourStartUtc)`)

Convert the hour start to Oslo local time. `Day` when the local weekday is Monday to Friday, the local date is not a public holiday, and the local hour is 6 to 21; `Night` otherwise.

## Public holidays (`NorwegianHolidays`)

Fixed: 1 January, 1 May, 17 May, 25 December, 26 December. From Easter Sunday E: Maundy Thursday E-3, Good Friday E-2, Easter Monday E+1, Ascension Day E+39, Whit Monday E+50.

## Write-side rules (Pricing slice)

| Action | Allowed when | Otherwise |
|---|---|---|
| Add a `PriceRate` or `GridTariffRate`, future start date | the date is not used by the same kind or tariff | `409 Pricing.DateExists` |
| Add, start date today or earlier | later than every existing one of the same kind or tariff, and `confirmPastDate` is true | `409 Pricing.NotNewest` / `409 Pricing.ConfirmationRequired` |
| Delete a rate | its start date is after today | `409 Pricing.AlreadyStarted`; unknown id `404 Pricing.NotFound` |
| Create a grid tariff | the name is not used | `409 Pricing.TariffNameExists` |
| Set location pricing | the location exists, the tariff (if given) exists | `404 Pricing.LocationNotFound` / `404 Pricing.TariffNotFound` |
| Edit a rate or delete a tariff | never in this feature | no such request exists |

## Saving (computed, not stored)

For a period and a location: `saving = sum over hours of (spot-terms cost - Norgespris-terms cost)`, over the hours where both sides can be priced; `hoursCounted` and `hoursWithConsumption` are returned with it. Periods are Oslo calendar days: Today, Last7Days (today and the six days before), MonthToDate, YearToDate.

## Addendum 2026-11-17: user stories 6 and 7

### `Location` (changed again)

| Field | Type | Rules |
|---|---|---|
| `ElectricityTaxExempt` | `bool` | Required, default false. Electricity tax is 0 for the location; the Enova fee still applies. |
| `VatExempt` | `bool` | Required, default false. VAT is 0 for the hourly price and the capacity charge. |
| `IsHolidayHome` | `bool` | Required, default false. Picks the holiday-home Norgespris cap. |

`Location.SetPricing(gridTariffId, supplierMarkupPerKwh, electricityTaxExempt, vatExempt, isHolidayHome)`. Changing
any of them recalculates past hours.

### `PriceRateKind` (changed)

New kinds `NorgesPrisCapHome` and `NorgesPrisCapHolidayHome`; `Value` is kWh per month, `> 0`, `<= 99999`, at most 4
decimals. Seed: `NorgesPrisCapHome` 5000 and `NorgesPrisCapHolidayHome` 1000, both from 2025-10-01.

### `GridCapacityStep` (new, phase 2)

| Field | Type | Rules |
|---|---|---|
| `GridTariffId` | `Guid` | Required, foreign key to `GridTariff`, delete restricted. |
| `ValidFrom` | `DateOnly` | Required. All steps of one table share it. |
| `LowerKw` | `decimal(9,4)` | Required, `>= 0`. The first step of a table starts at 0. |
| `UpperKw` | `decimal(9,4)?` | Null only for the top step; otherwise `> LowerKw` and equal to the next step's `LowerKw`. |
| `MonthlyAmount` | `decimal(9,4)` | Required, `>= 0`. NOK per month excluding VAT. |
| `AddedByUserId` | `Guid` | Required, no foreign key. |

Unique index on (`GridTariffId`, `ValidFrom`, `LowerKw`). A table is identified by (`GridTariffId`, `ValidFrom`) and is
added and deleted as a whole. No seed rows.

## Cap split (`NorgesPrisCap.Split`)

```
cap          = book.ValueOn(IsHolidayHome ? NorgesPrisCapHolidayHome : NorgesPrisCapHome, first Oslo day of the month)
room         = max(0, cap - monthKwhBefore)
withinCap    = min(hourKwh, room)
aboveCap     = hourKwh - withinCap
```

On Norgespris terms: `cost = withinCap x total(Norgespris terms) + aboveCap x total(spot terms with markup)`. No cap
value for the month means no cap (all kWh within); the seed covers every month since Norgespris started.
`RateCost.Cap = CapSplit(KwhWithinCap, KwhAboveCap, AboveCapBreakdown)`, null when nothing is above the cap.

## Exemptions in `CostCalculator`

```
electricityTax = location.ElectricityTaxExempt ? 0 : book.ValueFor(ElectricityTax, hour)
vat            = location.VatExempt           ? 0 : book.ValueFor(Vat, hour)
```

An exempt location does not need the value it is exempt from, so only the values it uses must exist for the hour to be
priced.

## Capacity charge (`CapacityCharge.Calculate`, computed, not stored)

```
peaks    = for each Oslo date: the hour with the highest kWh (kW = kWh of a one-hour bucket)
top      = the 3 highest peaks (all of them when fewer than 3 days have data)
average  = sum(top) / count(top)
step     = the step of the table valid on the first Oslo day of the month with LowerKw <= average < UpperKw
           (top step: LowerKw <= average)
amount   = step.MonthlyAmount x (1 + VAT on the first day of the month, 0 when VatExempt)
```

Result: `Month`, `Provisional` (month not ended), `Peaks[] (date, hourStartUtc, kw)`, `AverageKw`, `Step (lowerKw,
upperKw)`, `AmountExclVat`, `Vat`, `Amount`, `KwToNextStep` (null on the top step), `Set` (false when the location has
no tariff, the tariff has no step table for the month, or no consumption).

### Write-side rules (capacity steps)

| Action | Allowed when | Otherwise |
|---|---|---|
| Add a step table | the steps start at 0, are contiguous and increasing, and only the last has no upper bound | `400` |
| Add, future start date | no table of the tariff has that date | `409 Pricing.DateExists` |
| Add, start date today or earlier | later than every table of the tariff and `confirmPastDate` | `409 Pricing.NotNewest` / `409 Pricing.ConfirmationRequired` |
| Delete a table | its start date is after today | `409 Pricing.AlreadyStarted`; unknown `404 Pricing.NotFound` |
