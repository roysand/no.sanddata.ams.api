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

### Edge Cases

- **Time zone and weekday.** Day or night and weekday are decided from the Oslo time of the hour, never UTC. The hour that starts at 22:00 Oslo time is night; the hour that starts at 06:00 is day.
- **Public holidays.** Fixed holidays (1 January, 1 May, 17 May, 25 and 26 December) and the Easter-based ones (Maundy Thursday, Good Friday, Easter Monday, Ascension Day, Whit Monday) count as weekend. Easter Sunday and Whit Sunday are Sundays anyway.
- **A rate starts in the middle of a day.** Rates start on a date at midnight Oslo time, so a day never mixes two values of the same kind.
- **Norgespris location, spot comparison.** The comparison uses the supplier markup, because the comparison is what the household would pay on spot terms.
- **Rounding.** Amounts are carried at full precision and only shown rounded; the parts and the total never differ by a rounding gap.
- **Missing exchange rate or spot price.** Handled as before: the energy part is unavailable and so is the total.
- **VAT exemption areas.** Not modelled in phase 1; the VAT percentage applies to every location.
- **Monthly charges.** The supplier monthly fee, the grid fixed fee and the capacity charge are not in any hourly price in this phase, so the daily and hourly totals are lower than the monthly bill by those fixed amounts.
- **Norgespris cap.** The monthly limit of 5,000 kWh (1,000 kWh for a holiday home) is not applied in this phase; all consumption of a Norgespris location is priced at the Norgespris rate.

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

### Key Entities

- **Grid tariff**: A tariff of a grid company that a location can use: a name, and dated energy charges (day rate and night and weekend rate, NOK per kWh excluding taxes and VAT).
- **Electricity tax rate**: Dated electricity tax (forbruksavgift) in NOK per kWh.
- **Enova fee rate**: Dated Enova fee in NOK per kWh.
- **VAT rate**: Dated VAT percentage.
- **Supplier terms**: Per location, the supplier markup in NOK per kWh (the supplier monthly fee is phase 2).
- **Price breakdown**: The result of pricing one hour: energy, markup, grid energy charge, taxes, VAT, total per kWh, and a mark for parts that are unavailable or not set.
- **Norgespris saving**: For a location and a period: the saving (spot-terms cost minus Norgespris-terms cost, possibly negative), the number of hours counted and the number of hours with consumption.
- **Location**: Gains a grid tariff and supplier terms; the Norgespris agreement flag and price zone are unchanged.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For a set of hand-calculated example hours (weekday day, weekday night, Saturday, Sunday, public holiday, clock-change days, spot and Norgespris), 100% of calculated prices match the hand-calculated values.
- **SC-002**: The current hour, hourly, daily and day-price figures for the same hours agree in 100% of tested cases, and every daily total equals the sum of its hours.
- **SC-003**: After a rate with a future start date is added, 100% of hours before the date keep their old price and 100% of hours from the date use the new one, including the hours around midnight on the start date.
- **SC-004**: A household can compare the per-kWh part of its electricity bill with the app, and the difference is explained only by the monthly charges that phase 2 adds.
- **SC-005**: No regular user can read or change any of the new settings or rates.
- **SC-006**: Showing a month of hourly costs takes no noticeably longer than before and the number of rate lookups does not grow with the number of hours shown.
- **SC-007**: For a period with hours on both sides of the Norgespris rate, the saving shown equals the hand-calculated sum of the hourly differences in 100% of tested cases, and the shorter periods add up to the longer ones.

## Assumptions

- Phase 1 covers only per-kWh and per-hour parts. The supplier monthly fee, the grid fixed fee (in steps by the total kWh of the month or by peak kW) and the Norgespris monthly cap are phase 2.
- Prices are shown including VAT, as the bill shows them. Because costs were energy only and excluding VAT before, every existing figure rises on the first day of use. The frontend only needs the totals to keep working, and its labels must say that prices include VAT.
- No supplier markup applies to a Norgespris price. This is to be confirmed against the supplier terms.
- The electricity tax is 7.13 ore per kWh and the Enova fee is 1 ore per kWh for 2026; both change over time, which is why they are dated values.
- Each location has one grid tariff of the same kind: a day rate and a night and weekend rate. Which grid company and tariff each location has, the supplier markup values, and whether the tariff steps in phase 2 are per month or per year are open and are entered as data, not fixed in the design.
- All locations use VAT at the normal percentage; the VAT and electricity tax exemptions in parts of northern Norway are out of scope.
- The grid rates are entered excluding taxes and VAT. A grid company that publishes its rates including taxes needs them converted by the Admin when entering them.
- The spot price, the exchange rate and the Norgespris rates from feature 007 are used as they are, except that the tax field of a Norgespris row is no longer used: the electricity tax and the Enova fee are added separately, so counting it would count taxes twice. It stays in the table for history and should be left at 0.
- The frontend (`no.sanddata.ams.frontend`) keeps working unchanged because it only reads the existing rate and cost fields, which now hold the totals including VAT. Its own changes come in a separate feature after this one.
- The saving is the difference at full price, so it includes the supplier markup on the spot side, the same grid charge, taxes and VAT on both sides, and therefore equals (spot energy + markup - Norgespris rate) x (1 + VAT) x kWh per hour. It depends on the unconfirmed assumption that no markup applies under Norgespris.
- Until phase 2, the Norgespris monthly cap is not applied, so a saving for a location above 5,000 kWh a month (1,000 kWh for a holiday home) is overstated for the kWh above the cap.
- Out of scope: any change in the web app. This feature only makes the numbers right and available from the API (the totals, the parts and the saving). Showing the full price, its parts and the saving in the UI, and changing the price labels to say including VAT, is a later feature, listed in specs/_backlog.
