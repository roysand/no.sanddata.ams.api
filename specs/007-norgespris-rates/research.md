# Research: Norgespris Rates with Start Dates

All unknowns from the plan's Technical Context are resolved below; none remain.

## D1. Where the rate is stored, and its shape

**Decision**: a new table `NorgesPrisRate` (entity `NorgesPrisRate`): `Id`, `ValidFrom` (`DateOnly`, unique),
`RatePerKwh`, `TaxPerKwh`, `AddedByUserId`, plus the base `CreatedAt`/`UpdatedAt`.

**Rationale**: the spec wants rates an Admin can add without a redeploy and past hours to keep their old price. A start
date per row with "latest start on or before" needs no end date and no current flag, so adding a row can never leave
two "current" rows or a gap. `ExchangeRate` already stores a per-date value the same way.

**Alternatives**: a config list in `appsettings.json` (needs a redeploy and gives no Admin UI); a single editable
row (rewrites all history, the problem we are fixing); rows with `ValidFrom` and `ValidTo` (two columns to keep
consistent, overlaps and gaps to prevent).

## D2. Which date picks the rate

**Decision**: the Oslo calendar date of the hour's start instant. A pure function
`NorgesPrisSchedule.RateFor(hourStartUtc)` converts with `Europe/Oslo` and returns the last row whose `ValidFrom` is
on or before that date.

**Rationale**: Norgespris changes at local midnight. The hour that begins at 23:00 UTC on 31 December is 00:00 on
1 January in Oslo and must use the new rate. Per hour (not per day) means a 23- or 25-hour DST day needs no special
case: each hour belongs to exactly one Oslo date.

**Alternatives**: UTC date (wrong by one hour around midnight, rejected by the spec); date of the request (past hours
would change when viewed later).

## D3. Loading rates once per request

**Decision**: `CostCalculator` stays `AddScoped`. It keeps the rate list in a lazily loaded field
(`Task<IReadOnlyList<NorgesPrisRate>>`), so the first hour priced in a request loads all rows with a no-tracking query
and every later hour reuses them.

**Rationale**: the calculator is already scoped per request, and `GetDayPricesQueryHandler` and `HourlyCostProvider`
call it once per hour in a loop. A field on the scoped instance meets FR-017 with no cache layer, no invalidation and
no change to the handlers. Adding or deleting a rate happens in a different request, so no stale list is possible.

**Alternatives**: a query per hour (the problem); a singleton in-memory cache (needs invalidation across the add and
delete requests for no gain at this size); passing the list into each call from every handler (touches every caller).

## D4. Past-dated rate rule and the confirmation

**Decision**: the handler compares the new `ValidFrom` with today's Oslo date. If it is today or earlier it must be
later than the newest existing `ValidFrom`, else `409 NorgesPris.NotNewest`, and the request must carry
`confirmPastDate: true`, else `409 NorgesPris.ConfirmationRequired`. A start date in the future is accepted whatever the
other dates are (only a duplicate date is refused) and ignores the flag. The web app shows the "past costs will be
recalculated" warning and sends the flag only after the Admin accepts.

**Rationale**: Roy's rule is that the API holds every business rule, whatever client calls it. A rate dated before the
newest one would silently rewrite history that has already been shown and cannot be undone (no edit, no delete of
started rates). Newest-only past-dating only corrects the latest rate, and the explicit flag makes sure the caller knew
what it was doing. A warning only in the frontend would be skipped by a Scalar or curl call.

**Alternatives**: allow any past date with the warning (can rewrite old periods); no past dates at all (no way to fix
a wrong rate that has already started).

## D5. "Today" for the rules

**Decision**: inject `TimeProvider` (registered as `TimeProvider.System`) and take today's Oslo date from it.

**Rationale**: "has it started" and "is it in the past" depend on today, which unit tests must control. The handlers
today use `DateTime.UtcNow` directly; for the first logic that needs a controlled clock across midnight, the built-in
`TimeProvider` is the smallest honest seam and adds no package. Tests use a three-line `TimeProvider` subclass.

## D6. Seeding and removing the configuration

**Decision**: the migration inserts one row, `ValidFrom = 2020-01-01`, `RatePerKwh = 0.40`, `TaxPerKwh = 0`, a fixed
`Id`, and a null-equivalent `AddedByUserId` (`Guid.Empty`, shown as "seeded" nowhere). `FlatRateOptions`, its DI
registration and the `NorgesPris` section in `appsettings.json` are then removed in the same change.

**Rationale**: the production values are the defaults in `appsettings.json` (no environment override exists in the
compose files or docs; checked). A seed fixed in the migration cannot read configuration, so it uses those defaults.
`2020-01-01` is earlier than any possible measurement (the system started in 2026), so no hour is ever without a rate.
The migration is not edited after it has been applied, as the constitution requires.

**Alternatives**: seeding from configuration at startup (a second source of truth during a transition; startup code for
a one-off); a nullable first `ValidFrom` meaning "since forever" (special case in every comparison).

## D7. Deleting rates, and the "last rate" rule

**Decision**: delete only when `ValidFrom` is after today's Oslo date. Anything that has started, including today's
rate, gives `409 NorgesPris.AlreadyStarted`. There is no separate "last remaining rate" check.

**Rationale**: the seeded rate started in 2020, so it is never deletable. Because deleting is limited to rates that
have not started, a state with no started rate cannot be reached, so spec scenario 3.7 (the only rate cannot be
deleted) holds through the started rule alone. A second check would guard an impossible state (Constitution VIII).

## D8. Concurrent adds of the same date

**Decision**: the handler checks for an existing date first and returns `409 NorgesPris.DateExists`. The insert goes
through the repository method `TryInsertAsync`, which saves and, if the unique index on `ValidFrom` rejects it, checks
whether the date now exists and returns false; the handler reports that as `DateExists` too.

**Rationale**: the index guarantees only one of two simultaneous requests is saved, and the loser gets the same friendly
refusal instead of a server error, as the spec's edge case requires. The provider-specific exception stays inside
Infrastructure, so the handler has no EF Core dependency.

## D9. Endpoint shape

**Decision**: `GET /api/admin/norgespris`, `POST /api/admin/norgespris`, `DELETE /api/admin/norgespris/{id}`, all
JWT + `Roles(RoleNames.Admin)`, following the existing `/api/admin/locations` naming. The list returns each rate with a
computed `status` (`Superseded`, `InEffect`, `Upcoming`) and `totalPerKwh`, so the frontend does not have to repeat the
Oslo-date logic.

**Rationale**: matches the admin namespace the frontend already calls; computing status on the server keeps the
Oslo-date rule in one place (D2).

## D10. Frontend scope

**Decision**: this repo ships the API and the contract; the page is built in `no.sanddata.ams.frontend` under its own
spec, as 006 did with `contracts/backend-required.md`. The past-date warning (FR-011) and the page (FR-019) are
delivered there.

**Rationale**: separate repos, separate branches and deployments. The backend can be deployed first; until the page
exists the Admin can use the three endpoints through Scalar, which is enough to prepare the 1 January change.
