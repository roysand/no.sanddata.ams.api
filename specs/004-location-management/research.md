# Research: Location Management and Hashed Sensor Keys

Findings from reading the current code (2026-10-03) and the decisions they lead to. No open
`NEEDS CLARIFICATION` items remained after the spec.

## 1. How sensor keys work today

- `ApiKey` stores the raw key in column `Key` (varchar 100). `ApiKeyEfRepository.FindActiveByKeyAsync(key)`
  does `a.Key == key && a.IsActive && a.ExpiresAt > now` and includes the `Location`.
- `ApiKeyAuthenticationHandler` then builds claims: **`new Claim("ApiKey", apiKey.Key)`** (the plain key goes into
  the principal), `ApiKeyId` (via reflection) and `LocationId`. Ingestion reads `LocationId` from the claims.
- Location and key are 1:1: `Location.ApiKeyId` is NOT NULL with a unique index; `ON DELETE CASCADE` from key to location.
- `Location.IsActive` is **not used anywhere** (not at ingestion, not in lists, not in access checks).

## 2. Hashing design

- **Algorithm: SHA-256, lowercase hex (64 chars).** Keys will be 32 random bytes (256 bits of entropy), so the
  hash cannot be brute-forced and no salt or slow hash (BCrypt) is needed. A slow hash would add latency to every
  sensor reading. Lookup stays a single indexed equality query on the hash, which also avoids secret-dependent
  string-comparison timing.
- **Key format: 32 bytes from `RandomNumberGenerator`, encoded as lowercase hex (64 chars).** Fits the existing
  `varchar(100)` convention and is easy to paste into the forwarder's environment/config.
- **Hint = last 4 characters** of the key, stored in its own column so Admins can recognise a key without any
  reconstruction possibility (4 of 64 hex characters = 16 bits, negligible).
- **Schema**: add `KeyHash` (varchar 64, NOT NULL, unique index) and `KeyHint` (varchar 8, NOT NULL); drop `Key`.
- **Migration with backfill (so the running sensor keeps working)**: add both columns nullable, run one SQL statement
  `UPDATE "ApiKey" SET "KeyHash" = encode(sha256(convert_to("Key", 'UTF8')), 'hex'), "KeyHint" = right("Key", 4)`,
  make the columns NOT NULL, create the unique index, then drop `Key`. PostgreSQL's built-in `sha256(bytea)` produces
  exactly what `SHA256.HashData(Encoding.UTF8.GetBytes(key))` + lowercase hex produces in C#, verified by a unit test
  with a known vector and by the migration test on a copy of the real data.
- **`Down` cannot restore plain text.** It re-adds `Key` filled with the hash value so the schema round-trips; this
  is documented in the migration. Plain-text keys are intentionally unrecoverable.
- **Plain key must not stay in the principal**: the `ApiKey` claim is removed (nothing reads it).

## 3. Where checks happen (inactive locations, expiry)

- `FindActiveByKeyHashAsync(hash)` keeps `IsActive && ExpiresAt > now` and now also requires **`Location.IsActive`**
  (spec: a deactivated location rejects readings). Rejection stays in the auth handler (401), unchanged for the sensor.
- **Access for regular users is already centralised**: every data endpoint (measurements, consumption, cost, meters)
  calls `ILocationRepository.IsUserAssociatedAsync`. Adding `Location.IsActive` to that one query makes inactive
  locations "not found" everywhere. `GetForUserAsync` (the "my locations" list) gets the same filter.
- Admin link/unlink and admin location management do **not** use `IsUserAssociatedAsync`, so Admins keep full
  access to inactive locations.

## 4. Creating a location with its key

- One handler creates `ApiKey` + `Location` and saves once: atomic, so a failure leaves nothing (and no key shown).
- `Location` and `ApiKey` have private setters; add small domain methods (`Location.Update`, `SetActive`,
  `AssignApiKey`; `ApiKey.Rotate`, `SetActive`) instead of making setters public.
- Serial number: add a **unique index** (the dev database has one location, so no conflict) plus a handler check
  that returns a friendly `Location.SerialNumberExists` (409) before hitting the index.
- Zone: `Domain.Common.PriceZones` with `NO1`-`NO5`. The ENTSO-E client has its own private map of the same codes;
  the duplication is accepted (documented in code) rather than coupling Domain to Infrastructure.
- Price data for a **new zone** is fetched by the existing background service on its next cycle (every
  `PriceFetch:IntervalHours`, default 6) or at the next start. Until then the location's spot cost shows as
  "not available yet", which the dashboard already handles. Not changed by this feature.

## 5. Endpoint shape

- Regular `GET /api/locations` ("my locations") is unchanged apart from hiding inactive ones.
- Admin management lives under **`/api/admin/locations`** so it cannot be confused with the per-user list:
  `GET`, `POST`, `PUT /{id}`, `POST /{id}/api-key/rotate`, `PUT /{id}/api-key` (active flag).
  All use `AuthSchemes(JWT)` + `Roles(RoleNames.Admin)`; bodiless POST/PUT use route values only (the earlier
  415 lesson from feature 003).
- The admin list embeds each location's meters, so no new "list meters" endpoint is needed.
- Reader registration reuses `POST /api/meters`: members keep their right (but only for **active** locations, via the
  shared access check); an **Admin may register at any location**, active or not. This needs the caller's admin flag in
  `CreateMeterCommand` (the `Caller` record exists in the Users slice; Meters gets its own tiny equivalent field to
  avoid a cross-slice dependency, per the constitution).

## 6. Logging and secrets (FR-014, FR-017)

- `RequestResponseLoggingMiddleware` masks the `Headers` attribute unless `Headers` is listed in
  `RequestLogging:AttributesToLog`; with that opt-in, `Authorization` and **`X-API-Key`** would be logged in clear. It
  would also log response bodies when `LogResponseBody` is on, and the create/rotate responses contain the full key.
  **Decision**: always redact the `Authorization` and `X-API-Key` header values, and always mask the response body of
  key-revealing responses (`POST /api/admin/locations` and `.../api-key/rotate`), regardless of configuration.
- New events in the Locations range 1400-1499: location created/updated/activated-deactivated, key rotated,
  key activated/deactivated, reader registered by an admin. Fields are ids and the acting user, never keys or hashes.

## 7. Frontend (separate repo `no.sanddata.ams.frontend`)

- New admin **Locations** page (`/admin/locations`, `AdminRoute`) with a table, an add/edit dialog, a "key created"
  dialog that shows the key once with a copy button and a "I have copied the key" confirmation before it can be closed,
  a rotate-key confirmation (warning that the sensor stops working until updated), and a register-reader dialog.
- The Users page switches its location checkbox source from `GET /api/locations` to the admin list, which fixes the
  "own locations only" limitation.
- No frontend test framework exists in that repo; verification follows the headless-browser approach used for
  features 002/003 (not committed).

## 8. Things deliberately not done

- No deletion of locations, no custom expiry, no per-location time zone, no price-zone table (spec out of scope).
- No overlap period for rotation (old key dies immediately), no key-rejection logging (minimal footprint).
- No change to the sensor/forwarder: it keeps sending `X-API-Key` exactly as before.
