# Feature Specification: What the Car Charging Costs, on the Bill

**Feature Branch**: `feature/019-ev-charger-cost`

**Created**: 2026-11-22

**Status**: Draft

**Input**: From the frontend session, for the frontend's "Car charging cost" (frontend `backlog.md`). Spec 016 added
the Zaptec car charger's kWh to the bill as `evCharger`, never priced. Roy wants the bill to show what the car charging
costs, as its own figure.

Roy's car charger at Hjemme has its own meter in the garage. Spec 016's setting "count the car charger with the house"
says which agreement it is on (confirmed by Roy on 2026-11-22, see spec 018's assumptions):

- **On**: the charger is on the house's agreement. Its kWh are the house's usage, and since spec 018 they count toward
  the house's Norgespris limit.
- **Off**: the charger is on its own agreement. At Hjemme that is the garages' agreement, shared by 12 garages, with
  Norgespris.

Chosen with Roy on 2026-11-22:

- The cost is shown on the **bill only**, not on the Car tab, the hourly or the daily figures.
- It is the hours **so far**, like the car's kWh on the bill: measured, never projected.
- It **adds to no sum**: the bill's lines and total stay the house's, as spec 016 left them.
- **Counted with the house**: the car's kWh cost what a kWh costs the house in the same hour.
- **Not counted with the house**: the car's kWh cost the **Norgespris price including VAT**, energy only: no grid fees,
  no taxes other than VAT, no markup. This is the garages' agreement today, and Roy expects it **may change**, so the
  rule is kept in one place.

## User Scenarios & Testing _(mandatory)_

### User Story 1 - The car's cost when it is not counted with the house (Priority: P1)

For a location whose car is on its own agreement, the bill's car part gives what its charging has cost so far this
month: each hour's car kWh × the Norgespris price including VAT in effect in that hour.

**Why this priority**: It is Hjemme's case today: the car is on the garages' agreement, and the cost is the figure Roy
asked for.

**Independent Test**: As Roy, with Hjemme's setting off, read the current month's bill. The car part's cost equals the
car's kWh so far × the Norgespris price including VAT on the Admin Norgespris page, within 0,01 kr.

**Acceptance Scenarios**:

1. **Given** the setting off and car kWh in the month, **When** the bill is read, **Then** the car part has a cost
   equal to the sum over its hours of the hour's car kWh × the Norgespris price including VAT in effect in that hour.
2. **Given** a Norgespris rate change during the month, **When** the bill is read, **Then** each hour uses the rate
   in effect in that hour.
3. **Given** a VAT-exempt zone, **When** the bill is read, **Then** the Norgespris price has no VAT added, as the cost
   calculation does elsewhere.
4. **Given** an hour with no Norgespris rate in effect, **When** the bill is read, **Then** the car part's cost is
   null, and the rest of the car part is unchanged.
5. **Given** the location's own pricing model (spot or Norgespris) and whether it is over its own limit, **When** the
   bill is read, **Then** neither changes the car's cost: the car is not on the house's agreement.

---

### User Story 2 - The car's cost when it is counted with the house (Priority: P1)

For a location whose car is on the house's agreement, the car part gives what its kWh have cost at the house's own
price per kWh: in each hour, what the cost calculation charges the house for a kWh in that hour.

**Why this priority**: Same figure, the other setting. With the setting on, the Norgespris price would understate the
car's cost once the house is over its limit.

**Independent Test**: As Roy, with Hjemme's setting on, read the current month's bill. For an hour with car kWh, the
car's cost in that hour equals its kWh × that hour's price per kWh for the house, which is the rate
`GET /api/electricity-cost/prices` gives for the hour, within 0,01 kr.

**Acceptance Scenarios**:

1. **Given** the setting on, **When** the bill is read, **Then** the car part's cost is the sum over its hours of the
   hour's car kWh × the house's price per kWh in that hour: the energy price under the house's pricing model, the grid
   energy charge, the taxes per kWh and VAT, as the cost calculation prices a house kWh.
2. **Given** a Norgespris house, **When** the car's kWh take the house over its limit (spec 018 FR-004), **Then** the
   car's kWh after the limit are priced at spot terms, as the house's own kWh are.
3. **Given** the capacity charge and the monthly fixed fees, **When** the car's cost is worked out, **Then** neither is
   part of it: they are the house's and don't grow per kWh.
4. **Given** an hour where the car charged but the house has no reading (spec 016's `hoursWithoutMeterReading`),
   **When** the bill is read, **Then** that hour is still priced at the house's price per kWh for the hour.
5. **Given** an hour whose price or exchange rate is missing, **When** the bill is read, **Then** the car part's cost
   is null.

---

### User Story 3 - The bill says how the cost was worked out (Priority: P2)

The car part says which rule priced it: at the house's price or at the Norgespris price. The app can then say so next
to the cost, and a later change to the garages' rule shows up as a new value instead of a silent change.

**Why this priority**: The app already shows whether the car is counted with the house; this keeps the cost's meaning
explicit if the rule changes.

**Acceptance Scenarios**:

1. **Given** the setting on, **When** the bill is read, **Then** the car part says it is priced at the house's price.
2. **Given** the setting off, **When** the bill is read, **Then** it says it is priced at the Norgespris price.

---

### User Story 4 - Only people who may see the location (Priority: P1)

The cost is part of the bill, so it is visible to exactly those who may read the bill today: the owner, viewers and
Admins.

**Acceptance Scenarios**:

1. **Given** a viewer of the location, **When** they read the bill, **Then** they see the car's cost like the owner.
2. **Given** no access to the location, **When** someone asks for its bill, **Then** the request is refused as today.

---

### Edge Cases

- **No charging in the month**: the car part stays null, as in spec 016; no zero cost is invented.
- **A past month**: the same rules, with the rates in effect in each hour. Past months use the setting as it is when
  the bill is read, as spec 016's car part already does.
- **The setting changed mid-month**: the whole month is priced by the current setting, the same rule the car's kWh
  and `included` follow today.
- **Estimated car kWh** (spec 016's `estimated`): priced the same way; the part still says the kWh are estimated.
- **The running hour**: included as the car's kWh so far are.
- **Negative spot prices**: with the setting on, a negative energy price lowers the car's cost, as it does the house's.
- **Clock changes**: hours are UTC hours, months Oslo months, as in the bill.

## Requirements _(mandatory)_

### Functional Requirements

- **FR-001**: The bill's car part (`evCharger` on `GET /api/electricity-cost/bill`) MUST add the cost of the car's
  kWh so far, in NOK including VAT, or null when any of its hours cannot be priced.
- **FR-002**: With the location's "count the car with the house" setting off, each hour MUST be priced at the
  Norgespris price including VAT in effect in that hour, energy only.
- **FR-003**: With the setting on, each hour MUST be priced at the house's price per kWh in that hour, exactly as the
  cost calculation prices a house kWh, including where the Norgespris limit falls. No capacity charge and no monthly
  fees.
- **FR-004**: The car part MUST say which rule priced it (house price or Norgespris price).
- **FR-005**: The rule for a car not counted with the house MUST be in one place, so it can be changed (for example to
  the garages' own agreement) without touching the bill's other figures.
- **FR-006**: The cost MUST NOT be added to any bill line, part or total, and MUST NOT be projected.
- **FR-007**: No existing field MAY change meaning; the new fields are additions. An app without this spec reads the
  bill as before.
- **FR-008**: Access follows the bill's existing rules.

### Key Entities

- **Car charging cost**: on the bill's car part: the cost so far in NOK including VAT, and the rule that priced it.
  Derived from the car's hourly kWh, the setting and the rates; nothing new is stored.

## Success Criteria _(mandatory)_

### Measurable Outcomes

- **SC-001**: With the setting off, the car's cost equals Σ (hour's car kWh × Norgespris price incl. VAT) within
  0,01 kr for the current and the previous month at Hjemme.
- **SC-002**: With the setting on, it equals Σ (hour's car kWh × that hour's `ratePerKwh` from the prices endpoint)
  within 0,01 kr, for a month where the house stays within its limit.
- **SC-003**: The bill's lines and total are unchanged by this spec, to the øre, for every month.
- **SC-004**: The bill is answered no slower than today at the 95th percentile (within 10 %).

## Assumptions

- The car charger has its own meter (Zaptec), never the location's AMS meter, so pricing its kWh never prices a kWh
  twice.
- "The house's price per kWh" is the same per-kWh price the prices endpoint and the cost calculation use for the
  location: energy under its pricing model, the grid energy charge, the consumption tax, the Enova fee and VAT.
- The garages' agreement is Norgespris today; if it changes (FR-005), a later spec changes the rule.
- How the app shows the cost (beside the car's kWh in the bill's car note, on the Now card and the History bill) is
  the frontend's spec 016, which follows this one.
