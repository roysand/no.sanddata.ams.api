# Feature Specification: Hot Water Used per Hour, Day and Month

**Feature Branch**: `feature/020-water-heater-consumption`

**Created**: 2026-11-24

**Status**: Draft

**Input**: User description: Roy showed the OSO app's "Consumption (V40)" chart and asked for the same chart in the
frontend's heater tab. The chart shows litres of 40 °C water (V40) used per hour of a day, with a Day / Week / Month /
Year switch and the period's total ("Total v40 used 317 L").

OSO's API has no consumption history. It gives only what the tank holds right now: the litres of 40 °C water
available (`availableMix40Volume`). Spec 011 samples the heater's power about once a minute, but not the litres. So the
API must start storing the litres, and work out the water used from how they fall. Heating raises the litres while
water is drawn, which hides part of the water used, so the heat added in the same minute is counted back in ("the
power correction"). Chosen with Roy on 2026-11-24: the API first, with the power correction. The frontend chart is a
separate spec in `no.sanddata.ams.frontend`.

## User Scenarios & Testing _(mandatory)_

### User Story 1 - Keep a record of the hot water in the tank (Priority: P1)

Every time the heater is sampled, the litres of 40 °C water in the tank are stored with the power. OSO keeps no
history, so this record is the only one there will be, and every day without it is lost for good.

**Why this priority**: Nothing else in this feature works without it, and history starts only when it is in place.
It can ship on its own and start collecting before the consumption answer is built.

**Independent Test**: For Hjemme, let the sampler run for 15 minutes and run a hot tap for a few minutes in the
middle. The stored samples show the litres falling while the tap ran and the power next to them, as before.

**Acceptance Scenarios**:

1. **Given** a connected heater at an active location, **When** it is sampled, **Then** the sample stores the litres
   of 40 °C water OSO reports, next to the power.
2. **Given** OSO answers the power but not the litres, **When** the heater is sampled, **Then** the power is still
   stored and the litres are missing for that sample. Reading the litres never costs a power sample.
3. **Given** samples stored before this feature, **When** they are read, **Then** they have no litres, and their power
   is unchanged.

---

### User Story 2 - Hot water used per hour, day or month for a period (Priority: P1)

A household member's app asks for the hot water used for a day, a week, a month or a year. It gets the litres of
40 °C water used in each part of the period (hours for a day, days for a week or a month, months for a year), how
much of each part was sampled, and the total for the period. The app can then draw the same bar chart as the OSO app.

**Why this priority**: This is what the chart needs. It is P1 with story 1, but it can come a few days later, once
there are real samples to check the rule against (see Assumptions).

**Independent Test**: For Hjemme, after a day of sampling with one 8-minute shower at about 07:00, ask for that day.

- The 07:00 hour shows about the litres the shower used, also while the heater was heating.
- Hours with no hot water drawn show 0 or close to it.
- The day's total is the sum of the hours, and is close to the OSO app's "Total v40 used" for the same day.

**Acceptance Scenarios**:

1. **Given** a period of a day, **When** it is read, **Then** there is one entry per hour of the Oslo day: 24, or 23
   and 25 on the days the clocks change.
2. **Given** a period of a week or a month, **When** it is read, **Then** there is one entry per Oslo day. A week runs
   Monday to Sunday, a month is the calendar month.
3. **Given** a period of a year, **When** it is read, **Then** there is one entry per calendar month.
4. **Given** each entry, **When** it is read, **Then** it gives its start, the litres of 40 °C water used, and how
   many minutes of it were sampled.
5. **Given** a minute in which the litres fell, **When** it is counted, **Then** it counts the fall plus the litres
   the heater made in that minute (the power correction).
6. **Given** a minute in which the litres rose or stayed the same, **When** it is counted, **Then** it counts 0.
7. **Given** a part of the period with no sampled time at all, **When** it is read, **Then** its litres are missing,
   never 0. A part with only some sampled time counts what was sampled, and its sampled minutes say so.
8. **Given** the period reaches past now, **When** it is read, **Then** the parts after now are left out, and the part
   going on now counts up to now.
9. **Given** a period from before the litres were sampled, or with no heater, **When** it is read, **Then** every
   part has missing litres and the total is missing. It is not an error.
10. **Given** the answer, **When** it is read, **Then** the period's total is the sum of its parts with litres, and it
    is missing only when every part is missing.
11. **Given** a request without a date, **When** it is read, **Then** the period is the one that holds today (Oslo).
    A period kind other than day, week, month or year, or a date that cannot be read, is refused with a message that
    says why.

---

### User Story 3 - Only people who may see the location (Priority: P1)

The hot water used is part of the location's use, so it is visible to exactly those who may see the location's use and
the heater's power (spec 017): the owner, viewers and Admins.

**Why this priority**: The same rule as every other figure for a location, and it must hold from the first release.

**Independent Test**: As a viewer of Hjemme, ask for today: it is answered. As a user without access, ask for it: the
answer is the same as for a location that does not exist.

**Acceptance Scenarios**:

1. **Given** an owner, a viewer or an Admin, **When** they ask, **Then** they get the answer.
2. **Given** a user with no access to the location, or a location that does not exist, **When** they ask, **Then**
   the answer is "not found", the same in both cases.
3. **Given** no sign-in, **When** someone asks, **Then** the request is refused as unauthenticated.

---

### Edge Cases

- **Gaps in sampling**: an hour is counted from its litres at the start and end and the heating in its sampled
  time. An hour without litres is not sampled and has no litres. When the hour before has no litres, the hour starts
  from its own first litres, so water drawn before that first sample is not counted.
- **OSO reports the same litres for several samples**: OSO updates its power and litres in steps every few minutes,
  not every minute (seen on 2026-11-25). Counting whole hours lets the steps cancel out. A step can still move a few
  litres across an hour's edge, but never out of the day.
- **Heating with no water drawn**: the litres rise by about what the heating makes, so the hour counts about 0. A
  small surplus or shortfall is covered by the floor (FR-004).
- **Water drawn while heating**: the litres fall less than the water used, or rise. The hour's heating, turned into
  litres, is added back, so the water is counted in both cases.
- **Standby losses**: the tank cools slowly and loses about 2–3 L of 40 °C water an hour with no water drawn. An hour
  under the floor counts 0.
- **The tank full at its maximum**: the litres cannot rise above the tank's maximum, so heating then doesn't show in
  them, and the balance can count water that wasn't drawn. The heater normally stops at its set point before that.
- **Heater removed or replaced**: samples belong to the location and are kept (spec 011), so past periods still
  answer. Reconnecting takes longer than 2 minutes in practice, so the two heaters' litres are not compared.
- **Inactive location**: no new samples; past periods still answer to those who may see it.
- **Clock changes**: hours, days and months follow the same Oslo-time rules as the rest of the figures (a 23- or
  25-hour day).

## Requirements _(mandatory)_

### Functional Requirements

- **FR-001**: Each heater sample MUST also store the litres of 40 °C water OSO reports at that time. If OSO does not
  give them, the sample MUST still store the power, with the litres missing.
- **FR-002**: Samples stored before this feature MUST keep their power unchanged and MUST have no litres. There is no
  backfill.
- **FR-003**: The system MUST answer, for one location, a period kind (day, week, month or year) and a date, the
  litres of 40 °C water used in each part of the period: per Oslo hour for a day, per Oslo day for a week (Monday to
  Sunday) or a month, per calendar month for a year.
- **FR-004**: Water used MUST be counted per Oslo hour (whole UTC hours) as an energy balance: the litres at the
  hour's start minus the litres at its end, plus the litres the heater made in the hour. Those are the heater's kWh in
  the hour (from the stored power, as spec 012 holds it) divided by the kWh it takes to make one litre of 40 °C water.
  An hour under a documented floor, negative ones included, counts 0. (Changed on 2026-11-25 from pairs of samples;
  see research R10.)
- **FR-005**: The kWh per litre of 40 °C water MUST be one documented figure, chosen in the plan and checked against
  real data (see Assumptions).
- **FR-006**: Each part MUST give its start, its litres (rounded to whole litres), and its sampled minutes. A part
  without any sample that has litres MUST have its litres missing, never 0. Parts after now MUST be left out.
- **FR-007**: The answer MUST give the period's total litres: the sum of its parts with litres, missing only when every
  part is missing.
- **FR-008**: Without a date the period MUST be the one that holds today in Oslo. A period kind other than day, week,
  month or year, or a date that cannot be read, MUST be refused with a validation message.
- **FR-009**: The answer MUST be visible to exactly those who may see the location's use (owner, viewer, Admin), with
  the same "not found" for a location that does not exist and one that is not visible.
- **FR-010**: No existing endpoint or field MAY change, and the heater's power figures (specs 012 and 017) MUST stay
  the same.

### Key Entities

- **Water heater sample** (spec 011, extended): time, power and now also the litres of 40 °C water in the tank, which
  may be missing.
- **Hot water use part**: start (an Oslo hour, day or month, given in UTC), litres of 40 °C water used, sampled
  minutes. Derived from the samples; nothing new is stored for it.
- **Hot water use period**: the kind (day, week, month, year), its start and end, its parts and its total litres.

## Success Criteria _(mandatory)_

### Measurable Outcomes

- **SC-001**: For a fully sampled day, the day's total is within 15 % of the OSO app's "Total v40 used" for the same
  day, on at least 4 of 5 days checked.
- **SC-002**: A shower of known length and flow, taken while the heater is heating, is counted within 20 % of its
  litres. Without the power correction it would be counted at least 10 % short.
- **SC-003**: An hour with no hot water drawn and no gap counts at most 2 litres.
- **SC-004**: A year's answer is given in under 1 second, and a day's answer in under 300 ms, at the 95th percentile on
  the production server.
- **SC-005**: Over a day of normal sampling, at least 95 % of the minutes have litres stored.

## Assumptions

- **History starts when the litres are first stored.** Week, month and year fill up over time. Shipping story 1 first
  starts the record a few days sooner.
- **The rule is checked against real data before the answer is final.** With a few days of stored litres, the plan
  compares the counted water with the OSO app's day totals, and settles three things from it: the kWh per litre of
  40 °C water, how small falls from standby losses are counted (for example a minimum fall), and what to do if OSO
  updates its litres less often than once a minute. The rule in FR-004 is the starting point.
- **The kWh per litre of 40 °C water** is about 0,035 kWh (one litre heated from 10 °C to 40 °C). OSO also gives the
  energy stored in the tank next to the litres, so the plan may derive the figure from OSO's own numbers instead.
- **The heater's element puts all its power into the water**, so the heater's kWh is the heat added. Standby power
  (about 0,01 kW) is too small to matter.
- **Litres are 40 °C water (V40)**, as the OSO app shows them, not litres of hot water from the tank.
- **The period kinds match the OSO app**: day, week, month, year. Other periods or steps are out of scope until a chart
  needs them.
- **The answer is litres only.** The energy and cost of heating are already given by spec 012. Showers and baths are
  out of scope.
- **The frontend chart is a separate spec** in `no.sanddata.ams.frontend`. Once this API is built, a backlog note in
  `specs/_backlog/` hands the endpoint over to it, as for specs 018 and 019.
