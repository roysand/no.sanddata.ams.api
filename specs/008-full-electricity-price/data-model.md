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
