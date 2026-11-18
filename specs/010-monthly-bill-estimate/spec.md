# Feature Specification: Estimated Bill for the Month

**Feature Branch**: `feature/010-monthly-bill-estimate`

**Created**: 2026-11-18

**Status**: Draft

**Input**: Backlog note `specs/_backlog/monthly-bill-estimate.md` (written 2026-11-18, with Roy's invoice for July 2026
as ground truth). User description: "Estimated bill for the month, see specs/_backlog/monthly-bill-estimate.md".

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The supplier markup is charged under Norgespris too (Priority: P1)

A household on Norgespris looks at what an hour or a day has cost. The supplier (Fjordkraft) charges its markup
(påslag) on every kWh, also under Norgespris, so the price per kWh includes it. Today the markup is left out for
Norgespris locations, so every Norgespris cost is too low by the markup (9.59 kr in July 2026 for Roy's holiday home).

**Why this priority**: It is a wrong number in figures the app already shows, and the bill estimate cannot match the
invoice without it. It is small and can ship on its own, before the rest of this feature.

**Independent Test**: For a Norgespris location with a markup of 0.03192 kr per kWh excluding VAT, request the cost of
an hour with 1 kWh and confirm the price is (Norgespris rate + markup + grid energy charge + taxes) x 1.25.

**Acceptance Scenarios**:

1. **Given** a Norgespris location with a supplier markup, **When** an hour up to the monthly cap is priced, **Then**
   the markup is added to the Norgespris rate, as it is for a spot location.
2. **Given** the change is deployed, **When** past hours of a Norgespris location are requested, **Then** they are
   recalculated with the markup, so the hourly cost, the daily cost, the current hour and the day prices all rise by
   the markup (plus VAT) per kWh.
3. **Given** the Norgespris saving (spec 008, user story 5), **When** it is calculated, **Then** the markup is on both
   sides and cancels, so the saving per kWh up to the cap is (spot price − Norgespris rate) x (1 + VAT).
4. **Given** a supplier with no markup under Norgespris, **When** the Admin sets the location's markup to 0, **Then**
   no markup is added. There is no separate setting for "markup under Norgespris".

---

### User Story 2 - The bill for a finished month matches the invoice (Priority: P1)

A household member picks a month that has ended and sees the bill as the invoice shows it: the supplier part (Strøm)
and the grid part (Nettleie), each with its lines and subtotal, and the total. They can hold the app's figure next to
the invoice and check it line by line.

**Why this priority**: It proves the calculation against a real invoice. The estimate for the current month (user
story 3) is the same calculation with a projection added, so it cannot be trusted until this one matches.

**Independent Test**: Request July 2026 for Roy's holiday home and compare with the July invoice in "Reference data
for the tests": the total is 673.55 kr and every line is within the tolerance stated there.

**Acceptance Scenarios**:

1. **Given** a month that has ended, **When** its bill is requested, **Then** the answer has a Strøm part and a
   Nettleie part, each with its lines and subtotal, and a total that is the sum of the two parts, all including VAT,
   and it is marked as final.
2. **Given** the Strøm part, **When** it is read, **Then** it has these lines: the supplier monthly fee, the energy at
   the spot price, and the supplier markup.
3. **Given** the Nettleie part, **When** it is read, **Then** it has these lines: the grid energy charge for day kWh
   and for night and weekend kWh, the capacity charge, the electricity tax, the Enova fee and, for a Norgespris
   location, the Norgespris credit.
4. **Given** a Norgespris location, **When** the bill is calculated, **Then** the Strøm energy line is priced at the
   spot price for every kWh, and the Nettleie part has a Norgespris credit of (spot price − Norgespris rate) x
   (1 + VAT) per kWh up to the monthly cap, with no VAT on the credit itself, as the grid company bills it.
5. **Given** the Strøm energy line and the Norgespris credit, **When** they are added, **Then** the net energy cost
   equals what the hourly cost calculation (spec 008) gives for the same hours, to the øre, so the bill and the hourly
   figures never disagree.
6. **Given** an hour where the spot price is below the Norgespris rate, **When** the credit is calculated, **Then**
   that hour adds to the credit with a negative amount (a charge), as it does on the invoice.
7. **Given** a Norgespris location with consumption above the monthly cap, **When** the bill is calculated, **Then**
   the kWh above the cap get no credit.
8. **Given** the supplier monthly fee and the capacity charge, **When** a month is calculated, **Then** each is a full
   month, never prorated by days.
9. **Given** each line, **When** it is read, **Then** it has its quantity (kWh, or 1 month), its price per unit, its
   VAT rate and its amount, so it can be compared with the invoice line.

---

### User Story 3 - The current month shows what the bill will probably be (Priority: P1)

A household member opens the app in the middle of the month and sees the expected bill for the month: what has been
used and charged so far, what the rest of the month will probably add, and the fixed amounts. The figure is marked as
an estimate, and the household can see how much of it is known and how much is projected.

**Why this priority**: This is what the user asked for: the number other electricity apps show first. A frontend card
("Expected bill") waits for it.

**Independent Test**: For a location with known readings on the first 10 days of a 30-day month, request the current
month and confirm that the "so far" figures equal the finished-month calculation for those days, that the projection
covers exactly the remaining hours, and that the total is so far + projection + fixed amounts.

**Acceptance Scenarios**:

1. **Given** the current month, **When** its bill is requested, **Then** every line has an amount so far and a
   projected amount for the rest of the month, the total is their sum, and the bill is marked as an estimate.
2. **Given** the hours that have passed, **When** the so-far amounts are calculated, **Then** they use the same
   calculation as a finished month.
3. **Given** the hours that have not passed, **When** their consumption is projected, **Then** it is projected by
   repeating the hourly profile of the last 7 days before now: each remaining hour gets the average kWh of the
   hours on the same weekday and Oslo hour of day in those 7 days (also when they fall in the previous month).
   Because each projected hour has its own Oslo time, its kWh is priced at the day rate or the night and weekend
   rate as for a real hour.
4. **Given** projected hours, **When** their spot price is needed, **Then** hours with a published spot price use it,
   and the others use the plain average of the hourly spot prices of the last 7 days before now, not weighted by
   consumption.
5. **Given** a Norgespris location below its cap, **When** the rest of the month is projected, **Then** the total
   does not depend on the assumed spot price, because the Strøm energy line and the credit offset each other; only
   the split between Strøm and Nettleie does.
6. **Given** a Norgespris location whose projected consumption passes the monthly cap, **When** the month is
   projected, **Then** the projected kWh above the cap are priced on spot terms and get no credit.
7. **Given** the capacity charge of the current month, **When** it is included, **Then** it is the step on the days so
   far (spec 008, user story 7), marked as provisional, because the step can still rise.
8. **Given** the first day of the month, **When** the bill is requested, **Then** a projection is still given from
   the last 7 days of the previous month, and the answer says how many days of data it is based on so the app can
   show how certain the estimate is.
9. **Given** a location with fewer than 7 days of readings (a new location), **When** the rest of the month is
   projected, **Then** the profile uses the days there are, an hour with no matching hour in those days gets the
   average kWh per hour of the days there are, and with no readings at all the projection is 0 kWh and the answer
   says it is based on 0 days.
10. **Given** a month that has not started, **When** its bill is requested, **Then** it is refused.

---

### User Story 4 - An Admin sets the supplier monthly fee (Priority: P2)

An Admin sets, per location, the supplier's monthly fee (34.00 kr per month including VAT for Fjordkraft in July
2026) next to the supplier markup on the location pricing settings. The fee is entered excluding VAT (27.20 kr), and
VAT is added unless the location is exempt from VAT.

**Why this priority**: The bill needs it, but it is one setting on an existing Admin page, and the rest of the bill
can be tested with the fee set directly in the test data.

**Independent Test**: Set the fee of a location to 27.20 kr, request a month's bill and confirm the Strøm part has a
monthly fee line of 34.00 kr; mark the location as exempt from VAT and confirm the line is 27.20 kr.

**Acceptance Scenarios**:

1. **Given** an Admin, **When** they set the supplier monthly fee of a location, **Then** it is saved with the other
   location pricing settings and used for every month's bill of that location.
2. **Given** a location without a monthly fee set, **When** its bill is requested, **Then** the fee line is shown as
   not set, never as zero, and the bill is marked as incomplete. An Admin sets 0 for a supplier with no fee.
3. **Given** the location pricing list, **When** an Admin reads it, **Then** a location without a monthly fee is
   marked as incomplete, as one without a grid tariff is (spec 008, user story 4).
4. **Given** a regular user, **When** they try to read or change the fee, **Then** it is refused.
5. **Given** a change of the fee, **When** past months are requested, **Then** they are recalculated with the new
   fee, as for the other location pricing settings.

---

### Edge Cases

- **Month boundaries.** A month is an Oslo calendar month. Hours are assigned by their Oslo time, including the 23-
  and 25-hour days of the clock changes.
- **Missing readings.** Hours without a reading in the past part of the month are not filled in. The answer says how
  many hours had readings out of how many have passed, so a gap is visible and never hidden as zero consumption.
- **Missing prices.** An hour whose spot price or exchange rate is missing makes the spot-dependent lines unavailable
  for that hour, as in spec 008; the answer says how many hours were counted, and the bill is not presented as
  complete.
- **Location without a grid tariff or capacity steps.** The grid lines and the capacity charge are shown as not set,
  never as zero, and the bill is marked as incomplete.
- **A location set up in the middle of the month.** The fixed amounts are still a full month (not prorated); the
  energy lines cover the hours with readings.
- **Rates that change during the month.** Each hour uses the per-kWh values valid for that hour (spec 008). The
  monthly fee, the capacity steps and the VAT on the monthly amounts use the values valid on the first Oslo day of the
  month.
- **VAT-exempt location.** No VAT is added on any line, and the Norgespris credit is (spot price − Norgespris rate)
  per kWh.
- **Electricity-tax-exempt location.** The electricity tax line is 0; the Enova fee line is still there.
- **Spot location.** There is no Norgespris credit line, and the Strøm energy line is the full energy cost.
- **The last hour of the month.** When the month ends, the bill becomes final: no projection, the capacity charge is
  final.
- **Rounding.** Amounts are carried at full precision; the lines, the parts and the total add up with no rounding gap.
  The invoice rounds every line, which is why the comparison has a tolerance.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST add the location's supplier markup to the price per kWh of a Norgespris hour, as for a
  spot hour, and MUST recalculate past hours with it.
- **FR-002**: The Norgespris saving MUST include the markup on both sides, so it cancels out.
- **FR-003**: The system MUST provide the bill of a location for an Oslo calendar month, for the current month and
  for any month that has ended, and MUST refuse a month that has not started.
- **FR-004**: The bill MUST have a Strøm part (supplier monthly fee, spot energy, supplier markup) and a Nettleie part
  (grid energy charge for day kWh, grid energy charge for night and weekend kWh, capacity charge, electricity tax,
  Enova fee and, for a Norgespris location, the Norgespris credit), each with a subtotal, and a total equal to the sum
  of the two parts.
- **FR-005**: Each line MUST give its quantity, its price per unit, its VAT rate and its amount including VAT; each
  part and the total MUST also give the amount excluding VAT and the VAT.
- **FR-006**: For a Norgespris location, the system MUST price the Strøm energy line at the spot price for every kWh
  and give a Norgespris credit of (spot price − Norgespris rate) x (1 + VAT) per kWh up to the monthly cap, summed
  hour by hour, with no VAT on the credit line. The net energy (energy line + credit) MUST equal the energy part of the
  hourly cost calculation for the same hours.
- **FR-007**: The system MUST include the supplier monthly fee and the capacity charge as full months, never prorated.
- **FR-008**: For the current month, each line MUST give the amount so far and the projected amount for the rest of
  the month, and the bill MUST be marked as an estimate; a month that has ended MUST be marked as final and have no
  projection.
- **FR-009**: The system MUST project the consumption of each remaining hour of the current month as the average kWh
  of the hours with the same weekday and Oslo hour of day in the last 7 days before now, MUST fall back as in user
  story 3, scenario 9, when there are fewer than 7 days of readings, and MUST state how many days of data the
  projection is based on.
- **FR-010**: The system MUST price projected hours with the per-kWh calculation of spec 008, including the
  Norgespris cap, using the published spot price where there is one and otherwise the plain average of the hourly
  spot prices of the last 7 days before now.
- **FR-011**: The capacity charge of the current month MUST be the step on the days so far, marked as provisional.
- **FR-012**: An Admin MUST be able to set, per location, the supplier monthly fee in NOK per month excluding VAT, on
  the existing location pricing settings and under the same Admin-only rules; the system MUST add VAT unless the
  location is exempt from VAT, using the VAT valid on the first Oslo day of the month.
- **FR-013**: The system MUST show a line whose setting is missing (monthly fee, grid tariff, capacity steps) as not
  set, never as zero, and MUST mark such a bill as incomplete.
- **FR-014**: The system MUST state how many hours had readings and how many were priced out of the hours that have
  passed in the month.
- **FR-015**: The bill MUST be visible only to users linked to the location, as the other cost figures are.
- **FR-016**: The system MUST log changes of the supplier monthly fee with the acting user, using the project logging
  rules.
- **FR-017**: The system MUST load the dated values and the month's readings once per request, not once per hour.

### Key Entities

- **Monthly bill** (computed): For a location and an Oslo calendar month: the Strøm part, the Nettleie part, the
  total, whether it is final or an estimate, whether it is complete, and the counts of hours with readings, hours
  priced and days the projection is based on.
- **Bill part** (computed): Strøm or Nettleie: its lines and its subtotal including VAT, excluding VAT and the VAT.
- **Bill line** (computed): One line as on the invoice: what it is, the quantity, the price per unit, the VAT rate,
  the amount so far, the projected amount and the total, and a mark when its setting is not set or its data is
  incomplete.
- **Supplier terms** (changed): Per location, the supplier markup (spec 008) and now the supplier monthly fee in NOK
  per month excluding VAT.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The bill for July 2026 for Roy's holiday home matches the July invoice: the total is within 0.10 kr of
  673.55 kr, and each line is within the tolerance in "Reference data for the tests".
- **SC-002**: For hand-calculated months (a spot location, a Norgespris location below the cap, a Norgespris location
  above the cap, a VAT-exempt location and a tax-exempt location), 100% of lines, parts and totals match.
- **SC-003**: For every tested month, the net energy of the bill (Strøm energy + Norgespris credit + markup) equals
  the sum of the energy and markup parts of the hourly costs for the same hours, to the øre.
- **SC-004**: For the current month, the so-far amounts equal the finished-month calculation for the hours that have
  passed in 100% of tested cases, and the total equals so far + projected.
- **SC-005**: After a month has ended, the estimate shown for its last day and the final bill differ only by the
  projected part, so a household can see how good the estimate was.
- **SC-006**: A household can hold the app's bill next to its invoice and find every invoice line in the app with the
  same quantity and price.
- **SC-007**: No user who is not linked to the location can see its bill, and no regular user can read or change the
  supplier monthly fee.
- **SC-008**: Showing the bill for a month takes no noticeably longer than showing that month's daily costs.

## Assumptions

- Fjordkraft (supplier) and Elvia (grid company) are the reference for the invoice layout. Other suppliers' and grid
  companies' layouts are out of scope; the lines are the parts every Norwegian household bill has.
- The supplier bills the full spot price also under Norgespris, and the grid company credits the difference without
  VAT. The hourly cost (spec 008) keeps pricing Norgespris hours directly at the Norgespris rate; only the bill shows
  the spot energy and the credit as two lines, and the net is the same.
- The markup applies under Norgespris for every location. A supplier without it is handled by setting the location's
  markup to 0. Spec 008 assumed no markup under Norgespris "to be confirmed against the supplier terms"; the July
  invoice settles it.
- The markup fix (user story 1) changes figures the app already shows: the hourly and daily cost, the current hour,
  the day prices and the Norgespris saving of every Norgespris location with a markup. The frontend needs no change
  for it.
- The invoice's electricity tax and Enova fee are on the Nettleie part, split into day and night lines with the same
  rate. The bill may show each as one line with the total kWh; the comparison adds the invoice's two lines.
- The capacity charge is the one `GET /api/electricity-cost/capacity` already gives (spec 008, user story 7).
- The projection methods were decided by the product owner on 2026-11-18: consumption follows the hourly profile of
  the last 7 days (it follows weekdays, weekends and day and night, and works from the first day of the month), and
  hours without a published spot price use the plain average spot price of the last 7 days.
- With 7 days of profile, a weekday and hour of day occurs once, so the "average" is that one hour's kWh; a 23- or
  25-hour clock-change day in the window is handled by Oslo hour of day like any other day.
- Uncertainty of the estimate is shown by giving so far and projected separately and the number of days the
  projection is based on, not as a range.
- Before relying on the July 2026 acceptance test, the location's July readings, July's spot prices and exchange
  rates, and the rates valid in July (grid tariff 2026-07-01, taxes, Norgespris rate, the 1,000 kWh cap) must be in
  the database. The supplier markup (0.03192 kr) and the monthly fee (27.20 kr) excluding VAT must be set on the
  location.
- The spot price on the invoice comes from the supplier's own source; the app's spot price and exchange rate may
  differ slightly. For a Norgespris location below the cap this moves amounts between the Strøm energy line and the
  credit without changing the total, so the total has a tighter tolerance than those two lines.
- Out of scope: any change in the web app (a frontend spec, "Expected bill" card, follows in
  `no.sanddata.ams.frontend`), payment, due dates, and invoice history from the supplier.

## Reference data for the tests

Roy's invoice for July 2026: one combined invoice from Fjordkraft with the grid part from Elvia, for a holiday home on
Norgespris. Amounts include 25 % VAT unless noted.

**Strøm (supplier), 417.33 kr**

| Line | Quantity | Price | VAT | Amount | Stored value (excl. VAT) |
|---|---|---|---|---|---|
| Månedlig fastbeløp | 1 month | 34.00 kr/month | 25 % | 34.00 | fee 27.20 kr |
| Din strømpris | 240.31 kWh | 155.52 øre/kWh | 25 % | 373.74 | spot, from the readings and prices |
| Påslag | 240.31 kWh | 3.99 øre/kWh | 25 % | 9.59 | markup 0.03192 kr/kWh |

**Nettleie (grid), 256.22 kr**

| Line | Quantity | Price | VAT | Amount | Check |
|---|---|---|---|---|---|
| Energiledd weekdays 06–22 | 95.58 kWh | 36.24 øre/kWh | 25 % | 34.64 | 95.58 x 0.2899 x 1.25 |
| Energiledd nights and weekends | 144.73 kWh | 21.24 øre/kWh | 25 % | 30.74 | 144.73 x 0.1699 x 1.25 |
| Norgespris for fritidsbolig | −240.31 kWh | 1.06 kr/kWh | 0 % | −253.58 | (1.2442 − 0.40) x 1.25 = 1.0552 |
| Fastledd 5–10 kW | 1 month | 420.00 kr/month | 25 % | 420.00 | step 3 from 2026-07-01, 336 x 1.25 |
| Elavgift | 95.58 + 144.73 kWh | 8.91 øre/kWh | 25 % | 8.51 + 12.90 | 0.0713 x 1.25 |
| Energifondet | 95.58 + 144.73 kWh | 1.25 øre/kWh | 25 % | 1.20 + 1.81 | 0.0100 x 1.25 |

**Total 673.55 kr** (488.12 excluding VAT + 185.43 VAT; the credit has 0 % VAT).

Cross-check by the hourly calculation: net energy 240.31 x 0.40 x 1.25 = 120.16 (373.74 − 253.58), so the total is
34.00 + 9.59 + 120.16 + 34.64 + 30.74 + 420.00 + 8.51 + 12.90 + 1.20 + 1.81 = 673.55.

**Tolerance.** The invoice rounds each line to the øre and shows the spot price and the credit rate rounded.

- Total: within 0.10 kr.
- Lines that do not depend on the spot price (fee, markup, grid energy, capacity, taxes): within 0.02 kr each.
- Lines that depend on the spot price (Strøm energy, Norgespris credit) and the two part subtotals: within 1.00 kr,
  because the app's spot source and exchange rate may differ from the supplier's. Their sum (net energy) must be
  within 0.02 kr of 120.16 kr.
