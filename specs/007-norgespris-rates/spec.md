# Feature Specification: Norgespris Rates with Start Dates

**Feature Branch**: `feature/007-norgespris-rates`

**Created**: 2026-11-13

**Status**: Draft

**Input**: User description: "Admin-managed Norgespris rates with start dates. The Norgespris flat rate is fixed configuration today. From 1 January 2027 it will most probably change, so it must become data an Admin can manage. An Admin can list all Norgespris rates and add a new rate with a start date, a rate per kWh and a tax per kWh. The rate that applies to an hour is the one with the latest start date on or before that hour's Norwegian local date, so past hours keep the old price and hours from the start date use the new one. The rate is national. The first rate is seeded from the current configuration so existing costs do not change. A rate that starts in the past may be added only if it is the newest, with a warning that past costs are recalculated. A rate that has not started yet may be deleted; rates in effect cannot be edited or deleted. Scope includes a small Admin page in the frontend repo (`no.sanddata.ams.frontend`)."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Costs use the rate that applied at the time (Priority: P1)

Cost figures for a location with a Norgespris agreement, and the Norgespris comparison figure for a location without one, use the rate that was valid for each hour, not one fixed number. Before a new rate starts, every hour is priced as it is today. From the new rate's start date, hours are priced with the new rate, and hours before it keep the old one.

**Why this priority**: This is the reason for the feature. Without it a rate change on 1 January would either not happen or would silently rewrite every past cost figure.

**Independent Test**: With two rates (the seeded one and one starting on a later date), request the hourly cost for an hour before the start date and one after it, for a Norgespris location, and confirm each uses its own rate. Confirm the figures for hours before the later start are identical to before the feature.

**Acceptance Scenarios**:

1. **Given** the seeded rate only, **When** costs are requested for any period, **Then** every figure is identical to what it was before this feature.
2. **Given** a rate A and a later rate B starting on date D, **When** costs are requested for an hour whose Norwegian local date is before D, **Then** rate A is used.
3. **Given** the same rates, **When** costs are requested for an hour whose Norwegian local date is D or later, **Then** rate B is used.
4. **Given** a start date of 1 January, **When** the hour 23:00-24:00 on 31 December Norwegian time is priced, **Then** it uses the old rate and the hour from 00:00 on 1 January Norwegian time uses the new one, even though the UTC date differs.
5. **Given** a period that crosses the start date, **When** daily or hourly figures are requested, **Then** each hour uses the rate valid for it and the totals add up the correct mix.
6. **Given** a location without a Norgespris agreement, **When** its comparison figure is shown, **Then** the comparison uses the rate valid for that hour.
7. **Given** the day-prices view of a Norgespris location, **When** a day is shown, **Then** the flat rate shown for each hour is the one valid for that day.
8. **Given** a rate with a tax per kWh, **When** an hour is priced, **Then** the price used is the rate plus the tax, as today.

---

### User Story 2 - An Admin adds a new rate in advance (Priority: P1)

An Admin adds a new Norgespris rate with a start date, a rate per kWh and a tax per kWh, ahead of the date it takes effect. Adding it supersedes the previous rate from that date; there is no end date to set and no "current" flag to move. The Admin can see all rates, which of them is in effect today, and which are still in the future.

**Why this priority**: The Admin must be able to prepare the 1 January change before it happens, and without a code change or redeploy.

**Independent Test**: As Admin, list the rates (only the seeded one), add a rate starting on a future date, list again and see both with the seeded one marked as in effect, then request costs for an hour on each side of the start date.

**Acceptance Scenarios**:

1. **Given** an Admin and a start date after today, a rate and a tax, **When** they add the rate, **Then** it is saved and appears in the list as not yet started.
2. **Given** the list, **When** it is shown, **Then** rates are ordered by start date, and exactly one is marked as in effect today (the one with the latest start date on or before today).
3. **Given** a start date that already has a rate, **When** the Admin adds another with the same start date, **Then** it is refused with a clear message and nothing is saved.
4. **Given** a negative rate or negative tax, an empty or malformed start date, **When** the Admin tries to add it, **Then** it is refused with `400` and a message per field.
5. **Given** a regular user, **When** they try to list or add rates, **Then** it is refused with `403`; without a token, `401`.
6. **Given** the day the new rate starts, **When** today's date reaches it, **Then** it becomes the one in effect without any further action.

---

### User Story 3 - An Admin corrects a mistake (Priority: P2)

An Admin who entered a wrong rate can fix it, but only where that cannot silently rewrite history. A rate that has not started yet can be deleted and added again. A rate that starts in the past can be added only if it is the newest of all, so it only supersedes the latest rate; before saving, the Admin is warned that costs shown for past hours will be recalculated, because costs are calculated when shown and not stored.

**Why this priority**: Mistakes happen, but the first two stories already deliver the 1 January change. Rates in effect cannot be edited or deleted, so this story is the only safe way to correct one.

**Independent Test**: Add a future rate, delete it, and confirm it is gone and costs are unchanged. Try to delete the rate in effect and an older rate, and confirm both are refused. Add a rate dated in the past that is newer than all others after confirming the warning, and confirm past costs change; try one dated before the newest rate and confirm it is refused.

**Acceptance Scenarios**:

1. **Given** a rate whose start date is after today, **When** an Admin deletes it, **Then** it is removed and costs are unaffected.
2. **Given** the rate in effect today or any rate that has already started, **When** an Admin tries to delete it, **Then** it is refused with `409` and a clear message, and nothing changes.
3. **Given** a start date in the past that is later than every existing start date, **When** the Admin sends it with an explicit confirmation that past costs will be recalculated, **Then** the rate is saved and past hours from that date are priced with it.
4. **Given** a start date in the past that is earlier than the start date of any existing rate, **When** the Admin tries to add it, **Then** it is refused with a clear message.
5. **Given** a start date today or earlier and no confirmation in the request, **When** the Admin adds it, **Then** it is refused with a message that past costs will be recalculated and the request must be confirmed; a future date needs no confirmation.
6. **Given** the add screen, **When** the Admin picks a start date today or earlier, **Then** the warning is shown and the Admin must confirm it before the request is sent; for a future date it is not shown.
6. **Given** no edit action, **Then** there is no way to change the rate, tax or start date of an existing rate; a correction is made by deleting (if not started) or by adding a newer rate.
7. **Given** the only rate in the system, **When** an Admin tries to delete it, **Then** it is refused, because every hour must always have a rate.

---

### User Story 4 - An Admin page in the web app (Priority: P2)

In the web app an Admin opens a Norgespris page with a table of all rates (start date, rate per kWh, tax per kWh, total per kWh), the one in effect today highlighted, and future rates marked and deletable. An "Add rate" dialog takes start date, rate and tax, shows the recalculation warning for a past start date, and shows any refusal from the server in plain words. The page is visible only to Admins.

**Why this priority**: The Admin has no other way to use the endpoints without calling them by hand, but the API alone already delivers the capability.

**Independent Test**: Sign in as Admin, open the page, add a future rate and see it appear, delete it, then sign in as a regular user and confirm the page and its menu entry are not available.

**Acceptance Scenarios**:

1. **Given** an Admin, **When** they open the page, **Then** all rates are listed with the one in effect highlighted and future ones marked.
2. **Given** the dialog, **When** the Admin saves a valid rate, **Then** the table refreshes and shows it.
3. **Given** a rejected request, **When** the server answers with an error, **Then** its message is shown in the dialog and the dialog stays open.
4. **Given** a rate that has started, **Then** no delete action is offered for it.
5. **Given** a regular user, **Then** the menu entry is absent and opening the address directly does not show the page.

---

### Edge Cases

- **Time zone around midnight and daylight saving.** The valid rate is chosen by the Norwegian local date of the hour. On the days clocks change (23 or 25 hours long), every hour still gets exactly one rate and none is skipped or counted twice.
- **No rate valid for an hour.** An hour dated before the earliest start date has no rate. The seeded rate has an early enough start date that this does not occur for any real measurement. If it still happens, the figure is shown as unavailable, never as zero.
- **Two requests add rates at the same time with the same start date.** Only one is saved; the other is refused as a duplicate.
- **Start date is today.** It counts as in effect from 00:00 Norwegian time today, so it is treated as already started: it cannot be deleted and needs the recalculation warning.
- **The newest rate is deleted** (it had not started). The previous rate is then in effect again and nothing else changes.
- **Large rate values.** Values with more than four decimals or an unreasonably large amount are refused rather than rounded silently.
- **Restart or redeploy.** The rates survive; nothing about them lives in configuration after this feature.
- **A past-dated rate is added and costs change.** Figures shown for past hours change from then on; nothing else (stored measurements, spot prices) is touched.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST store Norgespris rates as data, each with a start date (a calendar date), a rate per kWh and a tax per kWh.
- **FR-002**: The system MUST determine the rate for an hour as the rate with the latest start date on or before that hour's Norwegian local date (Europe/Oslo, not UTC).
- **FR-003**: Every place that prices an hour with Norgespris (actual cost for locations with the agreement, comparison cost for locations without it, and the flat rate in the day-prices view) MUST use the rate valid for that hour.
- **FR-004**: The Norgespris rates MUST be the same for all price zones and all locations.
- **FR-005**: The system MUST create a first rate from the values currently in configuration, with a start date early enough to cover every stored measurement, so that all cost figures are unchanged by this feature.
- **FR-006**: An Admin MUST be able to list all rates, ordered by start date, with an indication of which one is in effect today and which have not started.
- **FR-007**: An Admin MUST be able to add a rate with a start date, a rate per kWh and a tax per kWh, and adding a later-dated rate MUST supersede the previous one from that date without an end date or a current flag.
- **FR-008**: The system MUST refuse a new rate whose start date equals the start date of an existing rate.
- **FR-009**: The system MUST refuse a rate or tax that is negative, has more than four decimals, is above 100, or is not a number, and a start date that is missing or not a valid date.
- **FR-010**: The system MUST accept a new rate with a start date in the past only if that start date is later than the start date of every existing rate, and MUST refuse it otherwise with a clear message.
- **FR-011**: The system MUST refuse a rate whose start date is today or earlier unless the request explicitly confirms that costs shown for past hours will be recalculated, so the rule holds for every caller; the web app MUST show the Admin that warning and send the confirmation only after the Admin accepts it.
- **FR-012**: An Admin MUST be able to delete a rate whose start date is after today.
- **FR-013**: The system MUST refuse to delete a rate that has started (including today's) and MUST refuse to delete the last remaining rate, with `409` and a clear message.
- **FR-014**: The system MUST NOT offer any way to change the rate, tax or start date of an existing rate.
- **FR-015**: Listing, adding and deleting rates MUST be limited to Admins: `401` without a sign-in, `403` for a signed-in user who is not an Admin.
- **FR-016**: Adding and deleting a rate MUST be logged with the acting user, the start date and the values, using the project's structured logging with stable reason codes.
- **FR-017**: A request that prices many hours (a day, a month) MUST load the rates once and not query them for each hour.
- **FR-018**: Once rates are data, the fixed Norgespris values MUST be removed from configuration so there is a single source of truth.
- **FR-019**: The web app MUST provide an Admin-only page that lists the rates with the one in effect highlighted and future ones marked, an add dialog with the past-date warning, and a delete action for rates that have not started, and MUST show the server's refusal messages.

### Key Entities

- **Norgespris rate**: A government-set flat price that starts on a calendar date. Attributes: start date (unique), rate per kWh, tax per kWh, when it was added and by whom. It has no end date; it is valid until a later-dated rate takes over. National, not tied to a location or price zone.
- **Location**: Unchanged. Its Norgespris-agreement flag still decides whether Norgespris is the actual or the comparison price; only the number it uses becomes date-dependent.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An Admin can add the 1 January rate in under one minute without a code change, a redeploy or direct database access.
- **SC-002**: After this feature is deployed and before any new rate is added, 100% of cost figures for existing data are identical to those before it.
- **SC-003**: After a new rate starts, 100% of hours before its start date keep their old price and 100% of hours from its start date use the new one, including the hours around midnight on the start date and on daylight-saving change days.
- **SC-004**: No regular user can list, add or delete a rate; every attempt is refused.
- **SC-005**: An Admin cannot, by any single action, change the price of an hour that has already passed without explicitly confirming that past costs will be recalculated.
- **SC-006**: Showing a month of hourly costs takes no noticeably longer than before, and the number of rate lookups does not grow with the number of hours shown.

## Assumptions

- The Norgespris rate is national, so one set of rates applies to every location and price zone (confirmed by Roy).
- A start date in the past is allowed only for the newest rate, with the recalculation warning (confirmed by Roy).
- Rates are in NOK per kWh, in the same unit the configuration uses today. Whether the tax is added to the rate or included in it follows today's behaviour: the price used is rate plus tax.
- The seeded rate's start date is early enough to cover every stored measurement (for example 1 January 2020 or the date of the earliest measurement); the exact date is a plan-level detail.
- There is no edit action by design: with delete-if-not-started and add-newer-only, every correction has a safe path and the audit trail stays simple.
- Only Admins manage rates. Owners and viewers see their costs but not the rates table.
- The frontend work lives in the separate repository `no.sanddata.ams.frontend` and follows its existing admin pages. This repository defines the API contract for it.
- The existing Admin-only location setting for the Norgespris agreement flag is unchanged.
- `specs/_backlog/` holds only "unique location names", which is unrelated to this feature, so nothing in it has to be done first.
