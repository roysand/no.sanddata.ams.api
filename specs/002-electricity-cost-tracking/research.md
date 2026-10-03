# Phase 0 Research: Electricity Cost Tracking

All `[NEEDS CLARIFICATION]` markers from the spec were resolved interactively before the spec was
finalized. This document covers the implementation-level decisions needed before design, each
with rationale, verified external facts, and the alternative considered.

## 1. TimescaleDB is not active in this database yet

**Finding**: `compose.db.yaml` already runs the `timescale/timescaledb` image, but no migration
has ever run `CREATE EXTENSION timescaledb` or converted `Measurement` into a hypertable — it's
still a plain Postgres table (confirmed: no `timescaledb`/`hypertable` references anywhere in
`src/Infrastructure/Database/Migrations/`). **This feature is what actually activates
TimescaleDB** for the first time, not just a consumer of an already-active feature.

**Decision**: This feature's first migration must, in order: enable the extension, convert
`Measurement` into a hypertable (with `migrate_data => true` so existing rows aren't lost), then
create the continuous aggregates. All via `migrationBuilder.Sql(...)` raw SQL — EF Core's
migration model has no native concept of hypertables or continuous aggregates.

## 2. Minute/hour consumption via continuous aggregates

**Decision**: Two continuous aggregates, both built directly from raw `Measurement` (not chained
off each other, to avoid extra TimescaleDB version caveats around aggregate-on-aggregate — data
volume here is trivial so there's no performance reason to chain them):

```sql
CREATE MATERIALIZED VIEW measurement_minute WITH (timescaledb.continuous) AS
SELECT "LocationId", "MeterId", time_bucket('1 minute', "Timestamp") AS bucket,
       avg("PowerWatts") AS avg_power_watts
FROM "Measurement" GROUP BY "LocationId", "MeterId", bucket;

CREATE MATERIALIZED VIEW measurement_hour WITH (timescaledb.continuous) AS
SELECT "LocationId", "MeterId", time_bucket('1 hour', "Timestamp") AS bucket,
       avg("PowerWatts") AS avg_power_watts
FROM "Measurement" GROUP BY "LocationId", "MeterId", bucket;
```

Energy (kWh) for a bucket = `avg_power_watts × bucket_duration_hours / 1000`. This is computed in
the read layer (or the view itself), not stored as a separate redundant column.

**Rationale for real-time aggregation covering the "live current hour" requirement (FR-002)**:
**Correction (verified empirically during implementation): this TimescaleDB version defaults new
continuous aggregates to `materialized_only = true`**, not `false` as originally assumed here —
real-time aggregation must be requested explicitly: `WITH (timescaledb.continuous,
timescaledb.materialized_only = false)`. With that set, a query against `measurement_hour`
automatically unions in raw, not-yet-materialized data for the current, still-open bucket.
Querying "this hour's consumption" mid-hour then returns an accurate, live-updating answer with
no extra application logic — this is exactly what User Story 1 needs. (This was caught by testing
the migration against a disposable database before applying it for real — the first version of
this migration would have silently broken FR-002 in production.)

**Accuracy caveat (documented, not solved)**: averaging instantaneous Watt readings rather than
trapezoidal-integrating them is an approximation. Acceptable given reading frequency (~1-2s) and
that this is a hobby-scale household dashboard, not billing-grade metrology.

**Refresh policy**: `add_continuous_aggregate_policy` on both views (e.g. minute: refresh every
minute, `end_offset` 1 minute; hour: refresh every 5 minutes, `end_offset` 1 minute) so completed
buckets materialize promptly while real-time aggregation covers the open bucket in between.

## 3. Reading continuous aggregates from EF Core

**Decision**: Continuous aggregates are materialized views — EF Core can query them but must
never try to write to them. Map each as a keyless entity via `.ToView("measurement_minute")
.HasNoKey()` in `OnModelCreating`. The DbContext never uses these for writes; the source-of-truth
write path (measurement ingestion) is completely unchanged.

## 4. ENTSO-E day-ahead prices

Verified against current ENTSO-E documentation (see Sources).

- **Auth**: requires a manually-requested security token — register an account at
  transparency.entsoe.eu, email transparency@entsoe.eu ("Restful API access", the registered
  email in the body), approval typically within ~3 business days, then generate the token from
  "My Account". **This is a manual, one-time setup step outside the codebase** — the
  implementation just needs the resulting token in configuration (`local.settings.json`, never
  committed), same pattern as the AMS API key.
- **Endpoint**: `GET https://web-api.tp.entsoe.eu/api` with query params `securityToken`,
  `documentType=A44` (day-ahead prices), `processType=A01`, `in_Domain`/`out_Domain` (EIC area
  code, identical for both on a price query), `periodStart`/`periodEnd` (UTC, `yyyyMMddHHmm`,
  no timezone suffix — conversion is the caller's job).
- **Response**: XML (`TimeSeries` → `Period` → `Point`, each point a position number + price;
  reconstruct the point's start from `Period.timeInterval.start` + `resolution` + the point's
  position). Parse with `System.Xml.Linq`, no new package needed.
- **Resolution is 15 minutes (`PT15M`), verified live** (96 points/day), and each day came back
  in two identical `TimeSeries`. **Decision (user, 2026-10-02): cost in Norway is calculated per
  hour, so the price for an hour is the mean of its four quarter-hour prices.** The client
  dedupes points by start time and averages per hour (a single point if a zone ever returns
  `PT60M`). Per-quarter cost is out of scope.
- **Norwegian bidding zone EIC codes** (verified, stable reference data — static, not needing a
  full reference table for this feature's scope, matching the spec's own deferral of a generic
  price-region table to later):

  | Zone | EIC code |
  |---|---|
  | NO1 | `10YNO-1--------2` |
  | NO2 | `10YNO-2--------T` |
  | NO3 | `10YNO-3--------J` |
  | NO4 | `10YNO-4--------9` |
  | NO5 | `10Y1001A1001A48H` |

## 5. Norges Bank exchange rates

Verified against the official Norges Bank open data API.

- **Endpoint**: `GET https://data.norges-bank.no/api/data/EXR/B.EUR.NOK.SP?format=sdmx-json&startPeriod=...&endPeriod=...` — no authentication required (public open data).
- **Response**: SDMX-JSON (a nested `dataSets`/`observations` structure keyed by dimension
  indices, not flat key-value pairs). Parse with `System.Text.Json`; no dedicated SDMX package
  given this feature only ever needs one simple daily series.

## 6. Background fetch scheduling

**Decision**: A single `BackgroundService` inside the API (per the earlier architecture decision
that price/FX ingestion lives in the API, not `no.sanddata.ams.services`, since both sources are
public HTTPS APIs the Azure-hosted API can reach directly). Loop on a fixed interval (e.g. every
6 hours, configurable) rather than trying to precisely target ENTSO-E's ~12:00-13:00 CET
next-day publish time: each tick attempts an idempotent upsert of (a) tomorrow's + today's prices
for every distinct `Zone` currently used by a `Location`, and (b) today's exchange rate. If data
isn't published yet, the attempt simply finds nothing new and retries next tick — no precise
wall-clock scheduling logic needed. Matches the simple timer-loop pattern already used by
`no.sanddata.ams.services`'s `OutboxDrainService`.

## 7. Cost is computed at query time, not stored

**Decision**: No persisted "cost" table. Cost = `consumption_kWh(hour, from the hour continuous
aggregate) × rate_NOK_per_kWh(hour, from ElectricityPrice + ExchangeRate, or the flat
government rate)`, computed in the query handler. Storing a separate cost table would mean
keeping it in sync with three other moving pieces (consumption, price, FX) for no real benefit
at this data volume — computing it on read is simple and cheap.

## 8. Flat government rate (Norgespris) is configuration, not data

**Decision**: The flat rate (NOK 0.40/kWh baseline + a tax component, per the user) is
application configuration (`appsettings.json`), not a database table — it's a single
government-set constant that changes rarely, not per-location or per-hour data. Exact current
values are a deployment/config concern, not hardcoded in code.

## Sources

- [ENTSO-E Transparency Platform API: key, docs and Python](https://progrunners.com/entso-e-api/)
- [IVT-HP-PriceControl Entsoe API Setup guide](https://github.com/QAnders/IVT-HP-PriceControl/blob/main/Entsoe-API-Setup.md)
- [New area definitions NO1-NO5 from 28 August 2017 — Nord Pool](https://www.nordpoolgroup.com/en/message-center-container/newsroom/tso-news/2017/q4/new-area-definitions-no1-no5-from-28-august-2017/)
- [Norges Bank — Data warehouse for open data](https://www.norges-bank.no/en/topics/Statistics/open-data/)
- [Norges Bank — Guide to the data portal](https://www.norges-bank.no/en/topics/statistics/open-data/guide-data-warehouse/)
