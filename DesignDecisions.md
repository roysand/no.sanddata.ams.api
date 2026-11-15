# Design decisions

Short notes on rules the system relies on but the code does not (yet) enforce. Read these before changing
measurement, meter or "Right now" behaviour.

## One meter per location

**Rule:** every location has exactly one reader/meter.

**Why it matters:** the "Right now" tile shows the newest measurement for a location
(`GET /api/measurements/latest?locationId=...`, `IMeasurementRepository.GetLatestAsync`). With a single meter
that is simply the current consumption. If a second meter is ever added to a location, the endpoint returns
whichever meter reported last, so the tile would jump between the two meters' values instead of showing a total.

**Current state: not enforced.** `MeterConfiguration` only has a unique index on `(LocationId, DeviceId)`, which
allows many meters per location as long as their device ids differ. Nothing in `CreateMeterEndpoint` stops a second one.

**To enforce it later:**
- Make the index on `Meter.LocationId` unique (new EF migration), and have the create-meter handler return a
  validation/conflict `Error` instead of a database exception.
- Or, if multi-meter locations become a requirement, change "latest" to take the newest reading *per meter* and
  sum them. The endpoint already accepts an optional `meterId` for narrowing.

## "Right now" (latest reading)

- The forwarder receives a reading about every 2.5 s and uploads in batches every `Outbox:DrainIntervalSeconds`
  (set to 10 s in the deployed `local.settings.json`), so the newest reading is at most about 12.5 s old.
- "Latest" is chosen by the measurement **timestamp**, not insert time, so a catch-up upload of old readings after
  an outage never replaces a newer reading. Backed by the index on `(LocationId, Timestamp)`.
- **Stale guard (in the API):** the latest endpoint returns `isStale`, true when the reading is older than
  `Measurements:StaleAfterSeconds` in appsettings.json (default 60, about 3-5 drain intervals; read per request, no code change needed). The frontend just shows "offline" instead of a frozen value.
  Beware of a reader with a wrong clock, which would make this comparison misleading.
- Raw readings (2.5 s resolution) must be stored for this to work; keep them for a few days and keep per-minute
  aggregates for history.
