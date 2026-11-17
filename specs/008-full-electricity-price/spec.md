# Feature Specification: Full Electricity Price per kWh

**Feature Branch**: `feature/008-full-electricity-price`

**Created**: 2026-11-16

**Status**: Draft

**Input**: User description: "Also calculate what is saved by using Norgespris; showing it in the UI is a later, separate feature. Calculate the price of the electricity used. It is not energy used x price (for Norgespris kr 0.40 per kWh excluding VAT): the other costs on the bill must be added, including the grid fee that depends on the hour of the day and the day of the week. The calculation must be used everywhere an electricity cost is shown. Phase 1 covers everything that is per kWh and per hour. Monthly parts (supplier monthly fee, grid fixed fee in steps by the kWh of the month or by peak kW, the Norgespris monthly cap) are phase 2."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Costs match what the electricity bill charges per kWh (Priority: P1)

A household member looks at what an hour, a day or the current hour has cost. The figure is the full price per kWh a household pays: the energy price, the supplier markup, the grid energy charge for that hour of the day and day of the week, the electricity tax and the Enova fee, and VAT. Every place that shows an electricity cost uses the same calculation, so the current hour, the hourly list, the daily totals and the day prices always agree.

**Why this priority**: Today every figure is energy only and excludes VAT, so it is well below the bill. This is the whole point of the feature.

**Independent Test**: For one location with known settings, request the cost of a weekday afternoon hour and a Sunday night hour with the same spot price and the same consumption. Both equal consumption times the hand-calculated price, and the afternoon hour is more expensive by exactly the difference between the grid day rate and the night rate (plus VAT).

**Acceptance Scenarios**:

1. **Given** a spot-priced location, **When** the cost of an hour is calculated, **Then** the price per kWh is (1 + VAT) x (spot price + supplier markup + grid energy charge + electricity tax + Enova fee) and the cost is consumption times that price.
2. **Given** a location with the Norgespris agreement, **When** the cost of an hour is calculated, **Then** the energy part is the Norgespris rate valid for that hour and no supplier markup is added.
3. **Given** a weekday that is not a public holiday, **When** the hour starts from 06:00 up to but not including 22:00 Oslo time, **Then** the grid day rate is used; **When** it starts from 22:00 to 05:59, **Then** the night and weekend rate is used.
4. **Given** a Saturday, a Sunday or a public holiday, **When** any hour of that day is calculated, **Then** the night and weekend rate is used all day.
5. **Given** the current hour, the hourly list, the daily totals and the day prices for the same hours, **When** they are compared, **Then** every figure comes from the same calculation and the daily total equals the sum of its hours.
6. **Given** a day with 23 or 25 hours because of the clock change, **When** it is calculated, **Then** every real hour gets exactly one grid rate by its Oslo time.
7. **Given** price data that is missing for an hour (for example the spot price or the exchange rate), **When** the cost is requested, **Then** that part is shown as unavailable, never as zero.

---

### User Story 2 - The parts of the price are available (Priority: P1)

Each calculated price comes with its parts: energy, supplier markup, grid energy charge, taxes, VAT and the total. Whether the app shows the parts is decided later, but the data is there so that showing them needs no new calculation. The existing rate and cost figures are the totals including VAT.

**Why this priority**: The parts make the calculation checkable against a real bill and keep the later UI discussion cheap.

**Independent Test**: Request the cost of one hour and confirm the parts add up to the total price per kWh, and that the existing rate and cost fields equal that total and consumption times it.

**Acceptance Scenarios**:

1. **Given** a calculated hour, **When** the parts are read, **Then** energy + markup + grid + taxes + VAT equals the total price per kWh, with no rounding gap.
2. **Given** the existing rate and cost fields, **When** they are read, **Then** they are the totals including VAT.
3. **Given** the Spot versus Norgespris comparison, **When** it is calculated, **Then** both sides get the same grid charge, taxes and VAT as the actual price, so only the energy part and the supplier markup differ.

---

### User Story 3 - An Admin maintains the rates and settings (Priority: P1)

An Admin keeps the data the price depends on and changes it before it takes effect: the grid tariffs (day rate and night and weekend rate), the electricity tax, the Enova fee and the VAT percentage, each with a start date. An Admin also sets, per location, which grid tariff it uses and the supplier terms (markup). A newer start date takes over without touching older hours.

**Why this priority**: Taxes and tariffs change at the turn of the year and in the middle of it; without this the price would need a code change every time.

**Independent Test**: Add a tax rate that starts next month, confirm hours before it keep the old tax and hours after it use the new one, and confirm a rate that has started cannot be edited or deleted.

**Acceptance Scenarios**:

1. **Given** rates with different start dates, **When** an hour is priced, **Then** the rate with the latest start date on or before the Oslo date of that hour is used, so past hours keep their old values.
2. **Given** an Admin, **When** they add a rate with a start date, **Then** it is saved; a start date already used by the same kind of rate is refused.
3. **Given** a rate that has not started, **When** an Admin deletes it, **Then** it is removed; **Given** a rate that has started, **Then** it cannot be edited or deleted and a correction is made by adding a newer rate.
4. **Given** a start date of today or earlier, **When** an Admin adds a rate, **Then** it must start after every existing rate of that kind and the request must confirm that past costs will be recalculated.
5. **Given** a location, **When** an Admin sets its grid tariff and supplier markup, **Then** every price for that location follows those settings, and changing them recalculates past hours as for the Norgespris agreement flag.
6. **Given** a regular user, **When** they try to read or change any of these settings, **Then** it is refused.

---

### User Story 4 - The first deployment does not break existing locations (Priority: P2)

On the first day the new calculation is used, the figures rise because VAT, grid charge and taxes are now included. A location with no grid tariff or supplier terms set yet must not break or show zero for those parts; the grid part is left out and the breakdown says so, and the Admin can see which locations still need their settings.

**Why this priority**: The calculation should not fail for existing locations, but it is not the core of the feature.

**Independent Test**: Deploy with a location that has no grid tariff set and confirm costs still show, with the grid part marked as not set rather than zero, and that the Admin list marks the location as incomplete.

**Acceptance Scenarios**:

1. **Given** a location without a grid tariff, **When** its cost is calculated, **Then** the grid part is left out and the breakdown says no tariff is set, so the total is not silently presented as complete.
2. **Given** locations with and without settings, **When** an Admin lists the pricing settings of the locations, **Then** the ones without a grid tariff are marked.

---

### User Story 5 - The system can tell what Norgespris saves (Priority: P2)

The system can tell how much the Norgespris agreement has saved a household compared with spot terms: for today, the last 7 days, the month so far and the year so far. The saving is the full-price difference per hour (spot terms minus Norgespris terms), summed over the period, so it follows the same calculation as every other cost. A negative number means an extra cost, because Norgespris is more expensive than spot in cheap hours. For a location without the agreement the same figure tells what the agreement would have saved or cost. Only the numbers are in this feature; showing them in the web app is a later feature.

**Why this priority**: It answers the question the household actually asks about the agreement, and every number it needs is already produced by the calculation in story 1 and 2. A later feature shows it in the UI.

**Independent Test**: For a Norgespris location with hours where spot is above and below the Norgespris rate, request the saving for a day and confirm it equals the sum over the hours of (spot-terms cost minus Norgespris-terms cost), with a negative contribution from the cheap spot hours.

**Acceptance Scenarios**:

1. **Given** a Norgespris location and a period, **When** the saving is requested, **Then** it equals the sum over the hours of the cost on spot terms (energy, supplier markup, grid charge, taxes and VAT) minus the cost on Norgespris terms.
2. **Given** hours where spot is cheaper than Norgespris, **When** the saving is calculated, **Then** those hours reduce the saving and the period total can be negative, shown as an extra cost.
3. **Given** an hour without a spot price or exchange rate, **When** the saving is calculated, **Then** that hour is left out of the saving and the answer says how many hours were counted out of how many were consumed, so a partial figure is never presented as complete.
4. **Given** the periods today, last 7 days, month so far and year so far, **When** each is requested, **Then** it uses Oslo calendar days, and the figures for shorter periods add up to the longer period they are part of.
5. **Given** a Norgespris rate or a tax that changes during the period, **When** the saving is calculated, **Then** each hour uses the values valid for that hour.
6. **Given** a user who is not linked to the location, **When** they ask for the saving, **Then** it is refused as for the other cost figures.

---

### User Story 6 - Exempt areas and holiday homes are priced correctly (Priority: P1)

An Admin marks, per location, whether it is exempt from the electricity tax, whether it is exempt from VAT, and whether
it is a holiday home (fritidsbolig). Households in the tiltakssone (Finnmark and the municipalities Karlsøy, Kvænangen,
Kåfjord, Lyngen, Nordreisa, Skjervøy and Storfjord in Troms) pay no electricity tax, and households in Nordland, Troms
and Finnmark pay no VAT on electricity. Neither area follows the price-zone borders, so both are settings on the
location and are not derived from the price zone. A holiday home has a lower Norgespris monthly cap than a permanent
home (today 1,000 kWh against 5,000 kWh). Both caps are dated values the Admin keeps, so a change in the rules needs no
code change. Consumption above the cap in a calendar month is priced on spot terms.

**Why this priority**: Without the exemptions a northern location is overpriced by the tax and by 25 percent. Without the
cap, a holiday home's cost and Norgespris saving are wrong for every kWh above 1,000 in a month.

**Independent Test**: For a holiday home on Norgespris with 1,200 kWh in a month, confirm that the first 1,000 kWh of
the month are priced at the Norgespris rate and the last 200 kWh on spot terms, and that the hour that crosses the cap
is split by kWh. For a location marked as exempt from tax and VAT, confirm that the electricity tax and VAT parts are
zero and the Enova fee is still added.

**Acceptance Scenarios**:

1. **Given** a location exempt from the electricity tax, **When** an hour is priced, **Then** the electricity tax is 0
   and the Enova fee is still added.
2. **Given** a location exempt from VAT, **When** an hour is priced, **Then** the VAT part is 0.
3. **Given** a Norgespris location, **When** its consumption from the start of the Oslo calendar month passes the cap
   for its kind (holiday home or permanent home), **Then** the kWh up to the cap are priced on Norgespris terms and the
   kWh above it on spot terms (spot energy plus the supplier markup), and the hour that crosses the cap is split by kWh.
4. **Given** a request whose range starts in the middle of a month, **When** the cap is applied, **Then** the
   consumption from the start of that month counts, so an hour has the same price whatever range it is requested in.
5. **Given** cap values with different start dates, **When** a month is priced, **Then** the cap valid on the first day
   of that month applies to the whole month.
6. **Given** a change of a location's exemption flags or holiday-home flag, **When** past hours are requested, **Then**
   they are recalculated with the new settings, as for the Norgespris agreement flag.
7. **Given** the Norgespris saving, **When** consumption is above the cap, **Then** those kWh add nothing to the saving,
   because both sides are priced on spot terms.
8. **Given** a regular user, **When** they try to read or change these settings or the cap values, **Then** it is
   refused.

---

### User Story 7 - The monthly capacity charge follows the grid company's rule (Priority: P2, phase 2)

The grid fixed fee (kapasitetsledd, on the bill as for example "Fastledd 5 - 10 kW") is a monthly amount picked by a
capacity step. The grid company finds the hour with the highest consumption on each day of the month (the daily peak),
takes the three highest daily peaks on three different days, and averages them. That average in kW decides the step
for the month. The steps and their amounts differ between grid companies and change over time, so they are data on the
grid tariff that an Admin maintains. Elvia's steps are in "Reference data for the tests" below; the bill this spec was
checked against shows Elvia's 5-10 kW step at 420 kr per month including VAT.

**Why this priority**: The capacity charge is a large part of the bill (420 kr of about 790 kr before the Norgespris
credit on the checked bill), and the household can lower it by spreading its consumption. It is monthly, not per kWh,
so it is not part of the hourly price and comes after user stories 1 to 6.

**Independent Test**: For a month with known hourly consumption, confirm the three daily peaks found, their average,
the step and the monthly amount against a hand calculation, including a month where the two highest hours fall on the
same day.

**Acceptance Scenarios**:

1. **Given** the hourly consumption of a month, **When** the capacity step is calculated, **Then** the daily peak of
   each Oslo calendar day is the hour with the highest kWh (an hour's kWh equals its average kW), the three highest
   daily peaks are averaged, and the step whose range holds that average is chosen.
2. **Given** two of the highest hours of the month on the same day, **When** the step is calculated, **Then** only the
   higher of them counts, because the three peaks must be on three different days.
3. **Given** an average exactly on a step border (for example 5.00 kW), **When** the step is chosen, **Then** the
   higher step is used: a step covers from its lower bound up to but not including its upper bound. The top step has no
   upper bound.
4. **Given** the month so far, **When** the capacity step is requested, **Then** the answer is the step on the days so
   far, the three peaks it is based on, and how far the average is from the next step, marked as provisional until the
   month has ended.
8. **Given** a month with consumption on fewer than three days (for example the first two days of the month, or a
   month with only two days of data), **When** the step is calculated, **Then** the average is taken over the daily
   peaks there are (two peaks on two days give the average of those two numbers). This applies to a month that has
   ended as well as to the month so far.
5. **Given** steps with different start dates, **When** a month is priced, **Then** the steps valid on the first day of
   that month apply to the whole month.
6. **Given** a location exempt from VAT, **When** the monthly amount is calculated, **Then** no VAT is added; otherwise
   the VAT valid on the first day of the month is added.
7. **Given** a location without a grid tariff, or a tariff without capacity steps, **When** the capacity charge is
   requested, **Then** it is shown as not set, never as zero.

---

### Edge Cases

- **Time zone and weekday.** Day or night and weekday are decided from the Oslo time of the hour, never UTC. The hour that starts at 22:00 Oslo time is night; the hour that starts at 06:00 is day.
- **Public holidays.** Fixed holidays (1 January, 1 May, 17 May, 25 and 26 December) and the Easter-based ones (Maundy Thursday, Good Friday, Easter Monday, Ascension Day, Whit Monday) count as weekend. Easter Sunday and Whit Sunday are Sundays anyway.
- **A rate starts in the middle of a day.** Rates start on a date at midnight Oslo time, so a day never mixes two values of the same kind.
- **Norgespris location, spot comparison.** The comparison uses the supplier markup, because the comparison is what the household would pay on spot terms.
- **Rounding.** Amounts are carried at full precision and only shown rounded; the parts and the total never differ by a rounding gap.
- **Missing exchange rate or spot price.** Handled as before: the energy part is unavailable and so is the total.
- **Exemption areas.** The electricity-tax exemption (tiltakssone) is decided by municipality and the VAT exemption by
  county (Nordland, Troms, Finnmark). An Admin sets both per location; the price zone is never used for this. The Enova
  fee applies everywhere.
- **Monthly charges.** The supplier monthly fee, the grid fixed fee and the capacity charge are not in any hourly price in this phase, so the daily and hourly totals are lower than the monthly bill by those fixed amounts.
- **Norgespris cap.** The cap counts the consumption of the location in the Oslo calendar month. The hour that crosses
  the cap is split: the kWh up to the cap on Norgespris terms, the rest on spot terms. If that hour has no spot price, the
  part above the cap is unavailable, as for any spot hour.
- **Bill lines split by day and night.** The bill shows the electricity tax and the Enova fee on two lines, one for the
  day kWh and one for the night and weekend kWh, but the rate is the same on both lines. The taxes do not depend on the
  hour; only the grid energy charge does.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST calculate the price per kWh for an hour as (1 + VAT) x (energy + supplier markup + grid energy charge + electricity tax + Enova fee), and the cost as consumption times that price.
- **FR-002**: The system MUST use the Norgespris rate valid for the hour (the rate only; the tax field on the Norgespris rows of feature 007 is not used, because taxes are added separately) as the energy part for a location with the Norgespris agreement, and the spot price converted to NOK per kWh otherwise, and MUST add no supplier markup to a Norgespris price.
- **FR-003**: The system MUST pick the grid energy charge from the Oslo time of the hour: the day rate on weekdays that are not public holidays from 06:00 up to but not including 22:00, the night and weekend rate at all other times.
- **FR-004**: The system MUST treat Saturdays, Sundays and the Norwegian public holidays as night and weekend all day, with the holidays computed from the calendar rather than listed by hand.
- **FR-005**: The system MUST keep each grid tariff rate excluding taxes and VAT, so taxes and VAT are applied exactly once.
- **FR-006**: The system MUST use the same calculation for the current hour, the hourly cost, the daily cost and the day prices, and a daily total MUST equal the sum of its hours.
- **FR-007**: The system MUST return the parts (energy, supplier markup, grid energy charge, taxes, VAT, total) with each calculated price, they MUST add up exactly to the total, and the existing rate and cost fields MUST be the totals including VAT.
- **FR-008**: The Spot versus Norgespris comparison MUST apply the same grid charge, taxes and VAT to both sides, and the supplier markup MUST apply to the spot side only.
- **FR-009**: The system MUST store the electricity tax, the Enova fee, the VAT percentage and each grid tariff rate as dated values, and MUST pick the value with the latest start date on or before the Oslo date of the hour.
- **FR-010**: An Admin MUST be able to list and add these dated values and delete one that has not started, and MUST NOT be able to edit or delete one that has started.
- **FR-011**: The system MUST refuse a dated value whose start date equals an existing one of the same kind, and MUST accept a start date of today or earlier only if it is later than every existing one of that kind and the request confirms that past costs will be recalculated.
- **FR-012**: An Admin MUST be able to set, per location, the grid tariff it uses and the supplier markup, and these settings are Admin-only like the Norgespris agreement flag.
- **FR-013**: The system MUST leave the grid part out, and MUST say that no tariff is set, for a location without a grid tariff, and MUST NOT treat it as zero.
- **FR-014**: The system MUST show an hour whose price data is missing as unavailable, never as zero.
- **FR-015**: All of these settings and dated values MUST be limited to Admins: no sign-in is refused, a signed-in user who is not an Admin is refused.
- **FR-016**: The system MUST log adding and deleting a dated value and changing the grid tariff or markup of a location with the acting user, using the project logging rules.
- **FR-017**: The system MUST load the dated values once per request and not once per hour.
- **FR-018**: The system MUST provide the saving from Norgespris for a location over a period as the sum over the hours of the cost on spot terms minus the cost on Norgespris terms, each calculated with the same full-price calculation, and MUST allow the result to be negative (an extra cost).
- **FR-019**: The system MUST leave an hour out of the saving when either side cannot be priced, and MUST state how many hours were counted out of how many had consumption.
- **FR-020**: The system MUST provide the saving for today, the last 7 days, the month so far and the year so far in Oslo calendar days, visible only to users linked to the location.
- **FR-021**: An Admin MUST be able to set, per location, an electricity-tax exemption flag, a VAT exemption flag and a
  holiday-home flag, Admin-only like the other location pricing settings, and the system MUST NOT derive any of them
  from the price zone.
- **FR-022**: For a location exempt from the electricity tax, the system MUST use 0 as the electricity tax and still add
  the Enova fee; for a location exempt from VAT, the system MUST use 0 as VAT.
- **FR-023**: The system MUST store the Norgespris monthly cap for a permanent home and for a holiday home as dated
  values in kWh, maintained by an Admin under the same rules as the other dated values (FR-009 to FR-011), and MUST use
  the cap valid on the first Oslo day of the month for the whole month.
- **FR-024**: For a Norgespris location, the system MUST price the kWh of an Oslo calendar month up to the cap on
  Norgespris terms and the kWh above it on spot terms, split the hour that crosses the cap by kWh, and count the
  consumption from the start of the month whatever range is requested.
- **FR-025**: The Norgespris saving MUST follow the cap, so kWh above it add nothing to the saving.
- **FR-026**: The system MUST log changes of the exemption and holiday-home flags with the acting user, as in FR-016.

Phase 2 (user story 7):

- **FR-027**: The system MUST store, per grid tariff, dated capacity steps: a lower bound in kW, an upper bound in kW
  (none for the top step) and a monthly amount in NOK excluding VAT, maintained by an Admin under the same rules as the
  other dated values (FR-009 to FR-011).
- **FR-028**: The system MUST find the capacity step of a location for an Oslo calendar month as the step holding the
  average of the three highest daily peaks on three different days, where a daily peak is the highest hourly kWh of the
  day, and a step covers from its lower bound up to but not including its upper bound. With daily peaks on fewer than
  three days, the system MUST average the peaks there are.
- **FR-029**: The system MUST return the monthly capacity charge including VAT (none for a VAT-exempt location) with
  the step, the three peaks and their average, and MUST mark the figure for a month that has not ended as provisional.
- **FR-030**: The system MUST NOT spread the capacity charge into the hourly price per kWh; it is a monthly figure.
- **FR-031**: The system MUST show the capacity charge as not set for a location without a grid tariff or without
  capacity steps, never as zero.

### Key Entities

- **Grid tariff**: A tariff of a grid company that a location can use: a name, and dated energy charges (day rate and night and weekend rate, NOK per kWh excluding taxes and VAT).
- **Electricity tax rate**: Dated electricity tax (forbruksavgift) in NOK per kWh.
- **Enova fee rate**: Dated Enova fee in NOK per kWh.
- **VAT rate**: Dated VAT percentage.
- **Supplier terms**: Per location, the supplier markup in NOK per kWh (the supplier monthly fee is phase 2).
- **Price breakdown**: The result of pricing one hour: energy, markup, grid energy charge, taxes, VAT, total per kWh, and a mark for parts that are unavailable or not set.
- **Norgespris saving**: For a location and a period: the saving (spot-terms cost minus Norgespris-terms cost, possibly negative), the number of hours counted and the number of hours with consumption.
- **Norgespris cap**: Dated monthly cap in kWh, one for a permanent home and one for a holiday home.
- **Capacity step** (phase 2): Per grid tariff and dated: lower bound and optional upper bound in kW, and the monthly
  amount excluding VAT.
- **Capacity charge** (phase 2, computed): For a location and a month: the three daily peaks, their average, the step,
  the amount including VAT, and whether the month has ended.
- **Location**: Gains a grid tariff, supplier terms, an electricity-tax exemption flag, a VAT exemption flag and a
  holiday-home flag; the Norgespris agreement flag and price zone are unchanged.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For a set of hand-calculated example hours (weekday day, weekday night, Saturday, Sunday, public holiday, clock-change days, spot and Norgespris), 100% of calculated prices match the hand-calculated values.
- **SC-002**: The current hour, hourly, daily and day-price figures for the same hours agree in 100% of tested cases, and every daily total equals the sum of its hours.
- **SC-003**: After a rate with a future start date is added, 100% of hours before the date keep their old price and 100% of hours from the date use the new one, including the hours around midnight on the start date.
- **SC-004**: A household can compare the per-kWh part of its electricity bill with the app, and the difference is explained only by the monthly charges that phase 2 adds.
- **SC-005**: No regular user can read or change any of the new settings or rates.
- **SC-006**: Showing a month of hourly costs takes no noticeably longer than before and the number of rate lookups does not grow with the number of hours shown.
- **SC-007**: For a period with hours on both sides of the Norgespris rate, the saving shown equals the hand-calculated sum of the hourly differences in 100% of tested cases, and the shorter periods add up to the longer ones.
- **SC-008**: For hand-calculated months of a holiday home and a permanent home that pass their cap, and for locations
  exempt from tax, from VAT and from both, 100% of calculated hourly costs and savings match, including the hour that
  crosses the cap and a request that starts in the middle of the month.

## Assumptions

- Phase 1 covers only per-kWh and per-hour parts. The supplier monthly fee and the grid capacity charge (user story 7)
  are phase 2. The capacity charge uses the rule of the three highest daily peaks; a grid company that steps by the
  total kWh of the month instead is not modelled.
- The capacity charge is for a whole month; a part month (moving in or out) is not prorated.
- The Norgespris monthly cap was moved into phase 1 (user story 6) because it decides the price per kWh of a holiday
  home.
- The phase 1 calculation (user stories 1 to 5) is built. User story 6 was added on 2026-11-17 after the calculation was
  checked against a real bill (October 2026). That bill confirms an electricity tax of 7.13 ore and an Enova fee of
  1.00 ore per kWh excluding VAT, the same for day and night.
- Prices are shown including VAT, as the bill shows them. Because costs were energy only and excluding VAT before, every existing figure rises on the first day of use. The frontend only needs the totals to keep working, and its labels must say that prices include VAT.
- No supplier markup applies to a Norgespris price. This is to be confirmed against the supplier terms.
- The electricity tax is 7.13 ore per kWh and the Enova fee is 1 ore per kWh for 2026; both change over time, which is why they are dated values.
- Each location has one grid tariff of the same kind: a day rate and a night and weekend rate. Which grid company and tariff each location has, the supplier markup values, and whether the tariff steps in phase 2 are per month or per year are open and are entered as data, not fixed in the design.
- The exemptions apply to households. All locations are households, so business rates (the reduced electricity tax for
  businesses) are out of scope.
- The caps are seeded as 5,000 kWh (permanent home) and 1,000 kWh (holiday home) from the start of Norgespris.
- The grid rates are entered excluding taxes and VAT. A grid company that publishes its rates including taxes needs them converted by the Admin when entering them.
- The spot price, the exchange rate and the Norgespris rates from feature 007 are used as they are, except that the tax field of a Norgespris row is no longer used: the electricity tax and the Enova fee are added separately, so counting it would count taxes twice. It stays in the table for history and should be left at 0.
- The frontend (`no.sanddata.ams.frontend`) keeps working unchanged because it only reads the existing rate and cost fields, which now hold the totals including VAT. Its own changes come in a separate feature after this one.
- The saving is the difference at full price, so it includes the supplier markup on the spot side, the same grid charge, taxes and VAT on both sides, and therefore equals (spot energy + markup - Norgespris rate) x (1 + VAT) x kWh per hour. It depends on the unconfirmed assumption that no markup applies under Norgespris.
- Above the Norgespris cap, both sides of the saving are on spot terms, so the formula above holds only for the kWh up to
  the cap.
- Elvia publishes the energy charge including the electricity tax, the Enova fee and VAT, and the capacity steps
  including VAT. The Admin enters them excluding all of these: energy charge = published / 1.25 - 0.0713 - 0.0100, step
  amount = published / 1.25. Elvia's rules are the same for permanent homes and holiday homes.
- With fewer than three days of peaks, the average is over the peaks there are (decided by the product owner on
  2026-11-17). Elvia's sheet does not say how a value exactly on a step border is handled; the rule in user story 7
  (the higher step) is an assumption to check with Elvia.
- Out of scope: any change in the web app. This feature only makes the numbers right and available from the API (the totals, the parts and the saving). Showing the full price, its parts and the saving in the UI, and changing the price labels to say including VAT, is a later feature, listed in specs/_backlog.

## Reference data for the tests

The tests use Elvia's standard household tariff (tariffblad 1.0) and an Elvia bill for 01.10.2026 - 01.11.2026, so that
the hand-calculated examples (SC-001, SC-008) can be compared with a real bill. All stored values are excluding the
electricity tax, the Enova fee and VAT. National values for 2026: electricity tax 0.0713, Enova fee 0.0100 NOK per kWh,
VAT 25 percent.

**Energy charge** (NOK per kWh). Elvia publishes it including all taxes and VAT; stored = published / 1.25 - 0.0813.

| Valid from | Day, published | Night and weekend, published | Day, stored | Night and weekend, stored |
|---|---|---|---|---|
| 2026-01-01 | 36.40 ore | 26.40 ore | 0.2099 | 0.1299 |
| 2026-07-01 | 46.40 ore | 31.40 ore | 0.2899 | 0.1699 |

**Capacity steps** (NOK per month). Elvia publishes them including VAT; stored = published / 1.25.

| Step | kW | 2026-01-01 published | 2026-01-01 stored | 2026-07-01 published | 2026-07-01 stored |
|---|---|---|---|---|---|
| 1 | 0-2 | 125 | 100 | 150 | 120 |
| 2 | 2-5 | 190 | 152 | 250 | 200 |
| 3 | 5-10 | 300 | 240 | 420 | 336 |
| 4 | 10-15 | 410 | 328 | 585 | 468 |
| 5 | 15-20 | 520 | 416 | 755 | 604 |
| 6 | 20-25 | 630 | 504 | 925 | 740 |
| 7 | 25-50 | 1,175 | 940 | 1,760 | 1,408 |
| 8 | 50-75 | 1,720 | 1,376 | 2,600 | 2,080 |
| 9 | 75-100 | 2,270 | 1,816 | 3,440 | 2,752 |
| 10 | over 100 | 4,570 | 3,656 | 6,800 | 5,440 |

**Bill lines, October 2026** (a holiday home on Norgespris; the line amounts include VAT):

| Line | kWh | Rate on the bill | Amount | Check |
|---|---|---|---|---|
| Energiledd hverdag 06-22 | 357.62 | 36.24 ore | 129.60 | 357.62 x 0.2899 x 1.25 = 129.59 |
| Energiledd natt og helg | 641.72 | 21.24 ore | 136.29 | 641.72 x 0.1699 x 1.25 |
| Elavgift (day kWh) | 357.62 | 8.91 ore | 31.88 | 357.62 x 0.0713 x 1.25 = 31.87 |
| Energifondet (day kWh) | 357.62 | 1.25 ore | 4.47 | 357.62 x 0.0100 x 1.25 |
| Elavgift (night kWh) | 641.72 | 8.91 ore | 57.19 | 641.72 x 0.0713 x 1.25 |
| Energifondet (night kWh) | 641.72 | 1.25 ore | 8.03 | 641.72 x 0.0100 x 1.25 = 8.02 |
| Fastledd 5-10 kW | 31 days | 420.00 kr/month | 420.00 | step 3, 336 x 1.25 |
| Norgespris for fritidsbolig | -999.35 | 1.28 kr | -1,283.18 | credit from the grid company, 0 percent VAT |

The calculation is at full precision, so a test compares with the bill lines within 0.01 kr per line (three lines
differ by 0.01 kr through the bill's rounding). The holiday home used 999.35 kWh, just under its 1,000 kWh cap; a
test with the same month plus extra consumption covers the cap.
