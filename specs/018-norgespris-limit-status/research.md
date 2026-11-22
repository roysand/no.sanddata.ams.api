# Research: The Norgespris Limit and the Spot Energy Price for the App

Spec: [spec.md](spec.md). The Technical Context had no open "NEEDS CLARIFICATION". The points below are the design
choices it rests on.

## R1. Where the status lives

**Decision**: A new query in the `ElectricityCost` slice: `GetNorgesPrisStatusQuery`, its handler, and
`GET /api/electricity-cost/norgespris-status`. It takes no parameters.

**Rationale**:
- The status is made of figures this slice already owns: the cap (`CostCalculator`), the Oslo month (`CostTime`) and
  the bill's projection (`ConsumptionProjection`). Putting it here keeps "computed exactly as the bill" (FR-003) a
  matter of calling the same code, not of copying it.
- The `NorgesPris` slice manages the rates (Admin only). The status is a location's use, read by owners and viewers.

**Alternatives considered**:
- *Add the figures to the bill.* The app would need one heavy bill request per location on every page (the spec's
  input rules this out).
- *Add them to `GET /api/locations`.* That would make the location list depend on readings, rates and the cost
  calculation, which is exactly the coupling between slices the constitution forbids.

## R2. Which locations are listed

**Decision**: `ILocationRepository.GetForUserWithRoleAsync(userId)`, the same call `GET /api/locations` uses, then
keep the locations that are active and have `HasNorgesPriceAgreement`. Ordered as that list.

**Rationale**:
- FR-007 and user story 3, scenario 2: an Admin gets the locations `GET /api/locations` gives them. That list is
  based on the user's own links (owner or viewer), also for an Admin, so the same call gives the same set.
- The list includes an owner's inactive locations (so they can switch them on). The edge cases say an inactive
  location is not listed, so it is filtered out here.
- A user without locations, or with none on Norgespris, gets `items: []` (scenario 8).

## R3. The limit

**Decision**: A new public `CostCalculator.NorgesPrisCapAsync(location, instantUtc)`, the body of today's private
`CapForAsync`: the `NorgesPrisCapHolidayHome` or `NorgesPrisCapHome` value valid on the first Oslo day of the month.
The pricing code calls the same method. `limitKwh` is null when no cap is set for the month.

**Rationale**: one rule, read once per request from the calculator's rate book (it is scoped). A limit change takes
effect in the month it is valid for, as in the cost calculation (edge case "a limit change").

## R4. The kWh so far

**Decision**:
- **Meter**: the sum of the location's hourly rows with `BucketStart` in [start of the Oslo month, now). The running
  hour's row has the readings up to now, so it is included (edge case "the running hour").
- **Car**: when `Location.IncludeEvChargerInTotals` is on, plus the charger's stored kWh for the hours in the same
  range (`IChargingSessionRepository.GetHourlyEnergyAsync`). Off, nothing is added (FR-004).
- 0 when there are no readings (scenario 7).

**Rationale**:
- This is the same running total the cost calculation uses for the cap (R9), so the status and the prices agree
  about when the limit is passed.
- The charger's kWh are stored per hour when a session ends (spec 016: Zaptec only gives completed sessions). A
  session still charging is therefore not counted yet, in the status or in the cost calculation. That is accepted:
  it is the same data the bill shows.

## R5. The projection

**Decision**: `projectedKwh` = the kWh before the current hour (meter, plus the car when included) + the projected
kWh of the current hour and the rest of the month, from `ConsumptionProjection.Project` over the 168 hours before the
current hour. It is exactly the bill's `totalKwh + projection.kwh`. The car is not projected. `projectedKwh` is null
when the window has no readings (`BasedOnHours == 0`).

The bill's window and remaining-hours code moves into one helper in `ConsumptionProjection`
(`ProjectRestOfMonth(rows, currentHourStartUtc, monthEndUtc)`), which both the bill and the status call.

**Rationale**:
- Scenario 5 and SC-001: the same figure as the bill. Calling the same helper makes it the same by construction.
- The bill counts the current hour as projected (its readings are partial). The status's kWh so far include the
  running hour's readings (R4), and its projection replaces them with the projected full hour, as the bill does.
  Early in an hour the two can therefore differ by less than one hour's use; SC-001's 0,01 kWh is met at the
  bill's own cut (the start of the current hour).
- The car is never projected (spec 016): its kWh arrive per completed session, so a 7-day profile of them would be a
  guess about when the car is plugged in. Its kWh so far are in the projection's starting point.
- With no readings in 7 days there is no profile, and the bill's projection is 0 kWh with `basedOnHours: 0`. A
  location without readings this month is the case of scenario 7: kWh so far 0 and projection null. In the first
  hours of a month the previous month's readings still give a projection, as in the bill.

**Alternatives considered**: *Call the bill's handler for each location.* That prices every hour of the month for
every location (hundreds of calculations each), which SC-003 does not allow.

## R6. The Norgespris price including VAT

**Decision**: A new public `CostCalculator.NorgesPrisPriceWithVatAsync(location, instantUtc)`: the Norgespris rate in
effect at the instant (`NorgesPrisSchedule.RateFor`) × (1 + VAT at the instant), with VAT 0 for a VAT-exempt
location. Null when the rate or the VAT is missing. Asked at now.

**Rationale**:
- The spec's comparison is energy only: no grid charge, taxes or supplier markup.
- The rate in effect now, so a rate that starts on the 1st is not used before (edge case "a rate change on the 1st").
- VAT follows the location, as in the cost calculation (Nord-Norge is VAT exempt).

## R7. The reset time

**Decision**: `resetsAt` = the UTC instant of 00:00 Oslo time on the 1st of next month
(`CostTime.LocalDayStartUtc`). `month` is the current Oslo month as `yyyy-MM`.

## R8. Over the limit

**Decision**: `overLimit` = `limitKwh` is set and `kwhSoFar ≥ limitKwh`. `overLimitKwh` = `kwhSoFar − limitKwh`
when over, else 0. No limit means never over.

**Rationale**: scenario 6 says "at or above", which is also when the cost calculation prices the next kWh on spot
terms (`NorgesPrisCap.IsPassed`).

## R9. The car in the Norgespris limit (FR-004)

**Decision**: When `Location.IncludeEvChargerInTotals` is on, every place that keeps the month's running kWh for the
cap adds the charger's kWh of the earlier hours of the same Oslo month:

| Where | Today | With the car |
|---|---|---|
| `HourlyCostProvider` (hourly, daily, saving, bill so far) | meter kWh of earlier hours | + charger kWh of earlier hours, read from the start of the month |
| `GetMonthlyBillQueryHandler.ProjectAsync` | meter kWh so far | + the charger's kWh so far (the bill's `evCharger.kwh`) |
| `GetDayPricesQueryHandler` (`/prices`) | meter kWh before the day, then each hour | + charger kWh before the day, then each hour |
| `GetCurrentHourCostQueryHandler` | meter kWh before the hour | + charger kWh before the hour |

Within an hour the car's kWh count after the house's: the house's kWh of hour *h* are split by the cap using the kWh
of the hours before *h* (house and car), and the car's kWh of *h* are added to the total for *h + 1*. This is the
same rule the house's own kWh already follow.

A charging hour without a meter reading is still added to the running total. The car itself is still never priced:
no hour, line or total gets its kWh, and the bill's lines change only because more of the house's kWh fall above the
cap (a smaller `NorgesPrisCredit`, the same `SpotEnergy` quantity).

With the setting off, nothing changes. With no charger, `GetHourlyEnergyAsync` returns no hours and nothing changes.

**Rationale**:
- Roy confirmed the meaning on 2026-11-22 (spec, Assumptions): on, the charger is on the house's agreement, so its
  kWh are the house's usage for the Norgespris limit.
- Reading the charger from the start of the month in `HourlyCostProvider` gives an hour the same price whatever range
  it is asked for in, the property the provider already has for the meter.
- The order within an hour cannot be known from hourly figures. Counting the car after the house keeps the existing
  rule for a single hour and never prices the car.

**Alternatives considered**:
- *Price the car's kWh as part of the house.* The car is on its own meter and its cost is a frontend feature (spec,
  Assumptions); the bill's quantities must stay the meter's.
- *A new repository method for a kWh sum.* `GetHourlyEnergyAsync` already returns at most one month of hours per
  location, and its sum is what the running totals need hour by hour.

## R10. The spot energy price per hour

**Decision**: `/api/electricity-cost/prices` adds `spotEnergyPricePerKwh` to each hour. It comes from a new public
`CostCalculator.SpotEnergyWithVatAsync(location, hourStartUtc)`: today's `SpotEnergyAsync` (day-ahead price ÷ 1000 ×
the EUR/NOK rate the calculation uses) × (1 + VAT of the hour), VAT 0 for a VAT-exempt location. Null when the price
or the exchange rate is missing. A negative price stays negative.

**Rationale**:
- The same exchange-rate fallback and the same preloaded prices as the cost calculation, so no new query per hour
  (the handler already preloads the day).
- It does not depend on the pricing model or the cap, so it is right also for a location over its limit (user
  story 2).
- `ratePerKwh` and `spotRatePerKwh` keep their meaning (FR-006); with the car counted (R9), a location over its
  limit sooner sees the spot rate sooner, which is the intended change.

## R11. Performance

**Decision**: Per listed location, two queries: one `GetHourlyAsync` from the earlier of the month start and the
window start (168 hours before the current hour) to now, and, only when the car is counted, one
`GetHourlyEnergyAsync` for the month so far. The kWh so far, the kWh before the current hour and the window are all
taken from the one row set in memory. The rate book and the Norgespris rates are read once per request by the scoped
calculator.

**Rationale**: SC-003 asks for < 300 ms p95 for 3 locations. That is about 6 indexed queries over at most 45 days of
hourly rows per meter (~1 100 rows), and no pricing. No cache is added (constitution VIII).

## R12. Logging

**Decision**: One new message, `NorgesPrisStatusQueried` (EventId 1505, Information), with the user id and the number
of locations listed. No PII. The prices endpoint keeps `CostQueried`.
