# Phase 1 Data Model: Electricity Cost Tracking

## New entities

### `ElectricityPrice` (new table)

Day-ahead spot price for one price region for one hour, from ENTSO-E.

| Field | Type | Notes |
|---|---|---|
| `Id` | Guid | PK |
| `PriceRegion` | string(10) | e.g. `"NO1"` |
| `HourStartUtc` | DateTime | Start of the priced hour, UTC |
| `PriceEurPerMwh` | decimal | As published by ENTSO-E |

Unique index on `(PriceRegion, HourStartUtc)` — upserts are keyed on this pair.

### `ExchangeRate` (new table)

Daily currency conversion rate, from Norges Bank.

| Field | Type | Notes |
|---|---|---|
| `Id` | Guid | PK |
| `CurrencyPair` | string(10) | `"EURNOK"` — kept as a field rather than assuming EUR/NOK forever, but only EUR/NOK is fetched by this feature |
| `RateDate` | DateOnly | The day this rate applies to |
| `Rate` | decimal | Units of NOK per 1 EUR |

Unique index on `(CurrencyPair, RateDate)`.

## No changes to existing entities

`Location.Zone` is already `IsRequired()` at the database level (confirmed in
`LocationConfiguration.cs`) and `Location.HasNorgesPriceAgreement` already exists — **FR-007 and
FR-008's data requirements are already satisfied by the current schema**. `HasNorgesPriceAgreement`
now gets an actual consumer for the first time (it currently exists but nothing reads it).

## New read-model views (not EF-managed tables)

Materialized as TimescaleDB continuous aggregates (see research.md §2-3), mapped into EF Core as
keyless entities for querying only:

- **`MinuteConsumption`**: `LocationId`, `MeterId`, `BucketStart` (UTC), `AvgPowerWatts` →
  `ConsumptionKwh` computed as `AvgPowerWatts / 60_000`.
- **`HourConsumption`**: `LocationId`, `MeterId`, `BucketStart` (UTC), `AvgPowerWatts` →
  `ConsumptionKwh` computed as `AvgPowerWatts / 1_000`.

Both include data for the current, still-open bucket via TimescaleDB's real-time aggregation —
no special-casing needed in application code for "the incomplete hour."

## Cost calculation (not persisted — computed at query time)

For a given `Location` and hour:

1. Sum that location's meters' `ConsumptionKwh` from `HourConsumption` for the hour.
2. If `Location.HasNorgesPriceAgreement` is true: `rate = FlatRateNokPerKwh + FlatRateTaxNokPerKwh`
   (from configuration, constant for every hour/day).
   Otherwise: `rate = (ElectricityPrice.PriceEurPerMwh / 1000) × ExchangeRate.Rate` for that
   location's `Zone` and hour (and that day's FX rate) — `null`/unavailable if either is missing.
3. `cost = consumption_kWh × rate` (null/unavailable if `rate` is null).
4. For the comparison feature (US4): also compute the *other* model's rate/cost the same way,
   independently nullable.
5. Day totals: sum of that day's hourly `consumption`/`cost` values (both models).

## New error codes

| Code | HTTP Status | Used when |
|---|---|---|
| `Location.NotFound` | 404 | Same semantics as existing measurement endpoints — `locationId` doesn't exist or isn't the caller's |
