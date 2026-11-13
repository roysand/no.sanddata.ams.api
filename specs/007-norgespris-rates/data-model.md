# Data Model: Norgespris Rates with Start Dates

## Entity: `NorgesPrisRate` (new, Domain)

Derives from `Entity` (`Id`, `CreatedAt`, `UpdatedAt`).

| Field | Type | Rules |
|---|---|---|
| `Id` | `Guid` | Primary key. |
| `ValidFrom` | `DateOnly` | Required. **Unique.** First Oslo calendar day the rate applies to. |
| `RatePerKwh` | `decimal(9,4)` | Required. `>= 0`, at most 4 decimals. NOK per kWh. |
| `TaxPerKwh` | `decimal(9,4)` | Required. `>= 0`, at most 4 decimals. The price used is `RatePerKwh + TaxPerKwh`. |
| `AddedByUserId` | `Guid` | Required. The Admin who added it (`Guid.Empty` for the seeded row). Plain column, no foreign key, so deleting a user never touches rates. |
| `CreatedAt`, `UpdatedAt` | `DateTime` | Set on creation (UTC). |

No end date, no "current" flag, no edit. A rate is valid from `ValidFrom` until the next later `ValidFrom`.

### Table and index

- Table `NorgesPrisRate`, schema `public`.
- Unique index on `ValidFrom` (backs the duplicate-date rule and makes the "latest on or before" lookup an index scan).

### Seed (in the migration `AddNorgesPrisRate`)

| Id | ValidFrom | RatePerKwh | TaxPerKwh | AddedByUserId |
|---|---|---|---|---|
| fixed constant GUID | 2020-01-01 | 0.4000 | 0.0000 | `00000000-...` |

The values are the current defaults in `appsettings.json` (`NorgesPris:RatePerKwh` 0.40, `TaxPerKwh` 0). `Down`
removes the table.

## Derived, not stored

For a rate and "today" (the Oslo calendar date now):

| Status | Meaning |
|---|---|
| `Upcoming` | `ValidFrom` is after today. Deletable. |
| `InEffect` | The row with the latest `ValidFrom` on or before today. Exactly one. |
| `Superseded` | `ValidFrom` is on or before today and a later started rate exists. |

`totalPerKwh = ratePerKwh + taxPerKwh`.

## Rate for an hour

Input: an hour-start instant in UTC. Convert to the Oslo calendar date, then take the rate with the greatest `ValidFrom`
that is `<=` that date. No such row gives "no rate" (the calculator reports the cost as unavailable, never as zero).
Applies identically to a location's actual cost, the comparison cost for a location without the agreement, and the flat
rate shown in the day-prices view, because all three go through `CostCalculator`.

## State transitions (rules at the write side)

| Action | Allowed when | Otherwise |
|---|---|---|
| Add, `ValidFrom` after today | the date is not already used | `409 NorgesPris.DateExists` |
| Add, `ValidFrom` today or earlier | the date is later than every existing `ValidFrom`, and not already used | `409 NorgesPris.NotNewest` (date already used gives `DateExists`) |
| Delete | `ValidFrom` after today | `409 NorgesPris.AlreadyStarted`; unknown id gives `404 NorgesPris.NotFound` |
| Edit | never | no such request exists |

## Removed

`FlatRateOptions` (class, DI registration, `NorgesPris` section in `appsettings.json`). `Location.HasNorgesPriceAgreement`
is unchanged.
