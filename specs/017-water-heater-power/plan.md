# Implementation Plan: The Water Heater's Power over the Last Hours

**Branch**: `feature/017-water-heater-power` | **Date**: 2026-11-20 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/017-water-heater-power/spec.md`

## Summary

A new read-only endpoint, `GET /api/locations/{locationId}/water-heater/power?from=&to=`, gives the water heater's
average power per 5 minutes over a window of at most 48 hours (default: the last 3 hours). The frontend's
"Last 3 hours" card can then draw water heating under the meter's 5-minute power line.

How it works:
- **One SQL query.** Spec 012's hourly SQL becomes one private query that takes the step size and buckets with
  `date_bin`. The hourly method calls it with 1 hour and the new method with 5 minutes. The hold rule is therefore
  shared, and the 5-minute kWh adds up to the hour's (research R1).
- **Shaping.** The handler turns kWh and sampled seconds into `powerKw` and whole `sampledSeconds` (R2).
- **The window.** One small helper resolves the default window and checks it, for both the validator and the
  handler (R3).
- **Access.** The rule is the same as for the charger's sessions (R4).
- Nothing is stored, and there is no migration.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: FastEndpoints, FluentValidation, and EF Core (Npgsql) with the existing raw SQL query
through `Database.SqlQuery<T>`

**Storage**: PostgreSQL (TimescaleDB image, PG16 locally, PG17 in production). It reads `WaterHeaterPowerSample`
through the existing index `(LocationId, SampledAtUtc)`. No new table and no migration.

**Testing**: xUnit + NSubstitute in `tests/Features.Tests`:
- pure tests for `PowerWindow`;
- handler tests with substituted repositories;
- HTTP tests through `EndpointHost`.

The SQL has no database test harness, as in spec 012. The quickstart checks it, including that the hourly figures are
unchanged.

**Target Platform**: Linux container on the Hetzner VPS behind Caddy

**Project Type**: web service (REST API)

**Performance Goals**: a 3-hour window in < 300 ms p95 in production (SC-003). The app refreshes it every minute.

**Constraints**:
- No existing endpoint or field changes (FR-006).
- Hourly results must stay identical after the SQL refactor.

**Scale/Scope**:
- About 180 samples per 3-hour window and at most about 2,900 per 48 hours.
- At most 576 items in an answer.
- 1 endpoint, 1 query, 1 validator, 1 handler, 1 repository method and 1 record.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | How this plan meets it | Status |
|---|---|---|
| I. Clean layering | The SQL stays in `Infrastructure/.../WaterHeaterPowerSampleEfRepository`. The interface method and the `WaterHeaterPowerStep` record are in Application. There is no EF and no HTTP in Application or Domain. | ✅ |
| II. Vertical slices | Everything new is in `Features/WaterHeaters`: endpoint, query, handler, validator, mapper and `PowerWindow`. It does not reach into `ElectricityCost`. The access check is a few lines, copied in the style of spec 016's sessions handler and not shared. | ✅ |
| III. Result over exceptions | The handler returns `Result<WaterHeaterPowerResponse>`. Not found is `Error.NotFound("Location.NotFound", …)`, and the endpoint maps it with `AddError` + `ThrowIfAnyErrors`. | ✅ |
| IV. Validate once | `GetWaterHeaterPowerValidator` refuses bad windows, using `PowerWindow` and `TimeProvider`. The handler only fills in the same defaults and never re-checks (R3). | ✅ |
| V. CQRS, compile-time dispatch | `GetWaterHeaterPowerQuery` reads only. The source generator picks up the handler, with no manual registration. | ✅ |
| VI. Structured logging | Refused access is logged with the existing `LocationAccessDenied` (EventId 1805). There is no new EventId and no PII. | ✅ |
| VII. Manual mapping | `WaterHeaterMapper.ToPowerItem` maps a step to an item: kW, rounding, and dropping 0-second steps. | ✅ |
| VIII. Minimal footprint | One step size (5 minutes), no option for others, no cache, no new index and no stored rollup. Sharing the SQL removes a copy instead of adding an abstraction. | ✅ |
| Stack: raw SQL behind EF Core | Spec 012 already justified this deviation. This plan adds no new raw SQL: it adds a step parameter to the existing query and uses `date_bin` (PG14+). | ⚠️ already justified |
| Workflow: format and build | `dotnet format --verify-no-changes` and `dotnet build` with no new warnings, before the work is done. | ✅ |

**Post-design re-check**: the design artifacts (research, data model, contract, quickstart) add no violations. The
gate passes.

## Project Structure

### Documentation (this feature)

```text
specs/017-water-heater-power/
├── spec.md
├── plan.md              # this file
├── research.md          # R1–R6
├── data-model.md
├── quickstart.md
├── contracts/
│   └── endpoints.md
├── checklists/
│   └── requirements.md
└── tasks.md             # from /speckit-tasks (not created here)
```

### Source Code (repository root)

```text
src/Application/
├── WaterHeaters/WaterHeaterPowerStep.cs                     # new record: StepStartUtc, Kwh, SampledSeconds
└── Common/Interfaces/Repositories/
    └── IWaterHeaterPowerSampleRepository.cs                 # + GetPowerStepsAsync(location, from, to, now, ct)

src/Infrastructure/Database/Repositories/
└── WaterHeaterPowerSampleEfRepository.cs                    # private step query with date_bin; hourly + 5-min call it

src/Features/WaterHeaters/
├── Endpoints/GetWaterHeaterPowerEndpoint.cs                 # new: GET …/water-heater/power, request record
├── Queries/WaterHeaterQueries.cs                            # + GetWaterHeaterPowerQuery, response + item records
├── Handlers/GetWaterHeaterPowerQueryHandler.cs              # new: access, window, repository, mapping
├── Validators/GetWaterHeaterPowerValidator.cs               # new: uses PowerWindow + TimeProvider
├── Mappers/WaterHeaterMapper.cs                             # + ToPowerItem
└── Services/PowerWindow.cs                                  # new: defaults (3 h, now) and the 48 h / order / future checks

tests/Features.Tests/
├── WaterHeaters/PowerWindowTests.cs                         # new
├── WaterHeaters/GetWaterHeaterPowerHandlerTests.cs          # new
└── Http/WaterHeaterPowerHttpTests.cs                        # new
```

**Structure Decision**: The existing Clean Architecture projects and the `Features/WaterHeaters` slice from specs
011/012. There is no new project. The only change outside new files is the shared step query in the existing
repository and one interface method.

## Complexity Tracking

> Spec 012 already justified the raw SQL, and this plan adds no new violation.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Refactor of spec 012's hourly SQL into a step-parameterized query | The 5-minute steps need the same hold rule, and SC-002 needs them to add up to the hour exactly | A copied query would give two copies of the hardest SQL in the repo, which can drift. Bucketing in C# would be a third copy of the hold rule. |
