# Research: Full Electricity Price per kWh

All unknowns from the plan are resolved below. Figures for 2026 were checked against Skatteetaten (electricity tax 7.13 ore per kWh, all of 2026), Elvia and Midtnett (Enova fee 1 ore per kWh, 25 percent VAT) and regjeringen.no (Norgespris 0.40 NOK per kWh excluding VAT, cap 5,000 kWh per month per home).

## D1. Where the formula lives

**Decision**: a pure static class `PriceCalculation` in Application computes a `PriceBreakdown` from plain inputs (energy, markup, grid rate or null, tax rates, VAT). `CostCalculator` only gathers the inputs (spot price, rates for the hour, location settings) and calls it.

**Rationale**: SC-001 asks for hand-calculated examples. A pure function with no repository needs no mocks to test, and it is the one place the formula exists, so every consumer gets the same answer (FR-006).

**Alternatives**: the formula inline in `CostCalculator` (needs repository stubs for every formula test); a calculator per cost consumer (the duplication the spec forbids).

## D2. Storage of the dated values

**Decision**: one table `PriceRate` for the three national values (kinds `ElectricityTax`, `EnovaFee`, `Vat`: kind, start date, value) with a unique index on kind and start date; `GridTariff` (name) and `GridTariffRate` (tariff, start date, day rate, night and weekend rate) with a unique index on tariff and start date.

**Rationale**: the three national values behave identically (one number from a start date), so one table with a kind avoids three copies of the table, repository and endpoints (Constitution VIII). A grid tariff has two numbers that always change together, so it is its own table. This is the same dated-row design as `NorgesPrisRate` in spec 007.

**Alternatives**: three tables (triple the code, same behaviour); a single row per kind with `ValidFrom` and `ValidTo` (two columns to keep consistent, overlaps and gaps to prevent); configuration in `appsettings.json` (needs a redeploy and loses history, the problem spec 007 solved).

## D3. Looking up the value for an hour, loaded once

**Decision**: `PriceRateBook` holds all the dated rows (a handful) loaded once and answers "value of kind K for this hour" and "rates of tariff T for this hour" with the latest start date on or before the Oslo date of the hour (the same rule as `NorgesPrisSchedule`). `CostCalculator` is scoped and keeps the book in a lazily loaded field, as it already does for the Norgespris rates.

**Rationale**: FR-009 and FR-017. The rows change only through Admin requests, which are separate requests, so a per-request snapshot is never stale.

**Alternatives**: a query per hour (the N+1 this feature must not add); a singleton cache (needs invalidation across requests for no gain at this size).

## D4. Grid period: day or night

**Decision**: `GridPeriod.For(hourStartUtc)` converts the hour to Oslo local time. It is Day when the local date is Monday to Friday, not a public holiday, and the local hour is 6 to 21 inclusive; otherwise Night (night and weekend). A day with 23 or 25 hours needs no special case because the decision is per hour from its own local time.

**Rationale**: FR-003, FR-004 and the clock-change edge case. This is how Elvia and most grid companies define it (day on weekdays 06-22, night and weekend otherwise, public holidays like weekends).

**Alternatives**: a per-grid-company calendar (not needed; the structure is the same for the grid companies seen); storing holidays as data (they are computable, so a table would only add a maintenance chore).

## D5. Public holidays

**Decision**: `NorwegianHolidays.IsHoliday(DateOnly)` computes the fixed days (1 January, 1 May, 17 May, 25 and 26 December) and the Easter-based days (Maundy Thursday, Good Friday, Easter Monday, Ascension Day, Whit Monday) from Easter Sunday using the standard Gregorian computus (the anonymous Gregorian algorithm). Easter Sunday and Whit Sunday are Sundays and need no entry.

**Rationale**: FR-004 asks for computed holidays. The computus is a dozen lines and is verified against known Easter dates in tests (5 April 2026, 28 March 2027, 16 April 2028).

**Alternatives**: a hand-kept list per year (breaks silently when the list runs out); a library (a new dependency for a dozen lines).

## D6. Result shape and the existing endpoints

**Decision**: `RateCost` gains a `Breakdown` (energy, markup, grid or null, taxes, VAT, total, and whether the grid tariff is set). `RatePerKwh` and `Cost` keep their names and become the totals including VAT, so `CostMapper`, the daily sums and the day prices keep working unchanged. The hourly and current-hour responses get an additive `parts` object; the daily and day-price responses stay totals only.

**Rationale**: the frontend reads `ratePerKwh` and `cost` and must keep working (spec assumption). Additive JSON fields are safe for it. The parts are only useful per hour.

**Alternatives**: new endpoints for the full price (two parallel calculations, the thing FR-006 forbids); parts in the daily response (a sum of parts per day is possible later; not needed now).

## D7. Exact arithmetic

**Decision**: all amounts are `decimal`. VAT is the VAT rate times (energy + markup + grid + taxes) and the total is that sum plus VAT, with no rounding anywhere in the calculation.

**Rationale**: FR-007 says the parts add up to the total with no rounding gap. Decimal arithmetic is exact for these operations, and rounding is for display.

## D8. Missing data

**Decision**: a missing spot price, exchange rate, VAT, electricity tax or Enova fee for the hour makes that price unavailable (`RateCost` is null, as today). A location without a grid tariff, or a tariff with no rate for the hour, leaves the grid part out and sets `gridSet` to false in the breakdown; the total is still given, as the spec asks (story 4).

**Rationale**: FR-013 and FR-014. A missing grid tariff is a setup gap the Admin can see and fix, while a missing tax or VAT is a gap in the national data and must not be hidden. The seed rows cover every date from the first measurement.

## D9. The comparison

**Decision**: `CostCalculator` prices both models with the same grid, tax and VAT inputs. The supplier markup is added on the Spot side only. For a Norgespris location the Actual is Norgespris terms and the Comparison is spot terms, and the other way round for a spot location, as today.

**Rationale**: FR-002 and FR-008. Only the energy part and the markup then differ, which is what the saving shows.

## D10. Performance: preload the price ranges

**Decision**: `CostCalculator.PreloadAsync(location, fromUtc, toUtc)` loads the spot prices of the zone (`GetByRegionAndHourRangeAsync`, which already exists) and the exchange rates of the range (new `GetByDateRangeAsync`, including the days before the range for the weekend fallback) into dictionaries. Per-hour lookups use them when present and fall back to the single-row queries otherwise. `HourlyCostProvider`, the day-prices handler and the saving handler call it before pricing a range.

**Rationale**: today every hour costs one price query plus up to eight exchange-rate queries. A year-to-date saving would be about 8,760 hours and tens of thousands of queries; the preload makes it three queries (SC-006).

**Alternatives**: leave per-hour queries (too slow for the saving); a materialised price view (a larger change than needed).

## D11. Location pricing settings

**Decision**: `Location` gets `GridTariffId` (nullable) and `SupplierMarkupPerKwh` (default 0). They are set with their own Admin endpoint in the `Pricing` slice (`PUT /api/admin/location-pricing/{locationId}`) and read with a list endpoint that marks locations without a tariff as incomplete. The Locations slice and the existing `PUT /api/admin/locations/{id}` body are not changed.

**Rationale**: vertical slices (Constitution II): the Locations slice does not learn about pricing, and the frontend call that edits a location keeps its exact body. Admin-only like the Norgespris agreement flag (FR-012).

## D12. Dated-rate rules

**Decision**: the same rules as spec 007, implemented once inside `Pricing` as `DatedRatePolicy` and used by the price-rate and the grid-tariff-rate handlers: refuse a start date already used by the same kind; a start date of today or earlier must be later than every existing one of the same kind and needs `confirmPastDate: true`; delete only a row whose start date is after today. The Oslo date comes from `TimeProvider` through `NorgesPrisSchedule.OsloDateOf`, which is public in Application.

**Rationale**: FR-010 and FR-011. The Norgespris slice is not touched (Constitution II prefers a little duplication over a shared dependency between slices). Inside one slice, sharing is fine.

## D13. Seed rows

**Decision**: the migration seeds `Vat` 0.2500 from 2020-01-01, `ElectricityTax` 0.0713 from 2026-01-01 and `EnovaFee` 0.0100 from 2026-01-01. It seeds no grid tariff.

**Rationale**: the electricity tax and the Enova fee are the 2026 values and the first measurement is from 2026-08-28, so every stored hour has a value. No grid tariff is seeded because the right tariff depends on the grid company of each location; until an Admin adds one, the grid part is left out and marked not set (story 4).

## D14. The saving

**Decision**: a query `GetNorgesPrisSavingQuery` in the ElectricityCost slice prices the hours from the earlier of the start of the year and six days before today, through now, once (one `HourlyCostProvider` call after a preload), and for each of the four periods sums, per hour, the spot-terms cost minus the Norgespris-terms cost. An hour where either side is unavailable is left out and counted separately.

**Rationale**: FR-018 to FR-020. One pass over the longest range serves all four periods and guarantees that shorter periods add up to longer ones (SC-007).

## D15. Out of scope here

Tariff delete or rename, VAT or tax exemptions by area, the supplier monthly fee, the grid fixed fee and capacity charge, the Norgespris monthly cap, and every web app change. The Admin screens for this data belong to the UI feature in `specs/_backlog`.

## D16. The tax field of a Norgespris rate

**Decision**: the energy part on Norgespris terms is the `RatePerKwh` of the Norgespris row valid for the hour. The `TaxPerKwh` column of spec 007 is no longer used; it stays in the table for history and should be left at 0.

**Rationale**: spec 007 added `RatePerKwh + TaxPerKwh` as the whole flat price when no other taxes were modelled. Now the electricity tax and the Enova fee are added separately as dated rows, so summing the old field as well would count taxes twice. The Norgespris price of 0.40 NOK per kWh excluding VAT is the energy part; grid rent and taxes come on top. Decided by Roy on 2026-11-16.

**Alternatives**: keep summing and require the field to stay 0 (a trap for the next Admin entry); remove the column (a migration and a frontend change for no gain, and the Norgespris Admin page in the frontend still sends and shows it).

## D17. Enum values in JSON

**Decision**: `PriceRateKind` is exposed as a string in every request and response record of the Pricing slice and parsed (case-insensitive) in the validator, as the existing `Role` of the link endpoint is.

**Rationale**: the API does not configure a JSON string-enum converter, so an enum property would be written and read as a number. Adding a global converter would change every other endpoint.
