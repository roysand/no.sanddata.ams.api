# Implementation Plan: Read the Zaptec EV Charger and Optionally Count It with the House

**Branch**: `feature/016-zaptec-ev-charger` | **Date**: 2026-11-20 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/016-zaptec-ev-charger/spec.md`

## Summary

A new `EvChargers` slice reads Roy's Zaptec charger and stores its completed charging sessions, split into kWh per
clock hour. The ElectricityCost figures gain a charger part and a `totalKwh` that includes the car when the location's
checkbox is on. No cost changes.

1. **Sign-in (FR-003, research R1).**
   - The Zaptec e-mail and password come from configuration only (`Zaptec__Username` / `Zaptec__Password` from the
     server's `.env`).
   - `ZaptecEvChargerClient` in Infrastructure fetches an OAuth password-grant token, caches it in memory until 5
     minutes before it expires, and never logs URLs, headers or bodies.
2. **Connect (US1, FR-001/002/004/005).**
   - Admin endpoints list the chargers the sign-in sees, and connect, show or remove one charger per location.
   - The `EvCharger` row holds ids and names only.
3. **Read and store (US2, FR-006–FR-009, research R2–R5).**
   - `EvChargerReadService` runs every 15 minutes. It calls `GET /api/sessions/archived` per connected charger, from
     a watermark minus 24 h, or from the start of the previous Oslo month on the first read.
   - Each session is split by the pure `ChargingEnergySplitter` into `ChargingSessionHour` rows, and replaced in
     place when read again.
   - Failures store nothing, leave the watermark where it is, and are logged at a limited rate.
4. **Count with the house (US3, FR-010–FR-016, research R6–R9).**
   - The setting is `Location.IncludeEvChargerInTotals`, changed through `PUT
     /api/locations/{id}/ev-charger/include` by an owner or an Admin.
   - `HourlyCostProvider` gets a `withEvCharger` flag and attaches an `EvChargerHour` to each hour.
   - Hourly, current, daily and bill responses gain `evCharger` and `totalKwh`.
   - `consumptionKwh` and every cost keep their meaning, so the setting can never change a cost.
5. **Sessions list (FR-015).** `GET /api/locations/{id}/ev-charger/sessions`.

Before any code: [quickstart.md](quickstart.md) §0 tries the Zaptec calls by hand with Roy's login. It checks that a
homeowner login may call the data endpoints and how dense `energyDetails` is. If the data calls are refused, stop and
go to the UserGroup route first.

## Technical Context

**Language/Version**: C# 14 / .NET 10, ASP.NET Core 10

**Primary Dependencies**: FastEndpoints, EF Core (Npgsql), FluentValidation, custom CQRS with
`Cqrs.SourceGenerator`, and `IHttpClientFactory` typed clients. **No new packages.**

**Storage**: PostgreSQL (TimescaleDB image). One migration `AddEvCharger`:
- 3 tables: `EvCharger`, `ChargingSession` and `ChargingSessionHour`;
- 1 column: `Location.IncludeEvChargerInTotals`.

Data access is LINQ only, with no raw SQL ([data-model.md](data-model.md)).

**Testing**: xUnit + NSubstitute in `tests/Features.Tests`:
- `ChargingEnergySplitter` and `EvChargerShare`: pure tests.
- Handlers: tests with a substituted `IEvChargerClient` and substituted repositories.
- Read service: one cycle with fakes, checking that a failure stores nothing, the watermark, and discarded sessions.
- Zaptec JSON parsing: tests against a canned answer taken from quickstart §0.
- Cost handlers: `totalKwh` and `evCharger` with the setting on and off, and costs identical in both.

There is no database test harness, as before. The `GROUP BY` and upsert are checked by hand in quickstart §2.

**Target Platform**: a Linux container on the Hetzner VPS (`compose.prod.yaml`). It gains 2 environment variables.

**Project Type**: web-service (API). The frontend checkbox and charger card are a separate frontend spec.

**Performance Goals**: The added work per request is small:
- the cost endpoints make one extra indexed `GROUP BY` over at most about 9,000 session-hours a year;
- the job makes one token call an hour and 1–2 data calls per cycle, far under Zaptec's limit of 10 requests per
  second.

**Constraints**:
- **Response fields:** none change meaning, and the new fields are added only (contracts).
- **Costs:** identical with the setting on or off, to the øre (SC-004).
- **Secrets:** the Zaptec e-mail, password and tokens are never in the database or any log (FR-003, FR-018).
- **Zaptec Fair Use:** use only the documented calls, no aggressive polling, and reuse the token.

**Scale/Scope**: one household and one charger, a few hundred sessions a year. The work is:
- **Domain:** 3 entities, and a column on `Location`.
- **Application:** 2 interfaces, 3 repository/client contracts, the splitter, the share and the types.
- **Infrastructure:** the client with its options and token cache, 2 repositories, 3 configurations and the
  migration.
- **Features:** 6 endpoints and the read service.
- **ElectricityCost:** 1 flag on the provider, and 4 responses that each gain 2 fields.
- **Docs:** the EventId table.

## Constitution Check

_GATE: Must pass before Phase 0 research. Re-check after Phase 1 design._

| Principle | How the plan meets it | Status |
|---|---|---|
| I. Clean layering | Entities in Domain; `IEvChargerClient`, the repository interfaces, the splitter and the types in Application; the Zaptec HTTP client, the options, the token cache and EF in Infrastructure; endpoints, handlers and the job in `Features/EvChargers`. | ✅ |
| II. Vertical slices | `Features/EvChargers` owns its commands, queries, handlers, endpoints, validators, mappers and log messages. ElectricityCost reads charger hours only through `IChargingSessionRepository` (Application), as it reads heater samples. The failure tracker is copied, not shared with WaterHeaters (research R10). | ✅ |
| III. Results over exceptions | The client returns `Result<T>` with `EvChargerErrors` for every HTTP, timeout and parsing failure, and never throws for them. The job's last-resort catch is only for the host's sake, as in spec 011. | ✅ |
| IV. Validate at the boundary | Validators check the connect request (`chargerId` not empty), the include request, and the sessions range (`from < to`, at most 366 days). The handlers do not validate again. Checking the charger against Zaptec is a business rule, so it is done in the handler as a `Result`. | ✅ |
| V. CQRS | Commands: connect, remove and set-include. Queries: list chargers, settings and sessions. The job writes through repositories, outside CQRS, as the water heater sampler does. All handlers are registered by the generator. | ✅ |
| VI. Logging | `LoggerMessage` delegates in `Features/EvChargers/Logging/LogMessages.cs` and `Infrastructure/External/Logging/ZaptecLogMessages.cs`, using EventIds 1900–1999, which are added to the table. Reason codes are stable. Nothing secret is logged, and the HttpClient has `RemoveAllLoggers()`. | ✅ |
| VII. Manual mapping | `EvChargerMapper`, plus additions to `CostMapper`, `BillMapper` and `LocationMapper`. | ✅ |
| VIII. Minimal footprint | No pricing, no Service Bus, no live session and no multi-charger support. There are 2 configuration keys: the sign-in. The read interval key matches the sampler's pattern, for testing. | ✅ |
| Stack: migrations only | One `AddEvCharger` migration, shipped in the same commit as the entities. | ✅ |

**Re-check after Phase 1**: no violations. Complexity Tracking stays empty.

## Project Structure

### Documentation (this feature)

```text
specs/016-zaptec-ev-charger/
├── plan.md              # This file
├── research.md          # Phase 0: R1–R10
├── data-model.md        # Phase 1: entities, types, errors, rules
├── quickstart.md        # Phase 1: try Zaptec by hand, then check end to end
├── contracts/
│   └── endpoints.md     # Phase 1: new endpoints and added fields
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/Domain/Common/Entities/
├── EvCharger.cs                         # new: the connection (ids, names, ReadUpToUtc)
├── ChargingSession.cs                   # new: one Zaptec session, owns its hours
├── ChargingSessionHour.cs               # new
└── Location.cs                          # + IncludeEvChargerInTotals, SetIncludeEvCharger

src/Application/
├── Common/Interfaces/External/IEvChargerClient.cs            # new: read-only
├── Common/Interfaces/Repositories/IEvChargerRepository.cs    # new
├── Common/Interfaces/Repositories/IChargingSessionRepository.cs  # new: upsert, hourly energy, sessions
└── EvChargers/
    ├── EvChargerModels.cs               # new: ZaptecCharger, ZaptecSession, EnergyPoint, EvChargerErrors, …
    ├── ChargingEnergySplitter.cs        # new: session → kWh per clock hour (research R5)
    └── EvChargerShare.cs                # new: EvChargerHour, EvChargerTotal, Sum

src/Infrastructure/
├── External/ZaptecEvChargerClient.cs    # new: token, chargers, archived sessions (research R1/R2)
├── External/ZaptecOptions.cs            # new: Username, Password
├── External/ZaptecTokenCache.cs         # new: singleton
├── External/Logging/ZaptecLogMessages.cs # new: 1920–1929
├── Database/Configuration/EvChargerConfiguration.cs, ChargingSessionConfiguration.cs,
│   ChargingSessionHourConfiguration.cs, LocationConfiguration.cs (+ column)
├── Database/Repositories/EvChargerEfRepository.cs, ChargingSessionEfRepository.cs
├── Database/Migrations/<timestamp>_AddEvCharger.cs
└── AddInfrastructureToDI.cs             # + options, cache, typed client (RemoveAllLoggers), repositories

src/Features/EvChargers/
├── Commands/, Queries/, Handlers/, Validators/, Mappers/EvChargerMapper.cs
├── Endpoints/EvChargerAdminEndpoints.cs   # list, connect, settings, remove
├── Endpoints/EvChargerEndpoints.cs        # include, sessions
├── Services/EvChargerReadService.cs, ReadFailureTracker.cs
└── Logging/LogMessages.cs                 # 1900–1919

src/Features/ElectricityCost/
├── Services/HourlyCostProvider.cs        # + withEvCharger; returns HourlyCosts; HourCost.EvCharger
├── Queries/GetHourlyCostQuery.cs, GetCurrentHourCostQuery.cs, GetDailyCostQuery.cs, GetMonthlyBillQuery.cs
│                                         # + totalKwh, evCharger records
├── Mappers/CostMapper.cs, BillMapper.cs
└── Handlers/ hourly, current, daily, bill (+ pass withEvCharger); saving handler adapts to HourlyCosts
src/Features/Locations/Queries/GetMyLocationsQuery.cs, Mappers/LocationMapper.cs   # + hasEvCharger, includeEvCharger
src/Features/ServiceCollectionExtensions.cs  # + AddHostedService<EvChargerReadService>

compose.prod.yaml                         # + Zaptec__Username / Zaptec__Password from .env
CLAUDE.md, DevelopmentGuide.md            # + EventId range 1900–1999 EvCharger; DockerDeployment.md + the 2 variables

tests/Features.Tests/EvChargers/          # splitter, share, handlers, read service, client parsing
tests/Features.Tests/ElectricityCost/     # + charger cases
```

**Structure Decision**: The charger connection, the reading and the sessions list form a new vertical slice,
`Features/EvChargers`, mirroring `Features/WaterHeaters`. The part shown in the figures is a property of the cost
figures, so it extends the existing ElectricityCost slice through Application interfaces, as spec 012 did for the
heater.

## Complexity Tracking

No violations to justify.
