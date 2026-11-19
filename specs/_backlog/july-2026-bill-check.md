# Backlog: Check the July 2026 bill against the invoice in production

Status: waiting. Written 2026-11-19, after spec 010 (estimated bill for the month) was merged and deployed (PRs #27 to
#32). Roy decided to do this check later. It is the last open part of spec 010: tasks T001 and T040 in
`specs/010-monthly-bill-estimate/tasks.md`. Remove this note, and mark T001 and T040 done, when the check is done.

## Why

The bill endpoint is proven on synthetic data (a unit test that rebuilds the July invoice, and a check on a scratch
copy of the local database, T038). It has not yet been compared with a real invoice on real readings. The July 2026
Fjordkraft/Elvia invoice for the holiday home (total 673,55 kr) is the reference; its lines are in
`specs/010-monthly-bill-estimate/spec.md`, "Reference data for the tests".

## Steps

1. **Set the holiday home's supplier terms in production** (Admin, `PUT /api/admin/location-pricing/{locationId}`, or
   the Admin pricing page): `supplierMonthlyFee` **27.20** (34,00 kr incl. VAT) and `supplierMarkupPerKwh` **0.0319**
   (the invoice's 3,99 øre incl. VAT is 0.03192 excl. VAT, but a markup has at most 4 decimals; 0.03192 is refused).
   Keep the grid tariff and the flags as they are. `GET /api/admin/location-pricing` should then show the holiday home
   with `isComplete: true`. Until this is done every location shows as incomplete and every bill has the fee line
   "not set".
2. **Confirm the July data is in production** (T001, read-only): hourly readings of the holiday home for 2026-07
   (744 hours), spot prices of its zone and EURNOK rates for July, Elvia's grid rate from 2026-07-01 (day 0.2899,
   night 0.1699), the capacity step table from 2026-07-01 (5-10 kW at 336), the Norgespris rate (0.40) and the
   holiday-home cap (1,000 kWh). Note what is missing; a gap explains a difference rather than blocking the check.
3. **Compare** `GET /api/electricity-cost/bill?locationId={holiday home}&month=2026-07` with the invoice, as in
   `specs/010-monthly-bill-estimate/quickstart.md` step 6:

   | Check | Expected | Tolerance |
   |---|---|---|
   | `total.amount` | 673,55 | ± 0,10 |
   | Fee, markup, grid day and night, capacity, electricity tax, Enova fee lines | the invoice line | ± 0,02 each |
   | `SpotEnergy`, `NorgesPrisCredit`, Strøm and Nettleie subtotals | 373,74 / −253,58 / 417,33 / 256,22 | ± 1,00 each |
   | `SpotEnergy + NorgesPrisCredit` | 120,16 | ± 0,02 |
   | Quantities `GridEnergyDay` / `GridEnergyNight` | 95,58 / 144,73 kWh | 0,01 |

   With markup 0.0319 the markup line is about 9,58 kr (invoice 9,59), inside the tolerance. A gap in the kWh
   quantities points at missing readings (`hours.withReadings` below 744); a gap in the spot lines only, at a
   different spot source or exchange rate (the total does not depend on spot on Norgespris below the cap).
4. **Record the result** in T001 and T040 of `specs/010-monthly-bill-estimate/tasks.md` and in
   `specs/010-monthly-bill-estimate/checklists/requirements.md`, then remove this note.

## Access

Production needs an Admin sign-in for step 1 and 3, and the database is reachable only through an SSH tunnel
(`DockerDeployment.md`, section 6 "Looking into the production database"). When this is picked up, decide first who runs which step: Roy in
Scalar with Claude comparing the pasted JSON, or Claude with a short-lived token (and the tunnel for step 2).
