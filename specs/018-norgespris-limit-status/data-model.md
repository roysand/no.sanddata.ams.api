# Data Model: The Norgespris Limit and the Spot Energy Price for the App

Nothing is stored, and there is no migration. Every figure is derived from existing tables: hourly consumption,
charging session hours, price rates (caps and VAT), Norgespris rates, spot prices and exchange rates.

## NorgesPrisStatus (response, one per listed location)

| Field | Type | Meaning | Source |
|---|---|---|---|
| `locationId` | Guid | The location | `Location.Id` |
| `month` | string `yyyy-MM` | The current Oslo month | `CostTime` (R7) |
| `limitKwh` | decimal? | The month's Norgespris limit; null when no cap is set | `NorgesPrisCapHolidayHome` / `NorgesPrisCapHome` on the 1st (R3) |
| `kwhSoFar` | decimal | Meter kWh from the month start to now, plus the car's when counted with the house | R4 |
| `projectedKwh` | decimal? | The bill's projected total for the month; null without readings in the last 7 days | R5 |
| `overLimit` | bool | `limitKwh` set and `kwhSoFar ≥ limitKwh` | R8 |
| `overLimitKwh` | decimal | `kwhSoFar − limitKwh` when over, else 0 | R8 |
| `norgesPrisPricePerKwh` | decimal? | The Norgespris rate in effect now × (1 + VAT); null when missing | R6 |
| `resetsAt` | DateTime (UTC) | 00:00 Oslo time on the 1st of next month | R7 |

Rules:
- Listed only for active locations the user is linked to (owner or viewer) that have a Norgespris agreement (R2).
- Values are not rounded.
- `projectedKwh` ≥ the kWh before the current hour; it can be below `kwhSoFar` early in an hour (R5).

## NorgesPrisStatusResponse (response)

| Field | Type | Meaning |
|---|---|---|
| `asOf` | DateTime (UTC) | The instant the figures were computed at ("now") |
| `items` | NorgesPrisStatus[] | One per listed location, in the order of `GET /api/locations`; empty when none |

## HourPriceResponse (changed: one field added)

| Field | Type | Meaning |
|---|---|---|
| `hourStart` | DateTime (UTC) | Unchanged |
| `ratePerKwh` | decimal? | Unchanged |
| `spotRatePerKwh` | decimal? | Unchanged |
| `spotEnergyPricePerKwh` | decimal? | **New.** Day-ahead price in NOK per kWh × (1 + VAT of the hour), VAT 0 when the location is exempt; nothing else. Null when the price or exchange rate is missing. Can be negative (R10). |

## Running month kWh for the cap (changed rule, FR-004)

`monthKwhBefore(h)` for an hour *h* of an Oslo month *M*:

- the meter kWh of the hours of *M* before *h*, as today;
- **plus**, when `Location.IncludeEvChargerInTotals` is on, the charger kWh of the hours of *M* before *h* (R9).

The cap splits only the meter kWh of *h*. The car's kWh are never priced.
