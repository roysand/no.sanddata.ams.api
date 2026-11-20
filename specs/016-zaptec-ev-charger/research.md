# Research: Read the Zaptec EV Charger and Optionally Count It with the House

Phase 0 of [plan.md](plan.md). Zaptec facts come from https://docs.zaptec.com, read 2026-11-20 (authentication,
usage guidelines, Fair Use Policy, rate limiting, installations, chargers, archived sessions). Points marked
**verify by hand** are not stated in the docs and are checked in [quickstart.md](quickstart.md) §0 before
implementation starts.

## R1. Signing in to Zaptec

- **Decision**:
  - **Token:** `POST https://api.zaptec.com/oauth/token`, form `grant_type=password`, `username`, `password`,
    `scope=openid`. The answer has `access_token` and `expires_in` (3600 in the docs); no refresh token.
  - **Configuration:** `Zaptec:Username` and `Zaptec:Password`, set in production as `Zaptec__Username` /
    `Zaptec__Password` from `ZAPTEC_USERNAME` / `ZAPTEC_PASSWORD` in the server's `.env` (`compose.prod.yaml`), and in
    `local.settings.json` locally. Bound to a `ZaptecOptions` class. Both empty means "not set up".
  - **Token cache:** one `ZaptecTokenCache` singleton holds the token in memory until 5 minutes before it expires,
    guarded by a `SemaphoreSlim`, so the job and the admin endpoints share one sign-in. A 401 on a data call clears
    the cache and signs in once more before giving up.
  - **HTTP:** a typed `HttpClient` with base address `https://api.zaptec.com/`, a 15 s timeout, the required
    `User-Agent: AmsApi/1.0` (a generic name, no personal data), and `RemoveAllLoggers()` so the factory's loggers
    never see the token request or the `Authorization` header.
  - **Errors:** a 400/401 from the token endpoint is `EvCharger.SignInRejected`; 5xx, timeout and network errors are
    `EvCharger.Unavailable`; 429 is `EvCharger.Unavailable` after one wait of `Retry-After` (always 1 s per the docs);
    an unreadable body is `EvCharger.InvalidResponse`; no configuration is `EvCharger.NotConfigured`.
- **Rationale**: Roy's choice (spec, Clarifications): his own login in `.env`, never through an endpoint or in the
  database. The token endpoint allows 1 request per second per IP and the guidelines say "Do not request a new token
  for every API call", so caching is required, not an optimisation. Keeping all of this inside the client means the
  later switch to Zaptec's UserGroup route changes one class and its options, not the job or the endpoints.
- **Alternatives**:
  - Storing the password encrypted in the database, as the OSO key is (spec 011): this is what the Fair Use Policy
    names, and Roy chose `.env`.
  - A new token per read: breaks the guidelines and the token rate limit for nothing.

## R2. Which Zaptec calls

- **Decision**:
  - **Chargers for the admin list:** `GET /api/chargers?PageSize=100` (fields `id`, `name`, `deviceId`,
    `installationId`, `installationName`). One page is enough for a household; more than 100 is answered with the
    first 100.
  - **Checking a charger on connect:** the same list, looking for the id; the connection stores the id, name,
    installation id and installation name from it.
  - **Sessions:** `GET /api/sessions/archived?ChargerId={id}&From={from}&To={to}&PageSize=200`, following `cursor`
    while `hasMore` is true. `From` is inclusive and `To` exclusive, both on the session's **end** time. Fields used:
    `id`, `startDateTime`, `endDateTime`, `energy`, `energyDetails[] { timestamp, energy }`, `replacedBySessionId`,
    `voided`.
  - **Not used:** `/api/chargehistory` (deprecated, ends 2027-01-01), Service Bus, webhooks, and every call that
    changes the charger (spec FR-017).
- **Rationale**: The archived-sessions call is the documented replacement and filters on end time, which is exactly
  "sessions that ended since the last read". Listing by charger id rather than installation id keeps a second charger
  at the same installation out.
- **Verified by hand** (2026-11-20, Roy's login, quickstart §0):
  - **Access:** the token, chargers and archived-sessions calls all answered `200` for a homeowner login.
  - **Token lifetime:** `expires_in` is **86400** (24 h, not the 3600 in the docs), and there is no refresh token.
  - **Casing:** `/api/chargers` answers in **PascalCase** (`Data`, `Id`, `Name`, `InstallationId`,
    `InstallationName`, `DeviceType`). `/api/sessions/archived` answers in camelCase. Parsing is case-insensitive.
  - **Roy's account:** 1 charger, "Lader Garasje Nr 11", `DeviceType` 4.
  - **Session volume:** 18 sessions from 2026-10-01 to 2026-11-20, 625.149 kWh in total, 7–14 points each. None was
    voided, aborted or replaced. `cursor` was null and `hasMore` false.
  - **`energyDetails` is per-interval, not cumulative** (contrary to the docs), as research R5 describes. The points
    add up exactly to `energy` (33.982 = 33.982 in the fixture session).

## R3. When to read, and how far back

- **Decision**:
  - **Job:** `EvChargerReadService` (a `BackgroundService` in `Features/EvChargers/Services`) runs every 15 minutes
    (`EvCharger:ReadIntervalMinutes`, default 15), with the same last-resort guard as the water heater sampler.
  - **Window per charger:** `From = ReadUpToUtc − 24 h`, `To = now`. On the first read `ReadUpToUtc` is null and
    `From` is the start of the previous Oslo month. After a successful read, `ReadUpToUtc = To`.
  - **Not configured:** the job logs once at startup that the Zaptec sign-in is not set up, and skips every cycle.
- **Rationale**: 15 minutes meets SC-003 and SC-005 with one sign-in an hour and one or two calls per cycle, far below
  10 requests per second. The 24-hour overlap catches sessions a charger uploads late after being offline (the
  sessions carry a separate `recognizedDateTime`); re-reading them is harmless because storing is idempotent (R4).
  The watermark makes gap filling after an outage automatic: the next success reads from where the last one ended.
- **Alternatives**: Service Bus events for near-real-time: a new protocol and connection for a feature that only needs
  history. A fixed "last 7 days" window: misses longer outages.

## R4. Storing sessions and their hours

- **Decision**:
  - Three tables: `EvCharger` (the connection), `ChargingSession` (one row per Zaptec session id) and
    `ChargingSessionHour` (the session's kWh per UTC clock hour, with `LocationId` copied in for the read query).
  - **Upsert:** for each session read, the stored session with the same Zaptec id and its hours are replaced in one
    `SaveChanges`. A session that has `replacedBySessionId` set, or is `voided`, is not stored, and is deleted if it
    was stored earlier.
  - **Hours for a range:** `SUM(Kwh) GROUP BY HourStartUtc` over `ChargingSessionHour` for the location and range, in
    LINQ (no raw SQL). It returns the kWh and whether any part is estimated.
- **Rationale**: Keeping the hours per session makes "replace a session" a plain delete-and-insert with no
  recomputation of shared hourly rows, and two sessions in the same hour simply add up. A household has a few hundred
  sessions a year and a few thousand session-hours, so the `GROUP BY` is trivial.
- **Alternatives**:
  - One hourly table per location updated in place: replacing a session would need to subtract its old share.
  - Computing hours from the sessions on read: moves the split into every cost query, and needs `energyDetails`
    stored as JSON.

## R5. Splitting a session into clock hours

- **What Zaptec sends** (verified by hand, R2):
  - **Point values:** each `energyDetails` point is the kWh delivered in the period that **ends** at its
    `timestamp`.
  - **Point times:**
    - The first point is at the session start, with 0.
    - The middle points are at whole UTC hours, but not always exactly: one is `02:59:59`.
    - The last point is at the session end, with the energy since the last whole hour.
  - **Hours with no charging** have no point.
  - **Examples:**
    - A session starting 18:23:09 has 2.706 kWh at 19:00, which is 37 minutes at about 4.4 kW.
    - A session ending 05:57:48 has 4.258 kWh at its end point.
- **Decision**: A pure `ChargingEnergySplitter.Split(start, end, totalKwh, details)` in `Application/EvChargers`.
  - **With details:** each point goes to the clock hour that ends at it.
    - **Snapping:** a timestamp within 2 minutes of a whole hour is rounded to that hour, and goes to the hour before
      it. For example, `19:00:00` and `02:59:59` go to 18:00 and 02:00.
    - **Other timestamps** (the end point) go to the hour that contains them. For example, `05:57:48` goes to 05:00.
    - **Combining:** points that land in the same hour are added together.
    - **Clipping:** hours before `floor(start)` or after `floor(end)` are dropped. Those are only the 0-valued start
      point.
    - **Negative values** count as 0.
  - **Without details** (none, or none left after clipping): `totalKwh` is spread evenly over the session's minutes,
    split at hour boundaries by time, and every hour is marked `Estimated`.
  - **Total:** if the hours do not add up to the session's `energy` within 0.001 kWh, they are scaled to it, so the
    stored session always matches the Zaptec app (SC-002). Roy's data adds up exactly, so this is only a safety net.
  - **No rounding.** Hours that Zaptec reports with 0 kWh are kept as 0.
- **Rationale**:
  - The values sit at about 4.4–4.5 kWh per full hour, the charger's power, and never grow. So they are amounts per
    period, not cumulative.
  - Points come per hour, so the split is exact per clock hour, with no interpolation.
  - The 2-minute snap absorbs the clock skew seen (`02:59:59`) without moving the end point of a session that ends
    mid-hour.
- **Alternatives**:
  - Treating the values as cumulative, as the docs say: this contradicts the data, and would give negative steps.
  - Spreading each point linearly over its interval: that only matters for points that are not on the hour, which
    the data does not have.

## R6. How "count with the house" shows in the figures

- **Decision**:
  - **`consumptionKwh` keeps its meaning:** the meter's kWh. Every cost, the Norgespris cap, the capacity step and the
    water heater's cap (spec 012) keep using it, so they cannot change (FR-014, FR-016).
  - **New `totalKwh`** on the hourly, daily, current-hour and bill responses: `consumptionKwh + evCharger.kwh` when
    the charger is included, else equal to `consumptionKwh`.
  - **New `evCharger` part:** `{ kwh, estimated, included }` on hours and the current hour; days and the bill add
    `hoursWithoutMeterReading` (R7) and the bill adds the first and last charging hour, as the heater part does. It is
    `null` when the range has no charger hours (FR-013).
- **Rationale**: If `consumptionKwh` itself grew, `ratePerKwh × consumptionKwh` would no longer be the cost, and the
  heater's "everything else" (`consumptionKwh − waterHeater.kwh`) would wrongly include the car. A separate total
  gives the frontend one field for the chart and keeps every existing field's meaning (no breaking change). The spec's
  "the hour's kWh includes the charger" is met by `totalKwh`; the spec wording is aligned with this.
- **Alternatives**: Changing `consumptionKwh` when the setting is on (breaks the cost and heater invariants above);
  no total, only the part (the setting would then do nothing in the API, contrary to FR-011).

## R7. Charging in an hour with no meter reading

- **Decision**:
  - **Hourly list:** unchanged. It still has one item per hour with a meter reading, and the charger part is attached
    to those hours only; no item is added for an hour without one.
  - **Days and the bill:** the charger part is the sum of **all** the charger's hours in the day or month, so no
    charged kWh is lost. `hoursWithoutMeterReading` counts the charging hours that had no meter hour, and `totalKwh`
    includes them.
- **Rationale**: Adding hours with no meter reading to the cost list would give them a null cost, which would make the
  day's cost null (`CostMapper.SumCost`) and so change a cost, which FR-014 forbids. Meter gaps are rare; the count
  makes them visible. A day with no meter reading at all has no day item, as today.
- **Alternatives**: Inserting placeholder hours with 0 kWh and cost 0 (shows a false cost); dropping the charger kWh of
  such hours (loses charging, breaks SC-002 at month level).

## R8. Where the charger part is computed

- **Decision**:
  - **Provider:** `HourlyCostProvider.GetAsync` gets a `withEvCharger` flag next to `withWaterHeater`. When it is set,
    it reads the charger hours once for the range and the location's `IncludeEvChargerInTotals`, attaches
    `EvChargerHour` to each `HourCost`, and returns the charger hours that had no meter hour alongside.
  - **Return type:** `GetAsync` becomes `HourlyCosts(IReadOnlyList<HourCost> Hours, IReadOnlyList<EvChargerHour>
    UnmatchedChargerHours)`.
  - **Pure math:** `EvChargerShare.Sum` in `Application/EvChargers` adds parts up for days and the month.
  - **Callers:** the hourly, daily and bill handlers pass `true`; the current-hour handler reads the one hour directly
    through the repository; the saving handler is unchanged.
- **Rationale**: The same shape as spec 012 (research R6 there): one extra indexed query only where the part is shown.
  `ElectricityCost` reads charger hours only through an Application repository interface, never from
  `Features/EvChargers` (Principle II).
- **Alternatives**: A separate "with charger" endpoint (the frontend would read two endpoints and merge them).

## R9. The setting and who may change it

- **Decision**:
  - **Field:** `Location.IncludeEvChargerInTotals` (bool, default false).
  - **Endpoint:** `PUT /api/locations/{locationId}/ev-charger/include` with `{ "include": true|false }`.
    - Owner or Admin: changes it.
    - Viewer: `403`.
    - Not linked or no such location: `404`.
    - `include: true` with no charger connected and no stored sessions: `400 EvCharger.NotConnected`.
  - **Removing the connection** sets it to false.
  - **`LocationSummaryResponse`** gains `hasEvCharger` and `includeEvCharger`, as `hasWaterHeater` was added in spec
    011, so the app can draw the checkbox without another call.
- **Rationale**: Spec FR-010. A viewer can already see the location, so `403` reveals nothing. It is a dedicated
  endpoint rather than a field on `PUT /api/locations/{id}`, so the owner's update does not grow a field that only
  makes sense with a charger.
- **Alternatives**: Adding it to the owner's location update (couples the Locations slice to charger rules).

## R10. Logging

- **Decision**: EventIds **1900–1999** for EvCharger, added to the table in `CLAUDE.md` and `DevelopmentGuide.md`:
  - 1900–1919: the feature (connect, remove, setting changed, read failing / still failing / recovered, read cycle
    failed, sessions stored);
  - 1920–1929: the Zaptec client (request failed with status, unavailable, invalid response, sign-in rejected).
  - The client logs the call name ("token", "chargers", "sessions") and the location id, never URLs, headers, bodies
    or exception messages.
  - The failure tracker is a copy of `SampleFailureTracker` in `Features/EvChargers/Services` (Principle II: a small
    duplication over a shared dependency between slices).
- **Rationale**: FR-009, FR-018, Constitution VI.
