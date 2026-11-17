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

Tariff delete or rename, the supplier monthly fee, and every web app change. (Originally also the exemptions, the Norgespris cap and the capacity charge; these moved into stories 6 and 7, see D18-D25.) The Admin screens for this data belong to the UI feature in `specs/_backlog`.

## D16. The tax field of a Norgespris rate

**Decision**: the energy part on Norgespris terms is the `RatePerKwh` of the Norgespris row valid for the hour. The `TaxPerKwh` column of spec 007 is no longer used; it stays in the table for history and should be left at 0.

**Rationale**: spec 007 added `RatePerKwh + TaxPerKwh` as the whole flat price when no other taxes were modelled. Now the electricity tax and the Enova fee are added separately as dated rows, so summing the old field as well would count taxes twice. The Norgespris price of 0.40 NOK per kWh excluding VAT is the energy part; grid rent and taxes come on top. Decided by Roy on 2026-11-16.

**Alternatives**: keep summing and require the field to stay 0 (a trap for the next Admin entry); remove the column (a migration and a frontend change for no gain, and the Norgespris Admin page in the frontend still sends and shows it).

## D17. Enum values in JSON

**Decision**: `PriceRateKind` is exposed as a string in every request and response record of the Pricing slice and parsed (case-insensitive) in the validator, as the existing `Role` of the link endpoint is.

**Rationale**: the API does not configure a JSON string-enum converter, so an enum property would be written and read as a number. Adding a global converter would change every other endpoint.

## Addendum 2026-11-17: user stories 6 and 7

Checked against Elvia's tariffblad 1.0 (valid from 2026-01-01 and 2026-07-01) and an Elvia bill for October 2026; the
figures are in the spec under "Reference data for the tests".

## D18. Exemptions are location flags

**Decision**: `Location` gets `ElectricityTaxExempt` and `VatExempt` (default false). `CostCalculator` uses 0 for the
electricity tax when the first is set and 0 for VAT when the second is set; the Enova fee is always added. The flags
are set by the Admin location-pricing endpoint, never derived from the price zone.

**Rationale**: FR-021, FR-022. The tax exemption follows municipalities (tiltakssonen) and the VAT exemption counties
(Nordland, Troms, Finnmark); neither matches NO4, so the zone cannot decide.

**Alternatives**: derive from the zone (wrong for most of NO4 for the tax, and at the borders for VAT); a municipality
list on the location (data the API does not have and a list to keep up to date for two flags).

## D19. The cap values are PriceRate kinds

**Decision**: two new `PriceRateKind` values, `NorgesPrisCapHome` and `NorgesPrisCapHolidayHome`, value in kWh per
month, managed through the existing price-rate endpoints and `DatedRatePolicy`. The validator allows up to 99,999 kWh
for these kinds (the column is `decimal(9,4)`), 100 for the fees and 1 for VAT. The cap for a month is the value valid
on the first Oslo day of the month (`PriceRateBook.ValueOn(kind, DateOnly)`). Seeded: 5,000 and 1,000 from 2025-10-01,
the start of Norgespris.

**Rationale**: FR-023. They are one national number from a start date, exactly what `PriceRate` holds, so no new table,
repository or endpoints (Constitution VIII). Picking the value on the 1st keeps a month from mixing two caps.

**Alternatives**: a separate cap table (same behaviour, more code); fields on `NorgesPrisRate` (couples two values that
change at different times and touches the Norgespris slice).

## D20. Applying the cap: the caller passes the month kWh before the hour

**Decision**: `CostCalculator.CalculateAsync` gets a parameter `monthKwhBefore`: the kWh the location used in the same
Oslo calendar month before this hour. The pure `NorgesPrisCap.Split(monthKwhBefore, hourKwh, cap)` returns
`(WithinCap, AboveCap)`. On Norgespris terms (the actual of a Norgespris location and the comparison of a spot
location) the within part is priced at the Norgespris rate and the above part on spot terms with the markup. Spot terms
are never split. Callers:

- `HourlyCostProvider` reads the hourly consumption from the Oslo start of the month of `fromUtc` (same query, earlier
  start), keeps a running sum that resets at each Oslo month start, and returns only the requested hours.
- `GetCurrentHourCostQueryHandler` and `GetDayPricesQueryHandler` get the month sum before the hour (or before the day,
  then adding the measured kWh of each hour) from one new aggregate query
  `IConsumptionRepository.GetConsumptionKwhAsync(locationId, fromUtc, toUtc)`.
- The saving uses `HourlyCostProvider`, so it follows the cap with no change of its own (FR-025).

**Rationale**: FR-024. The calculator stays one hour at a time and needs no consumption repository; the month context is
the caller's, and every caller already has the consumption. Reading from the month start means an hour has the same
price in every range (story 6, scenario 4).

**Alternatives**: the calculator loads the month itself (a second consumption read per request, and the calculator would
depend on consumption); apply the cap per day or per request range (gives a different price for the same hour depending
on the range asked for).

## D21. Result shape of a split hour

**Decision**: `RateCost` gains `CapSplit? Cap` with `KwhWithinCap`, `KwhAboveCap` and `AboveCapBreakdown`. `Breakdown`
stays the parts of the kWh within the cap (the Norgespris terms); when the whole hour is above the cap, `Breakdown` is
the spot-terms parts and `KwhWithinCap` is 0. `Cost` = within x Norgespris total + above x spot total, exactly.
`RatePerKwh` = `Cost` / kWh (for a 0 kWh hour, the rate of the next kWh). `Cap` is null on spot terms and when the hour
is fully within the cap. The JSON gets an additive `cap` object next to `parts`.

**Rationale**: FR-007 says the parts add up exactly; averaging two breakdowns would need a division per part. Keeping
two exact breakdowns plus the kWh split keeps every figure exact and checkable, and the frontend keeps reading
`ratePerKwh` and `cost`.

## D22. Day prices with the cap

**Decision**: `/prices` shows, per hour, the rate of the next kWh given the month kWh before the hour (measured
consumption up to that hour). Once the month has passed the cap, a Norgespris location sees the spot rate.

**Rationale**: that is what the next kWh costs, which is the question the price list answers. Future hours use the
month kWh measured so far.

## D23. Capacity steps: one table of dated step rows

**Decision**: `GridCapacityStep` (tariff, valid from, lower kW, upper kW or null for the top step, monthly amount
excluding VAT). The Admin adds a whole step table for a start date in one request; the validator requires it to start
at 0, be contiguous (each lower bound equals the previous upper bound), strictly increasing, and have exactly one top
step without an upper bound, which is the last one. `DatedRatePolicy` applies per (tariff, start date): `DateExists`,
`NotNewest`, `ConfirmationRequired`, and only an upcoming table can be deleted (all its rows together).

**Rationale**: FR-027. The steps of one date belong together and change together (Elvia changed every amount on
2026-07-01), so they are added and removed as one table under the existing dated rules.

**Alternatives**: one row per date with the steps as JSON (cannot be checked by the database and is awkward in EF
queries); steps without dates (loses history, the problem spec 007 solved).

## D24. The capacity calculation

**Decision**: a pure `CapacityCharge.Calculate(hours, steps, vatFraction, monthEnded)`. `hours` are the hourly kWh of one
Oslo calendar month (one hour's kWh is its average kW). Steps: daily peak per Oslo date = the highest hour; the three
highest peaks on different days (fewer if fewer days have data); average; step where lower <= average < upper (top
step: lower <= average). Amount = step amount x (1 + VAT), where VAT is the value on the first day of the month, or 0
for a VAT-exempt location. Returns the peaks (date, hour, kW), the average, the step, the amount excluding and
including VAT, the kW to the next step, and `Provisional = !monthEnded`. No steps or no tariff -> `Set = false`.

**Rationale**: FR-028 to FR-031 and the Elvia definitions (dognmaks = the clock hour with the highest kWh in a day;
kWh/h = kW). Fewer than three days averaging what exists was decided by the product owner on 2026-11-17. The border
rule (lower inclusive) is an assumption to confirm with Elvia.

## D25. The capacity endpoint

**Decision**: `GET /api/electricity-cost/capacity?locationId=&month=YYYY-MM` (month optional, default the current Oslo
month) in the ElectricityCost slice, for users linked to the location like the other cost endpoints. One consumption
read for the month and one step read for the tariff. Not added to the hourly or daily figures (FR-030).

**Rationale**: the charge is monthly; showing it next to the hourly figures without spreading it keeps FR-006 (daily =
sum of hours) true.

## D26. Test data

**Decision**: tests use the Elvia figures stored excluding taxes and VAT (day 0.2899 / night 0.1699 from 2026-07-01,
0.2099 / 0.1299 from 2026-01-01; the step tables of both dates) and reproduce the October 2026 bill lines within 0.01 kr
per line. Capacity examples: peaks 6.2, 5.1 and 4.9 kW give 5.4 kW, step 5-10, 336 kr, 420 kr with VAT (the bill);
an average of exactly 5.00 kW gives step 5-10; two days with peaks 4.0 and 6.0 give 5.0 kW; two highest hours on one day
count once. Cap examples: a holiday home with 999.35 kWh (the bill) stays within; the same month plus 200.65 kWh puts
1,000 kWh at Norgespris and 200 kWh on spot terms, with the crossing hour split.
