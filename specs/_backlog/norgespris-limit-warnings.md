# Backlog: Norgespris limit warnings (frontend spec 015)

Status: the API side is built (spec `specs/018-norgespris-limit-status`, PR #58, merged and deployed 2026-11-22). The
frontend spec is written (`no.sanddata.ams.frontend` → `specs/015-norgespris-limit-warnings/spec.md`, branch
`015-norgespris-limit-warnings`). Its assumptions say "the field names come from its plan", so this note is input
for that plan. Remove this note once the frontend's plan for spec 015 has settled the field names. The two
production checks of API spec 018 (T022, T023 in its `tasks.md`) are tracked there, not here.

## For the frontend

Full shapes and field meanings:
[`specs/018-norgespris-limit-status/contracts/endpoints.md`](../018-norgespris-limit-status/contracts/endpoints.md)
and [`data-model.md`](../018-norgespris-limit-status/data-model.md). The rules behind each figure are in
[`research.md`](../018-norgespris-limit-status/research.md).

### The status: `GET /api/electricity-cost/norgespris-status`

- **One request for every location**, JWT only, no parameters. The answer is an object, not a bare list:
  `{ "asOf": "…Z", "items": [ … ] }`.
- **Each item has:** `locationId`, `month` (`yyyy-MM`), `limitKwh`, `kwhSoFar`, `projectedKwh`, `overLimit`,
  `overLimitKwh`, `norgesPrisPricePerKwh` and `resetsAt`. It has no location name: join on `locationId` with
  `GET /api/locations` for the name.
- **Which locations:** the ones `GET /api/locations` gives the user that are active and have Norgespris, in the same
  order. Admins get the same list as everyone else, not every location. A location without Norgespris is not
  listed, and a user with none gets `items: []`.
- **Over (FR-002):** use `overLimit`, which is `kwhSoFar ≥ limitKwh`. `overLimitKwh` is how far over, 0 when not over.
- **Approaching (FR-003):** `kwhSoFar ≥ 0.9 × limitKwh` and not `overLimit`. That rule lives in the app.
- **Projected (FR-004):** `projectedKwh > limitKwh`. This replaces spec 012's reading of the bill's
  `NorgesPrisCredit` line.
- **Null fields:**
  - `limitKwh` is null when no limit is set. Such a location is never over, so show no limit warning for it.
  - `projectedKwh` is null when the last 7 days have no readings (spec 015, scenario 5: "without a projection yet").
  - `norgesPrisPricePerKwh` is null when no rate or VAT is set. Show no expensive-hours warning then.
- **Days until the limit** (spec 015 assumption): (`projectedKwh − kwhSoFar`) spread over the days left. Early in an
  hour `projectedKwh` can be slightly below `kwhSoFar`: the running hour's partial readings are in `kwhSoFar`, while
  the projection uses a full projected hour instead. Treat a negative rest as 0.
- **Reset date:** `resetsAt` is a UTC instant, e.g. `2026-11-30T23:00:00Z` for December. Show it as an Oslo date
  (1 December), never as the UTC date.
- **Matching the bill (SC-003):** `projectedKwh` is exactly the bill's `totalKwh + projection.kwh`. `kwhSoFar`
  includes the running hour, while the bill's `totalKwh` stops at the start of the current hour. Rounded to whole
  kWh, the two can differ by part of an hour's use.
- **The car:** when a location counts its car with the house, the car's kWh are already in `kwhSoFar` and
  `projectedKwh`. The app adds nothing (spec 015 agrees). Only completed charging sessions count (Zaptec gives no
  others), so a car still charging appears once its session ends. The car is never projected.
- **Refresh:** the status is cheap: two queries per location and no pricing, aimed at under 300 ms. A refresh every
  few minutes is fine (FR-001). `asOf` is when the figures were computed.
- **Errors:** 401 without a token. There is no other error case, but FR-009 still applies to network failures.

### Tomorrow's spot energy price: `GET /api/electricity-cost/prices?locationId=…&date=yyyy-MM-dd`

- **New field on each item:** `spotEnergyPricePerKwh`. It is the day-ahead price in NOK per kWh including VAT (none
  for a VAT-exempt location), with no grid fees, taxes or markup.
- **Comparing with Norgespris:** it is on the same basis as `norgesPrisPricePerKwh`, so the 4 × rule (FR-006) is
  `spotEnergyPricePerKwh ≥ 4 × norgesPrisPricePerKwh`, with no conversion.
- **Use this field, not the others:** `ratePerKwh` and `spotRatePerKwh` are full rates with fees and taxes, and a
  location over its limit already sees the spot rate in `ratePerKwh`.
- **Not published yet:** the field is null for an hour whose price isn't published. Use it for the "tomorrow is
  published" check, as the price chart does with the other two fields. A negative price is sent as negative.
- **Asking for the hours:** one request per location that needs it (FR-010). The day has 23, 24 or 25 items on
  clock-change days, each with a UTC `hourStart`, so group them into Oslo hours for the ranges.

### Changed elsewhere by API spec 018

For a location with "count the car with the house" on, the car's kWh now count toward the Norgespris limit in every
cost figure: hourly, daily, current hour, saving, bill and prices. Such a location passes its limit sooner, and its
bill's `NorgesPrisCredit` line gets smaller. Nothing in the frontend has to change for this, but figures on the
bill and the price chart can shift for that location compared with before. With the setting off, nothing changed.

## Not covered by the API

- Web push and warnings outside the app (frontend `backlog.md`, "Warnings").
- The car's own charging cost on the bill (frontend `backlog.md`, "Car charging cost"). When that is specified, it can
  use `norgesPrisPricePerKwh` from the status as the price per kWh.
