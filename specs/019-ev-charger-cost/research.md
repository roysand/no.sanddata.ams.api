# Research: What the Car Charging Costs, on the Bill

Spec: [spec.md](spec.md). The Technical Context had no open "NEEDS CLARIFICATION". The points below are the design
choices it rests on.

## R1. Where the cost is worked out

**Decision**: In the bill handler, right after the hours so far are read, through a new service in the
`ElectricityCost` slice: `EvChargerCost.PriceAsync(location, soFar, chargerHours, calculator, ct)`. It returns the
cost and the rule that priced it. The handler passes it to `BillMapper`, which adds both to `evCharger`.

**Rationale**:
- The cost is on the bill only (spec), so neither `HourlyCostProvider` nor the hourly and daily figures change.
- The provider has already preloaded the month's spot prices and exchange rates for [month start, end of the hours so
  far). Pricing the car before the projection, which preloads a different range, keeps every lookup in memory
  (SC-004).
- The car's hours are the ones the bill's car part already sums: the charger parts of the hours so far and the
  charging hours without a meter reading. The cost therefore covers exactly the kWh the part shows.

**Alternatives considered**: *Price the car inside `HourlyCostProvider`*. It has the running month total, but then
every hourly and daily request would price the car for nothing. The provider would also need a second path for the
charging hours without a meter reading.

## R2. Not counted with the house: the Norgespris price (FR-002, FR-005)

**Decision**: Each hour costs car kWh × `CostCalculator.NorgesPrisPriceWithVatAsync(location, hour)` (spec 018,
R6): the Norgespris rate in effect in that hour × (1 + VAT), with no VAT for a VAT-exempt location. Null when the
rate or the VAT is missing. The rule is one private method of `EvChargerCost`,
`SeparateAgreementCostAsync`, with a comment saying it is the garages' agreement and is the place to change it.

**Rationale**:
- This is the same figure spec 018's status sends as `norgesPrisPricePerKwh`, so the app's two prices agree.
- The house's pricing model, its limit and its markup play no part (scenario 5): the car is not on that agreement.
- There is one place to change, and the bill's lines never read it (FR-005, FR-006).

## R3. Counted with the house: the house's price (FR-003)

**Decision**: Each car hour *h* is priced by the one calculation, as if the car's kWh were house kWh used after the
house's kWh of the same hour: `CostCalculator.CalculateAsync(location, h, carKwh(h), monthKwhBefore, ct).Actual.Cost`,
where `monthKwhBefore` = the house's and the car's kWh of the month before *h* + the house's kWh of *h*.

**Rationale**:
- This uses spec 018's rule (R9) unchanged: within an hour the car counts after the house, and the car's earlier
  hours count toward the limit. The car's kWh that pass the limit are therefore priced on spot terms, and the ones
  within it at Norgespris (scenario 2), split exactly as the calculator splits any hour.
- `Actual.Cost` is what the calculator charges for those kWh in that hour. That includes the energy under the
  house's model, the supplier markup, the grid energy charge, the electricity tax, the Enova fee and VAT, but not the
  capacity charge or the monthly fees (scenario 3). For a house within its limit, the rate per kWh is the prices
  endpoint's `ratePerKwh` for the hour (SC-002).
- A charging hour without a house reading is priced the same way, with 0 house kWh in that hour (scenario 4).
- A negative spot price lowers the cost, as for the house.

**Alternatives considered**: *kWh × `ratePerKwh` of the hour.* That is the rate of the next kWh only, so it would
price all of an hour's car kWh on one side of the limit, and disagree with the calculator where an hour crosses it.

## R4. Null and the rule

**Decision**:
- `cost` is null when any car hour has no price (a missing rate, VAT, spot price or exchange rate), as FR-001 says.
  A partial sum is never sent, because it would read as the whole month.
- `pricedAt` is `HousePrice` when the location counts the car with the house, otherwise `NorgesPrisPrice` (FR-004).
  It is set whenever the car part is set, also when `cost` is null.
- No charging in the month: the car part stays null, as in spec 016.

## R5. Which setting, and which hours

**Decision**: The current setting prices the whole month, past months too, as `included` already does (edge cases).
The hours are those of the car part: the hours so far, never projected (FR-006). The running hour isn't part of the
hours so far, so it isn't priced, matching the car's kWh on the bill.

## R6. What does not change

**Decision**: No line, part, total, capacity step or projection reads the cost (FR-006, SC-003). The bill's lines are
built before the cost is worked out, and `MonthlyBillBuilder` is not touched. `evCharger` gets two new fields;
existing fields keep their meaning (FR-007).

## R7. Performance

**Decision**:
- No new query: the charger's hours are already read for the bill.
- With the setting off, each hour is a lookup in the Norgespris rates and the rate book, which are loaded once per
  request.
- With it on, each car hour is one calculation over preloaded prices. That is about 100 hours a month, against the
  744 the bill already prices.

SC-004 (within 10 % of today) holds without a cache.
