# Data Model: Location Management and Hashed Sensor Keys

One migration. No new tables.

## `ApiKey` (changed)

| Column | Before | After |
|---|---|---|
| `Key` | varchar(100), the secret in clear | **dropped** |
| `KeyHash` | n/a | varchar(64) NOT NULL, **unique index**; lowercase hex SHA-256 of the key |
| `KeyHint` | n/a | varchar(8) NOT NULL; last 4 characters of the key, non-secret |
| `Description` | varchar(100) | unchanged (auto-generated, e.g. "Sensor key for Cabin") |
| `IsActive`, `ExpiresAt`, `CreatedAt`, `UpdatedAt` | | unchanged |

Rules:
- A key is accepted only if its hash matches, `IsActive`, `ExpiresAt > now`, **and** its location is active.
- Rotation updates the same row: new `KeyHash`/`KeyHint`, `ExpiresAt = now + 2 years`, `IsActive = true`.
- Status shown to Admins: `Active`, `Expired` (`ExpiresAt <= now`) or `Deactivated` (`IsActive = false`).

## `Location` (behavior changed, one index added)

| Column | Change |
|---|---|
| `SerialNumber` | **unique index** added (friendly 409 check in the handler first) |
| `Zone` | validated against `PriceZones` (NO1-NO5) at the boundary |
| `IsActive` | now meaningful (see rules) |
| all others | unchanged |

Rules:
- `IsActive = false`: ingestion rejects the location's key; regular users get "not found" for it everywhere; Admins
  still see and can edit it. History is kept; reactivating restores everything.
- Create inserts `ApiKey` + `Location` in one save; the plain key exists only in that request's response.

## `Meter` (unchanged)

Unique per (`LocationId`, `DeviceId`). Admins may register at any location.

## Migration steps (`AddHashedApiKeys`)

1. Add `KeyHash` and `KeyHint` as nullable.
2. Backfill: `UPDATE "ApiKey" SET "KeyHash" = encode(sha256(convert_to("Key",'UTF8')),'hex'), "KeyHint" = right("Key",4);`
3. Make both NOT NULL; create the unique index on `KeyHash`.
4. Drop `Key`.
5. Create the unique index on `Location.SerialNumber`.
6. `Down`: re-add `Key`, fill it with `KeyHash` (plain text cannot be restored), drop the new columns and indexes.

Verified first on an isolated TimescaleDB container with a copy of the existing key, then applied to the dev database;
the real forwarder key must still authenticate afterwards.

## Constants

- `Domain.Common.PriceZones`: `All = ["NO1","NO2","NO3","NO4","NO5"]`.
- Key format: 32 random bytes, lowercase hex (64 characters); lifetime 2 years.
