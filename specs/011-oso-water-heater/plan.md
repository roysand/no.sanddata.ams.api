# Implementation Plan: Read the OSO Water Heater

**Branch**: `feature/011-oso-water-heater-plan` | **Date**: 2026-11-19 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/011-oso-water-heater/spec.md`

## Summary

A new `WaterHeater` slice that connects one OSO water heater per location, reads its state live from OSO's inCharge
API, and samples its power every minute.

1. **Connect (US1).** Admin endpoints `PUT`, `GET` and `DELETE /api/admin/locations/{id}/water-heater`. Connecting
   checks the key and device id with OSO's `Status` call, then stores the device id and the key encrypted with
   AES-256-GCM. The encryption key comes from the server's `.env` (research R1: Hetzner VPS, no key vault). Responses
   show the device id shortened and never the key.
2. **Read (US2).** `GET /api/locations/{id}/water-heater` for owners, viewers and Admins. It makes four OSO calls in
   parallel within a 10-second budget and returns one cleaned state: OSO's misspellings fixed, the missing sensor
   given as `null`, the reading's age and a stale flag. OSO failures come back as `502`/`503` with stable codes.
3. **Sample (US3).** `WaterHeaterSamplingService`, a `BackgroundService` shaped like `PriceFetchService`. Once a
   minute it stores `ActualLoad` for each active location's heater. A failed sample stores nothing, and failure
   logging is rate-limited by a pure `SampleFailureTracker`.

Every path to a log is closed for the key and device id. That covers the HttpClient factory's URL logging
(`RemoveAllLoggers`), the request-body logging (`SensitiveData`), exception messages and a credentials record whose
`ToString()` hides its values (research R3).

## Technical Context

**Language/Version**: C# 14 / .NET 10, ASP.NET Core 10

**Primary Dependencies**: FastEndpoints, FluentValidation, EF Core (Npgsql), custom CQRS with `Cqrs.SourceGenerator`,
`IHttpClientFactory`, `System.Text.Json`, `System.Security.Cryptography.AesGcm`. **No new packages.**

**Storage**: PostgreSQL (TimescaleDB image). Two new plain tables, `WaterHeater` and `WaterHeaterPowerSample`, and
one migration, `AddWaterHeater` (data-model.md).

**Testing**: xUnit + NSubstitute in `tests/Features.Tests`. A stub `HttpMessageHandler` replays the real OSO answers
from 2026-11-19. Tests also use `FixedTimeProvider` and `EndpointHost` for the HTTP tests, and a capturing
`ILogger` for SC-006. The new `SensitiveData` rule is covered in the existing
`tests/Features.Tests/Authentication/RequestLoggingRedactionTests.cs`. The check against the real
heater is a manual quickstart step.

**Target Platform**: a Linux container on a Hetzner VPS (`compose.prod.yaml`: Caddy, API, Postgres), with secrets in
`/opt/ams-api/.env`.

**Project Type**: web-service (API). No frontend change; a frontend spec follows.

**Performance Goals**: state answered within 3 s when OSO is working and within 11 s with an error when it is not
(SC-003); one OSO call per heater per minute for sampling.

**Constraints**: the key and device id are never logged or returned in full (FR-003, FR-004, FR-016). No OSO call
that changes the heater (FR-015): `IWaterHeaterClient` has only read methods. OSO being down must never stop the host
(SC-005).

**Scale/Scope**: one household, one heater, about 525,000 samples a year. The work is 2 entities, 1 migration, 1
Application client interface, 1 protector interface, 2 repositories, 1 HTTP client, 1 protector, 3 commands and
queries for Admin plus 1 query for reading the state, 4 endpoints, 1 validator, 1 mapper, 1 background service, 1
pure tracker, and 1 `SensitiveData` rule.

## Constitution Check

*GATE: passes before Phase 0 and after Phase 1.*

| Principle | Status |
|---|---|
| I. Clean Architecture layering | Pass. The entities are in Domain with no crypto or HTTP. `IWaterHeaterClient`, `ISecretProtector`, the repositories and the `WaterHeaterReading`/`WaterHeaterCredentials` records are in Application. The OSO client, AES-GCM protector, EF configurations and migration are in Infrastructure. Handlers, endpoints and the sampler are in `Features/WaterHeater`. |
| II. Vertical slices | Pass. A new slice that reaches into no other slice. The location-access check uses the shared `ILocationRepository.IsUserAssociatedAsync`. `Caller` (Users slice) is not reused: the endpoint passes `UserId` and `IsAdmin` itself (R7). |
| III. Result over exceptions | Pass. OSO failures, a missing encryption key, an undecryptable key, a device in use and a heater that isn't connected are all `Error` values. The client catches `HttpRequestException`/timeouts/`JsonException` and returns `Result`. The sampler's catch-all is the last-resort guard that `PriceFetchService` has too. |
| IV. Validate once | Pass. The validator checks the presence, length and hex format of the key and device id. OSO acceptance and device-in-use depend on outside state, so the handler decides them. |
| V. CQRS, compile-time dispatch | Pass. `ConnectWaterHeaterCommand` and `RemoveWaterHeaterCommand` mutate. `GetWaterHeaterSettingsQuery` and `GetWaterHeaterStateQuery` only read: the state query calls OSO but stores nothing. Handlers are generated-registered. |
| VI. Structured logging | Pass. New `LogMessages` in the range **1800–1899 (WaterHeater)**, added to the `CLAUDE.md` and `DevelopmentGuide.md` tables. Stable reason codes are `Rejected`, `Unavailable`, `InvalidResponse`, `KeyUnreadable` and `Unexpected`. No log carries the key, device id, URL or exception message. |
| VII. Manual mapping | Pass. `WaterHeaterMapper` builds the settings and state responses, the device-id hint, age and stale flag. |
| VIII. Minimal footprint | Pass. No package, no generic "external device" abstraction, no caching, no retention job, no hypertable yet. The only config values are the encryption key (required) and the sample interval (needed by tests and local runs, as `PriceFetch:IntervalHours` is). |

**Deliberate addition, not a deviation**: the state endpoint lets an unlinked Admin read, unlike the other location
reads (spec US2, research R7). This is kept in that one handler.

Post-design re-check: still passes. Phase 1 added no package or cross-slice reference.

## Project Structure

### Documentation (this feature)

```text
specs/011-oso-water-heater/
├── plan.md              # This file
├── research.md          # Phase 0
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── contracts/
│   └── endpoints.md     # Phase 1
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/Domain/Common/Entities/
├── WaterHeater.cs                                   # LocationId, DeviceId, ProtectedSubscriptionKey, Replace()
└── WaterHeaterPowerSample.cs                        # LocationId, SampledAtUtc, PowerKw
src/Application/
├── Common/Interfaces/External/IWaterHeaterClient.cs # GetStateAsync, GetActualLoadAsync (read only)
├── Common/Interfaces/Security/ISecretProtector.cs   # Protect / Unprotect(Result) with associated data
├── Common/Interfaces/Repositories/IWaterHeaterRepository.cs
├── Common/Interfaces/Repositories/IWaterHeaterPowerSampleRepository.cs
└── WaterHeater/WaterHeaterModels.cs                 # WaterHeaterCredentials, WaterHeaterReading, SpecialMode, OsoCodes
src/Infrastructure/
├── External/OsoWaterHeaterClient.cs                 # typed HttpClient, DTOs with OSO's spellings, error mapping
├── External/Logging/LogMessages.cs                  # + OSO client entries (1800 range)
├── Security/AesGcmSecretProtector.cs                # WaterHeater:KeyEncryptionKey, v1: format
├── Logging/SensitiveData.cs                         # + CarriesSecretInRequest (PUT …/water-heater)
├── Middleware/RequestResponseLoggingMiddleware.cs   # mask request body when CarriesSecretInRequest
├── Database/Configuration/WaterHeaterConfiguration.cs
├── Database/Configuration/WaterHeaterPowerSampleConfiguration.cs
├── Database/Repositories/WaterHeaterEfRepository.cs
├── Database/Repositories/WaterHeaterPowerSampleEfRepository.cs
├── Database/ApplicationDbContext.cs                 # + two DbSets
├── Database/Migrations/<timestamp>_AddWaterHeater.cs
└── AddInfrastructureToDI.cs                         # client (Timeout 10 s, RemoveAllLoggers), protector, repos
src/Features/WaterHeater/
├── Commands/WaterHeaterCommands.cs                  # ConnectWaterHeaterCommand, RemoveWaterHeaterCommand
├── Queries/WaterHeaterQueries.cs                    # settings + state queries, response records
├── Handlers/ConnectWaterHeaterCommandHandler.cs
├── Handlers/RemoveWaterHeaterCommandHandler.cs
├── Handlers/GetWaterHeaterSettingsQueryHandler.cs
├── Handlers/GetWaterHeaterStateQueryHandler.cs
├── Endpoints/WaterHeaterAdminEndpoints.cs           # PUT / GET / DELETE, StatusOf(error) → 400/404/409/502/503
├── Endpoints/GetWaterHeaterStateEndpoint.cs
├── Validators/ConnectWaterHeaterValidator.cs
├── Mappers/WaterHeaterMapper.cs
├── Logging/LogMessages.cs                           # 1800–1899
└── Services/
    ├── WaterHeaterSamplingService.cs                # BackgroundService, PeriodicTimer
    └── SampleFailureTracker.cs                      # pure: start / hourly / recovered decisions
src/Features/ServiceCollectionExtensions.cs          # + AddHostedService<WaterHeaterSamplingService>
src/api/appsettings.json                             # + WaterHeater:SampleIntervalSeconds = 60
compose.prod.yaml                                    # + WaterHeater__KeyEncryptionKey: ${WATER_HEATER_KEY_ENCRYPTION_KEY}
DockerDeployment.md                                  # + the .env line and how to generate it
CLAUDE.md, DevelopmentGuide.md                       # + EventId range 1800–1899 WaterHeater
tests/Features.Tests/WaterHeater/
├── TestData/oso-*.json                              # the 2026-11-19 answers, device id removed
├── OsoWaterHeaterClientTests.cs                     # parsing, null fourth sensor, UTC times, error mapping, timeout
├── AesGcmSecretProtectorTests.cs                    # round trip, tamper, wrong location, missing/bad key
├── ConnectWaterHeaterHandlerTests.cs                # accept, reject, unavailable, replace, device in use
├── GetWaterHeaterStateHandlerTests.cs               # access, not connected, age/stale, error codes
├── SampleFailureTrackerTests.cs                     # first failure, hourly, recovered
├── WaterHeaterSamplingServiceTests.cs               # gap on failure, others continue, inactive skipped
├── NoSecretsInLogsTests.cs                          # SC-006 with a capturing logger
└── ../Http/WaterHeaterHttpTests.cs                  # 200/400/401/403/404/409/502/503
tests/Features.Tests/Authentication/RequestLoggingRedactionTests.cs  # + request body masked for PUT …/water-heater
```

**Structure Decision**: the existing Clean Architecture layout. The water heater is a new slice. The OSO client sits
next to the ENTSO-E and Norges Bank clients, and the protector in a new `Infrastructure/Security` folder.

## Delivery order

Each step builds and passes its tests on its own:

1. **Foundation**: entities, configurations, migration `AddWaterHeater`, repositories, `ISecretProtector` with
   `AesGcmSecretProtector`, config and compose entries.
2. **OSO client**: `IWaterHeaterClient`, `OsoWaterHeaterClient` with parsing and error mapping against the recorded
   answers, and DI registration with `RemoveAllLoggers` and the 10 s timeout.
3. **US1 connect**: commands, settings query, validator, Admin endpoints, and the `SensitiveData` rule with its
   middleware change.
4. **US2 state**: state query, handler, mapper, endpoint.
5. **US3 sampling**: `SampleFailureTracker`, `WaterHeaterSamplingService`, hosted-service registration.
6. **Polish**: `NoSecretsInLogsTests` (SC-006), EventId tables, `DockerDeployment.md`, removing the backlog note,
   `dotnet format`, and the quickstart against Roy's heater (including the supercharge time-zone check).

## Complexity Tracking

No constitution violations to justify.
