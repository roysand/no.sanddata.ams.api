# Feature Specification: The Norgespris Limit and the Spot Energy Price for the App

**Feature Branch**: `feature/018-norgespris-limit-status`

**Created**: 2026-11-22

**Status**: Draft

**Input**: From the frontend session, for the frontend's spec 015 (Norgespris limit warnings). Roy has Norgespris at
both Hjemme and the cabin. The cabin, a holiday home with a monthly limit of 1 000 kWh, has been over the limit; Hjemme
(5 000 kWh) has not. Above the limit, every kWh is priced at spot terms, so the app should warn:

- when a location is approaching its limit, from 90 % of it
- while a location is over its limit, until the limit resets on the 1st of the next month
- about expensive hours tomorrow, only for a location that is over or projected over its limit. An hour is expensive
  when its spot energy price is at least 4 × the Norgespris price, both including VAT and without grid fees, taxes or
  the supplier markup (2,00 kr at a Norgespris price of 0,50 kr).

Each warning is shown for **every** location the user can see, not only the one selected in the app.

Today the app cannot do this from what the API sends:

- The **monthly limit** is never sent. The frontend works it out from the bill's `NorgesPrisCredit` line, which only
  shows it once the month is already projected over the limit (frontend spec 012, research R7).
- The **Norgespris price** is only on the Admin endpoints (`GET /api/admin/norgespris`).
- The **spot energy price** is not sent. `GET /api/electricity-cost/prices` gives full rates with every fee and VAT.
  For a Norgespris location under its limit, the spot energy price could be worked out as Norgespris price + (spot
  rate − rate), since every fee is the same on both sides. That breaks once the location is over its limit, because
  the rate then **is** the spot rate, and that is exactly when the warning is needed.
- Reading the bill for every location just to find the month's kWh would be one heavy request per location on every
  page.

Chosen with Roy on 2026-11-22: start with the API, then specify the frontend.

## User Scenarios & Testing _(mandatory)_

### User Story 1 - The Norgespris limit status of every location (Priority: P1)

The app asks once for the Norgespris status of every location the user can see. For each location with a Norgespris
agreement, it gets the month's limit, the kWh used so far this month, the kWh projected for the whole month, the
Norgespris price including VAT, and when the limit resets. From that, the app warns about approaching and passing the
limit for every location, on every page.

**Why this priority**: Two of the three warnings need nothing else, and the third needs it to know when to show.

**Independent Test**: As Roy, ask for the status.

- The cabin is listed with a limit of 1 000 kWh, and Hjemme with 5 000 kWh.
- Each location's kWh so far equals the current month's bill's metered kWh, and its projected kWh equals the bill's
  projected total, within 0,01 kWh.
- The Norgespris price equals the rate in effect today on the Admin Norgespris page, including VAT.
- The reset time is the 1st of next month at 00:00 Oslo time.

**Acceptance Scenarios**:

1. **Given** a user with access to several locations, **When** they ask, **Then** the answer has one entry per active
   location they can see that has a Norgespris agreement, and none for locations without one.
2. **Given** a location that is a holiday home, **When** it is listed, **Then** its limit is the holiday-home limit;
   otherwise it is the home limit. Both are the values the cost calculation uses (`NorgesPrisCapHolidayHome` and
   `NorgesPrisCapHome`) for the current month.
3. **Given** the month so far, **When** a location is listed, **Then** its kWh so far are the metered kWh of the
   current Oslo month up to now, counted the same way as the Norgespris limit in the cost calculation.
4. **Given** a location that counts its car charger with the house (spec 016's setting on), **When** it is listed,
   **Then** the car's kWh are added to its kWh so far and its projection: the setting means the charger is on the
   house's agreement, so its kWh count toward the house's Norgespris limit. With the setting off, the charger is on its
   own agreement and its kWh are not counted.
5. **Given** the current month, **When** a location is listed, **Then** its projected kWh are the month's kWh so far
   plus the projected rest of the month, the same figure as the current month's bill.
6. **Given** the kWh so far at or above the limit, **When** the location is listed, **Then** it says it is over the
   limit and by how many kWh.
7. **Given** a location without readings this month, **When** it is listed, **Then** its kWh so far are 0 and its
   projection is null. It is not an error.
8. **Given** a user with no Norgespris locations, **When** they ask, **Then** the answer is an empty list.

---

### User Story 2 - The spot energy price per hour (Priority: P1)

The app's day prices also give each hour's spot energy price including VAT, without grid fees, electricity tax, the
Enova fee or the supplier markup. The app can then compare tomorrow's hours with 4 × the Norgespris price, also for a
location that is already over its limit.

**Why this priority**: The expensive-hours warning cannot be computed correctly without it once a location is over its
limit.

**Independent Test**: For the cabin, ask for tomorrow's prices once they are published. Each hour's spot energy price
equals the day-ahead price for the cabin's zone, converted to NOK and with VAT added, within 0,0001 kr. It is the same
whatever the location's pricing model and whether it is over its limit.

**Acceptance Scenarios**:

1. **Given** an hour with a published price and exchange rate, **When** prices are read, **Then** the hour has its
   spot energy price in NOK per kWh including VAT (none in a VAT-exempt zone, as the cost calculation does).
2. **Given** an hour without a price or exchange rate yet, **When** prices are read, **Then** its spot energy price is
   null, like the other rates.
3. **Given** a negative spot price, **When** it is read, **Then** it is sent as negative.
4. **Given** the existing fields, **When** prices are read, **Then** `ratePerKwh` and `spotRatePerKwh` are unchanged.

---

### User Story 3 - Only people who may see the location (Priority: P1)

The status and the prices are part of a location's use and cost, so they are visible to exactly those who may see the
location: the owner, viewers and Admins.

**Independent Test**: As a viewer of the cabin only, ask for the status: only the cabin is listed. As an Admin, every
active Norgespris location the Admin can see in the app is listed.

**Acceptance Scenarios**:

1. **Given** an owner or viewer, **When** they ask, **Then** they get only the locations they have access to.
2. **Given** an Admin, **When** they ask, **Then** they get the same locations the app's location list gives them
   (`GET /api/locations`), so the warnings match the locations they can switch to.
3. **Given** no sign-in, **When** someone asks, **Then** the request is refused as unauthenticated.

---

### Edge Cases

- **The first hours of a month**: the kWh so far start again at 0 at 00:00 Oslo time on the 1st, and the reset time
  moves to the next month.
- **The running hour**: the kWh so far include the running hour's readings up to now, as the cost calculation counts
  them for the cap.
- **A Norgespris rate change on the 1st** (expected on 1 January): the price is the rate in effect now. A rate that
  starts later is not used until its day.
- **A limit change**: the limit is the one in effect for the current month.
- **A location that changes Norgespris agreement mid-month**: it is listed from the moment it has the agreement, with
  the month's kWh so far.
- **An inactive location**: not listed. It receives no readings, so it needs no warning.
- **Clock changes**: the month is an Oslo calendar month, as in the bill.

## Requirements _(mandatory)_

### Functional Requirements

- **FR-001**: The system MUST answer, in one request, the Norgespris status of every active location the user can see
  that has a Norgespris agreement.
- **FR-002**: Each entry MUST give the location id, the month (`yyyy-MM`), the limit in kWh, the kWh so far this
  month, the projected kWh for the month (null without a projection), the Norgespris price per kWh including VAT,
  and the reset time (UTC instant of the 1st of next month 00:00 Oslo time).
- **FR-003**: The limit, the kWh so far and the projection MUST be computed exactly as the cost calculation and the
  bill compute them, so the app's warnings agree with the bill and the prices.
- **FR-004**: The car charger's kWh MUST be counted in the kWh so far and the projection exactly when the location
  counts the car with the house (spec 016's setting), and MUST NOT be counted otherwise. The Norgespris limit in the
  cost calculation, the prices and the bill MUST follow the same rule, so they agree with the status: with the setting
  on, the house reaches its limit sooner, and the kWh after the limit are priced at spot terms.
- **FR-005**: `GET /api/electricity-cost/prices` MUST add, for each hour, the spot energy price per kWh including VAT
  and nothing else, or null when its price or exchange rate is missing.
- **FR-006**: No existing field MAY change meaning; the new fields are additions. The one change in behaviour is
  FR-004: for a location that counts its car with the house, the Norgespris limit now includes the car's kWh, which
  changes spec 016's rule that the car never affects a cost.
- **FR-007**: Access MUST follow the location's rules: owner, viewer and Admin, as `GET /api/locations` lists them.
- **FR-008**: The status MUST be answered fast enough to be read on every page load and refreshed every few minutes
  (see SC-003).

### Key Entities

- **Norgespris limit status**: one per location with a Norgespris agreement: location, month, limit, kWh so far,
  projected kWh, Norgespris price including VAT, and reset time. Derived from readings, rates and the bill's
  projection; nothing new is stored.
- **Spot energy price**: one per hour of a day, alongside the existing rates. Derived from the stored day-ahead prices
  and exchange rates.

## Success Criteria _(mandatory)_

### Measurable Outcomes

- **SC-001**: For every Norgespris location, the kWh so far and the projected kWh equal the current month's bill within
  0,01 kWh.
- **SC-002**: The spot energy price equals the day-ahead price × exchange rate × (1 + VAT) within 0,0001 kr for every
  hour of a day.
- **SC-003**: The status for a user with 3 locations is answered in under 300 ms at the 95th percentile on the
  production server.
- **SC-004**: For the cabin over its limit, the status says it is over and by how many kWh, and tomorrow's prices give
  a spot energy price for every published hour.

## Assumptions

- The rules in the app (90 %, 4 × Norgespris, showing for every location, the banner's wording) stay in the frontend's
  spec 015. The API only gives the figures, so the rules can change without an API release.
- The app reads tomorrow's prices only for locations that are over or projected over their limit: usually none or one
  location, so no list of prices for every location is needed.
- The projection is the bill's: it already exists, and using it keeps the warning and the bill's expected total in
  agreement.
- The car charger has its own meter (Zaptec reads it), never the location's AMS meter, so adding its kWh never counts
  them twice. Roy confirmed on 2026-11-22 what the "count the car with the house" setting means: on, the charger is
  on the house's agreement and its kWh are the house's usage, also for the Norgespris limit; off, it is on its own
  agreement (at Hjemme: the garage's, shared by 12 garages, with Norgespris) and counts for nothing of the house's.
- The car's own cost (its kWh × the Norgespris price including VAT, shown on the bill whether the setting is on or
  off) is a frontend feature (frontend `backlog.md`, "Car charging cost"). It uses this spec's Norgespris price.
- No warning is sent outside the app: web push is out of scope (frontend `backlog.md`, "Warnings").
- A frontend spec 015 follows, which shows the three warnings in the existing banner.
