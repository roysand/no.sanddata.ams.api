# Research: Read the OSO Water Heater

Phase 0 of [plan.md](plan.md). Every open point from the Technical Context is decided here. The real OSO answers
behind these decisions are in `specs/_backlog/oso-water-heater.md` ("Tried by hand, 2026-11-19").

## R1. Protecting the OSO subscription key at rest

**Decision**: AES-256-GCM with `System.Security.Cryptography.AesGcm`, behind an Application interface
`ISecretProtector` (`Protect(string) → string`, `Unprotect(string) → Result<string>`). The 32-byte key comes from
configuration `WaterHeater:KeyEncryptionKey` (base64). On the Hetzner server it is `WATER_HEATER_KEY_ENCRYPTION_KEY`
in `/opt/ams-api/.env`, passed to the `api` service in `compose.prod.yaml` as
`WaterHeater__KeyEncryptionKey: ${WATER_HEATER_KEY_ENCRYPTION_KEY}`, exactly as `JWT_SECRET_KEY` is. The stored value is `v1:` + base64(nonce ‖ ciphertext ‖ tag), with a
fresh 12-byte random nonce per value. The location id is bound in as associated data, so a value copied to another
location's row does not decrypt.

**Rationale**:
- The API runs as one container on a Hetzner VPS with secrets in `.env` (`DockerDeployment.md`); there is no key vault.
  A key in `.env` follows the pattern the JWT secret already uses, and a database dump alone does not reveal the OSO
  key (spec Assumptions).
- AES-GCM is authenticated, built into .NET, needs no package, and can be unit-tested without a filesystem.
- The `v1:` prefix leaves room to change the scheme later without guessing.

**Alternatives considered**:
- *ASP.NET Core Data Protection* (`IDataProtector`). The platform standard, with key rotation. But without
  `PersistKeysToFileSystem` on a mounted volume, a container restart makes a new key ring and every stored key becomes
  unreadable, silently. On Linux the key ring is also stored unencrypted unless a certificate is configured. It needs
  a new volume in `compose.prod.yaml`, which the deploy pipeline does not copy. More moving parts for one secret.
- *Plain text in the database*. Fails FR-003.
- *Store the key only in `.env`, not per location*. Simpler, but the spec makes the key a per-location, Admin-managed
  setting that can be replaced without a deploy.

**When the encryption key is missing or wrong**: the app still starts. Connecting a heater fails with
`WaterHeater.EncryptionNotConfigured` (503). Reading or sampling a heater whose key cannot be decrypted fails with
`WaterHeater.KeyUnreadable` (the Admin reconnects), logged once as in R6. Nothing else in the API is affected.

## R2. The OSO HTTP client

**Decision**: a typed `HttpClient` `OsoWaterHeaterClient : IWaterHeaterClient` in `Infrastructure/External`, base
address `https://api.osoenergy.no/water-heater-api/`, header `Ocp-Apim-Subscription-Key` set per request (the key
differs per heater, so not a default header). `HttpClient.Timeout` = 10 s (FR-011, SC-003). Two methods:

- `GetStateAsync(credentials)`: calls `Status`, `CurrentTemperature`, `ActualLoad` and `TappingCapacityKwh` **in
  parallel**, then combines them into one `WaterHeaterReading`. Any failure fails the whole read with the first
  error.
- `GetActualLoadAsync(credentials)`: `ActualLoad` only, for sampling.

`AvailableMixV40Volume` is not called: `Status.availableMix40Volume` has the same number (spec, backlog note).

The client logs nothing that contains the URL. Error mapping, as the ENTSO-E client does:

| OSO answer | Error code | Meaning for the caller |
|---|---|---|
| 401, 403, 404 | `WaterHeater.Rejected` | The key or device id is wrong; an Admin must reconnect |
| 5xx, timeout, `HttpRequestException` | `WaterHeater.Unavailable` | OSO is down or slow; try later |
| 400, other 4xx, unparsable JSON | `WaterHeater.InvalidResponse` | Unexpected answer from OSO |

**Rationale**: four calls in parallel keep the state answer near the slowest single call (SC-003), and the parallel
calls share one 10-second budget through the request's cancellation token.

## R3. Keeping the device id and key out of every log (FR-016)

The device id is in every OSO URL path, and the key is in a request header and the connect request body. Places that
could log them, and what closes each:

1. **`IHttpClientFactory` logging.** By default it logs each request URL at `Information`
   (`System.Net.Http.HttpClient.<name>.LogicalHandler` and `.ClientHandler`). Since .NET 9 the **query string** is
   redacted, but the **path is not**, so the device id would be logged once per minute. **Decision**: register the OSO
   client with `.RemoveAllLoggers()`. The client's own `LogMessages` log the location id, status code and reason code
   only.
2. **Request/response logging middleware.** The connect request body has the key and device id. Request bodies are
   not logged today (`LogRequestBody: false`), but the switch exists. **Decision**: extend `SensitiveData` with
   `CarriesSecretInRequest(method, path)`, true for `PUT /api/admin/locations/{id}/water-heater`, and mask the request
   body for it whatever the options say, as the one-time sensor-key response is masked today. Responses never contain
   the key, and contain the device id only shortened (R4).
3. **Exceptions.** `HttpRequestException` messages can contain the URL. **Decision**: the client catches them and
   logs the reason code, never `ex.Message`, and never passes the exception to the logger. The sampler's last-resort
   catch logs only the exception type (R6).
4. **Errors returned to callers.** Error descriptions are fixed text; none includes the device id or key.

A test (SC-006) runs the connect, read and sample paths with a capturing logger and asserts that neither value
appears in any log entry or in the response body.

## R4. Showing the device id shortened (FR-004)

**Decision**: `DeviceIdHint` = first 6 + `…` + last 6 characters, computed by the mapper from the stored device id.
The device id itself is stored in plain text: it is an identifier, not a credential (it is useless without the key),
and it is needed in every URL. A unique index enforces FR-005.

## R5. Reading OSO's JSON

**Decision**: `System.Text.Json` with `PropertyNameCaseInsensitive = true` and private DTO records in the client that
match OSO's names exactly, including the misspelt `holidyModeStartTime` / `holidyModeEndTime`. The client maps them to
the Application record `WaterHeaterReading` with correct names.

- **Times**: `lastTelemetry.timestamp`, `lastActivityTime` and `timeStamp` carry `Z` and are parsed as UTC. The
  supercharge and holiday times have no zone and are parsed as UTC (`DateTimeStyles.AssumeUniversal |
  AdjustToUniversal`), per the spec's assumption. One helper parses both forms.
- **`temperatureOne` = 0** becomes `null` (FR-009). The other three temperatures are kept even at 0 (a real 0 °C is
  impossible in a working heater, but only the missing sensor is known to report 0).
- **Codes**: `lastTelemetry.control.mode`, `optimizationOption`, `optimizationSubOption` and `profileStatus.id` are
  passed on as `int?` without interpretation (FR-009).
- **`heater`** 0/1 → `IsHeating` bool; **`connectionState`** 0/1 → `IsOnline` bool.
- **Special mode**: `profileStatus.isInHolidayMode` / `isInSuperchargeMode` with their times → `SpecialMode`
  (`None`, `Holiday`, `Supercharge`) plus start and end. "Blue battery" mode is passed as a third value
  `BlueBattery` when `isInBlueBatteryMode` is true (no times are given for it). OSO's `meaning` text is passed on as
  `ProfileName`.

Parsing is tested against the exact JSON answered on 2026-11-19 (stored as test data, device id removed).

## R6. Sampling once a minute

**Decision**: `WaterHeaterSamplingService : BackgroundService` in `Features/WaterHeater/Services`, written like
`PriceFetchService`: a `PeriodicTimer` of `WaterHeater:SampleIntervalSeconds` (default 60), a scope per cycle, and a
last-resort `catch (Exception ex) when (ex is not OperationCanceledException)` that logs only the exception type.

Each cycle:
1. Loads every heater whose location is active (one query).
2. For each heater: decrypts the key, calls `GetActualLoadAsync`, and on success inserts a
   `WaterHeaterPowerSample(locationId, sampledAtUtc = now, powerKw)`. Heaters are sampled one after another (one
   household; no parallelism needed), each wrapped so one failure does not stop the next (scenario 2).
3. Saves all samples of the cycle in one `SaveChangesAsync`.

The sample time is the time OSO was asked (`TimeProvider`), truncated to the second: `ActualLoad` has no timestamp of
its own. A cycle that overruns the next tick simply makes `PeriodicTimer` skip that tick, so there are never two
cycles at once.

**Failure logging (FR-014)**: the service keeps a small in-memory map per location: `failingSince` and
`lastLoggedUtc`. On the first failure it logs `WaterHeaterSampleFailing` (Warning, with the reason code); while still
failing it logs `WaterHeaterSampleStillFailing` at most once an hour, with the minutes since the start; on the first
success after failures it logs `WaterHeaterSampleRecovered` (Information) with how long it failed. The map lives only
in memory: a restart logs the next failure again, which is wanted. The decision logic is a pure class
(`SampleFailureTracker`) with the clock passed in, so it is unit-tested without waiting.

**Rationale**: OSO's telemetry is ~2 minutes old, so 60 s is enough (spec Assumptions); a config value for the
interval is kept only because tests and a local run need a shorter one, as `PriceFetch:IntervalHours` does.

**Alternatives considered**: storing the sample at OSO's `lastTelemetry.timestamp` (needs a second call to `Status`
per minute, and the timestamp often does not change between samples); Quartz/Hangfire (a new package for one timer).

## R7. Who may read the state

**Decision**: the existing `ILocationRepository.IsUserAssociatedAsync` (owner or viewer of an active location) **or**
the Admin role. The endpoint passes `UserId` and `IsAdmin` (`User.IsInRole(RoleNames.Admin)`) on the query; `Caller`
belongs to the Users slice and is not reused across slices (Principle II). Otherwise `404` (FR-010). The spec includes
Admins (US2, scenario 1); other location reads today use only the link, so the Admin rule is a deliberate addition for
this endpoint, kept in its handler.

## R8. Storage

**Decision**: two new tables in one migration `AddWaterHeater`:

- `WaterHeater`: one row per connected heater. Unique on `LocationId` (one heater per location) and on `DeviceId`
  (FR-005). Removing a connection deletes the row (the key goes with it, scenario 6).
- `WaterHeaterPowerSample`: append-only. Index on `(LocationId, SampledAtUtc)` for the later kWh query. **No foreign
  key to `WaterHeater`**, so samples stay when the connection is removed and continue after a reconnect (spec Key
  Entities); a foreign key to `Location` with `Restrict`, as `Measurement` has.

Size: 1,440 rows per heater per day, about 525,000 a year, each ~40 bytes plus index. A plain Postgres table is
fine with no retention job (spec Assumptions: kept indefinitely). It is **not** made a TimescaleDB hypertable now, as
`Measurement` is: nothing aggregates it yet. The later kWh feature can convert it, with the same one-way migration
`Measurement` used, if its queries need a continuous aggregate.

## R9. Configuration

| Key | Where | Default |
|---|---|---|
| `WaterHeater:KeyEncryptionKey` | server `.env` as `WATER_HEATER_KEY_ENCRYPTION_KEY`, mapped in `compose.prod.yaml`; `local.settings.json` locally | none (connect refused with 503 until set) |
| `WaterHeater:SampleIntervalSeconds` | `appsettings.json` | 60 |

Generate the key with `openssl rand -base64 32`. `DockerDeployment.md` §3.4 and `compose.prod.yaml` (pass the variable
to the `api` service) are updated; the server's `.env` is a manual step, as for every secret.
