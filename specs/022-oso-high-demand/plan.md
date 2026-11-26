# Implementation Plan: Turn the Water Heater's High Demand On and Off

**Branch**: `feature/022-oso-high-demand` | **Date**: 2026-11-26 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/022-oso-high-demand/spec.md`

## Summary

An Admin turns the OSO water heater's High Demand on or off. Two new Admin endpoints,
`PUT` and `DELETE /api/admin/locations/{locationId}/water-heater/high-demand`, send OSO's undocumented
`POST` or `DELETE 1/Device/{deviceId}/HighDemand/` once, with no retry. Then they read the heater's state back with the
existing state call and answer with it, so the Heater tab updates at once (research R1–R5).
- **The client:** `IWaterHeaterClient` gets one write method, `SetHighDemandAsync`. It's the only exception to spec
  011's read-only rule (FR-008, research R9).
- **A removed call:** OSO answering 404 to the switch gets its own error, `WaterHeater.HighDemandNotOffered`, since the
  calls are undocumented (R2).
- **Logs:** each switch logs who, where, on or off, and the outcome (1807 and 1808), never the key or device id (R7).
- **Nothing stored:** there is no migration.
- **The real-heater test (FR-009):** quickstart §3. It decides whether the frontend shows the switch.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: ASP.NET Core, FastEndpoints, FluentValidation, the custom CQRS with `Cqrs.SourceGenerator`,
and the existing typed `HttpClient` for OSO (spec 011)

**Storage**: none (FR-010). Reads the existing `WaterHeater` connection (key and device id) only.

**Testing**: xUnit + NSubstitute. The client is tested with `OsoStubHandler`, the HTTP pipeline with the in-process
FastEndpoints host (`FastEndpointsHostCollection`), and the logs with `NoSecretsInLogsTests`.

**Target Platform**: Linux container on the Hetzner VPS (production)

**Project Type**: web service (REST API)

**Performance Goals**: a switch answers within 5 seconds including the read-back (SC-003). That's one OSO write plus
the four parallel state reads; the 10-second client timeout covers OSO being slow.

**Constraints**:
- The calls are undocumented (R1).
- The write is sent once (R3).
- The key and device id are never logged (spec 011, FR-016).
- Admin only (FR-003).

**Scale/Scope**: one heater (Hjemme), switched a few times a month.

## Constitution Check

_GATE: Must pass before Phase 0 research. Re-check after Phase 1 design._

| Principle | How the plan meets it | Status |
|---|---|---|
| I. Clean layering | **Application:** the interface method and the new error. **Infrastructure:** the HTTP call. **Features:** the command, handler, endpoints and log events. Domain is unchanged. | ✅ |
| II. Vertical slices | Everything is in `Features/WaterHeaters`. The read-back uses the slice's own `WaterHeaterMapper.ToState`. | ✅ |
| III. Results over exceptions | `SetHighDemandAsync` returns `Result` for every HTTP, timeout and status failure, as `GetAsync` does. The handler returns `Result<T>`. | ✅ |
| IV. Validate at the boundary | The only input is the route's `locationId`, read with `Route<Guid>` as in `RemoveWaterHeaterEndpoint`: a value that isn't a GUID gives a 400 before the handler, and there's no body to validate. The role is checked by `Roles(Admin)` before the handler. The handler checks business rules only: the location exists and a heater is connected. | ✅ |
| V. CQRS | `SetWaterHeaterHighDemandCommand` changes state at OSO and returns `Result<T>`. Its handler is registered by the generator. No query writes. | ✅ |
| VI. Logging | `LoggerMessage` events 1807 and 1808 in WaterHeater's 1800–1899. The client reuses 1820–1822 with `call = "HighDemand"`. They log ids, on or off, and reason codes only. | ✅ |
| VII. Manual mapping | The response is built in `WaterHeaterMapper`, reusing `ToState`. | ✅ |
| VIII. Minimal footprint | There is one interface method, no new `HttpClient`, no configuration flag, no table, no background job and no package. Turning the feature off is the frontend's job (spec Assumptions). | ✅ |
| Stack: migrations only | No schema change. | ✅ |

**Re-check after Phase 1**: there are no violations, so Complexity Tracking stays empty.

## Project Structure

### Documentation (this feature)

```text
specs/022-oso-high-demand/
├── plan.md              # This file
├── research.md          # Phase 0: R1–R9, including the hand test of 2026-11-26
├── data-model.md        # Phase 1: interface change, error, command, response, rules
├── quickstart.md        # Phase 1: tests, local, the real-heater test (FR-009), logs
├── contracts/
│   └── endpoints.md     # Phase 1: the two endpoints
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/Application/
├── Common/Interfaces/External/IWaterHeaterClient.cs   # + SetHighDemandAsync; summary names the exception
└── WaterHeaters/WaterHeaterModels.cs                  # + WaterHeaterErrors.HighDemandNotOffered

src/Infrastructure/External/
└── OsoWaterHeaterClient.cs                            # + SetHighDemandAsync (POST/DELETE, once), 404 → own error

src/Features/WaterHeaters/
├── Commands/WaterHeaterCommands.cs                    # + SetWaterHeaterHighDemandCommand, WaterHeaterHighDemandResponse
├── Handlers/SetWaterHeaterHighDemandCommandHandler.cs # new
├── Endpoints/WaterHeaterHighDemandEndpoints.cs        # new: PUT (on) and DELETE (off), Admin only
├── Endpoints/WaterHeaterAdminEndpoints.cs             # WaterHeaterErrorStatus: HighDemandNotOffered → 502
├── Mappers/WaterHeaterMapper.cs                       # + ToHighDemandResponse
└── Logging/LogMessages.cs                             # + 1807 Switched, 1808 Failed

tests/Features.Tests/
├── WaterHeaters/OsoStubHandler.cs                     # records the method; answers "HighDemand" (trailing slash)
├── WaterHeaters/OsoWaterHeaterClientTests.cs          # + switch tests
├── WaterHeaters/SetWaterHeaterHighDemandHandlerTests.cs  # new
├── WaterHeaters/NoSecretsInLogsTests.cs               # + switch scenarios
└── Http/WaterHeaterHighDemandHttpTests.cs             # new, in FastEndpointsHostCollection

DevelopmentGuide.md                                    # Water heater section: the two endpoints
```

**Structure Decision**: the existing `WaterHeaters` slice, as for every heater feature so far (specs 011, 012, 015,
017, 020). No new project.

## Phases

1. **Foundational**:
   - the interface method, the error, and the client with its tests;
   - the stub learns methods and the trailing slash.
2. **US1 + US2** (on and off): the command, handler, endpoints, mapper and logs, each tested first.
   - On and off share one handler, so the two stories are built together.
3. **US3** (Admin only): the HTTP tests for 401 and 403, and that OSO isn't called.
4. **Polish:**
   - `NoSecretsInLogsTests`, the development guide, then build, format and test;
   - the PR, then the deploy;
   - Roy's real-heater test (quickstart §3, FR-009);
   - the frontend handover note in `specs/_backlog/`.

## Complexity Tracking

No violations.
