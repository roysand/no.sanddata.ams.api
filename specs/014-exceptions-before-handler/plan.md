# Implementation Plan: Catch Failures During Sign-In Checks

**Branch**: `feature/014-exceptions-before-handler` | **Date**: 2026-11-20 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/014-exceptions-before-handler/spec.md`

## Summary

`UseExceptionHandling()` moves one step earlier, in front of `UseAuthorization()`. That is where the API-key lookup
actually runs (research R1, confirmed by the baseline stack trace). The five pipeline lines move into one method,
`UseRequestPipeline`, so the tests run the same order as `Program.cs` (R4).

1. **Order (FR-001, FR-002)**: the order is CORS → authentication → request log → **exception handler** →
   authorization. A failing key lookup now gets spec 013's fixed 500 and one 2020 entry, in every environment. The
   baseline returned an empty body in Production and the full stack trace in Development.
2. **Request log (FR-003)**: no code change. It is still outside the handler, so it logs the normal 2002 with the
   fixed body, not its `catch` path (R2).
3. **CORS (FR-004)**: no code change expected. The headers are added when the response starts, so `Clear()` doesn't
   remove them, as the baseline shows. A test pins this, and the fallback is in R3 if it fails.
4. **Tests (FR-005–FR-007)**: `ExceptionPipelineTests` gets a host built on `UseRequestPipeline`:
   - the real JWT and API-key handlers;
   - a substituted key repository that throws or returns `null`;
   - the real `IngestMeasurementsEndpoint`;
   - a fallback terminal delegate, so spec 013's cases keep running (R5).
5. **Docs and wrap-up (FR-008, FR-009)**:
   - a "Request pipeline order" subsection in `DevelopmentGuide.md` (R6);
   - the backlog note moves into this folder.

## Technical Context

**Language/Version**: C# 14 / .NET 10, ASP.NET Core 10

**Primary Dependencies**: ASP.NET Core middleware (`UseCors`, `UseAuthentication`, `UseAuthorization`), FastEndpoints
7.1.1, and the existing `ApiKeyAuthenticationHandler`. **No new packages.** Infrastructure already gets the ASP.NET
Core framework through `Microsoft.AspNetCore.Authentication.JwtBearer`.

**Storage**: none, and no migration.

**Testing**: xUnit + NSubstitute + TestServer in `tests/Features.Tests/Middleware/`, reusing `CapturingLogger`.

**Target Platform**: a Linux container on a Hetzner VPS, as before.

**Project Type**: web-service (API).

**Performance Goals**: none. The same middleware run in a different order costs nothing.

**Constraints**:
- No exception text in any response or log message (SC-003).
- Exactly one 2020 entry and one 2002 entry per failed key check (SC-002).
- Planned refusals stay 401/403 (FR-005).
- Spec 013's guarantees hold (FR-006).

**Scale/Scope**:
- One new extension method (about 15 lines).
- `Program.cs` loses five lines and gains one call.
- `ExceptionPipelineTests` gets a new host and about 5 new tests.
- One docs subsection; the backlog note moves.

## Constitution Check

_GATE: Must pass before Phase 0 research. Re-check after Phase 1 design._

| Principle | How the plan meets it | Status |
|---|---|---|
| I. Clean layering | `UseRequestPipeline` lives in Infrastructure, next to the middleware it orders. `Program.cs` (API) calls it. Domain and Application are untouched. | ✅ |
| II. Vertical slices | No feature slice changes. The test uses `IngestMeasurementsEndpoint` as it is. | ✅ |
| III. Results over exceptions | Unchanged. Rejected keys are still `AuthenticateResult.Fail` → 401, not exceptions. | ✅ |
| IV. Validate at the boundary | Unchanged. | ✅ |
| V. CQRS | Unchanged. | ✅ |
| VI. Logging | No new event. It reuses 2020 / `UnhandledException`. | ✅ |
| VII. Manual mapping | Not affected. | ✅ |
| VIII. Minimal footprint | One small method, and only because the tests otherwise can't run the real order (R4). No CORS workaround unless a test shows it is needed (R3). No safety-net handler (spec decision). | ✅ |

**Re-check after Phase 1**: unchanged, no violations.

## Project Structure

### Documentation (this feature)

```text
specs/014-exceptions-before-handler/
├── plan.md              # This file
├── research.md          # Phase 0: baseline, R1–R6
├── data-model.md        # Phase 1: no data change; which failures reach the 013 shapes
├── quickstart.md        # Phase 1: tests, live check against the baseline
├── contracts/
│   └── api-key-endpoints.md   # Phase 1: before/after for API-key endpoints
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/Infrastructure/Middleware/
└── RequestPipelineExtensions.cs         # new: UseRequestPipeline(corsPolicyName)

src/api/Program.cs                       # UseCors…UseExceptionHandling (5 lines) → app.UseRequestPipeline(FrontendCorsPolicy)

tests/Features.Tests/Middleware/
└── ExceptionPipelineTests.cs            # new host on UseRequestPipeline; + key-lookup, request-log, CORS, refusal cases

DevelopmentGuide.md                      # + "Request pipeline order"
specs/_backlog/exceptions-before-handler.md → specs/014-exceptions-before-handler/source-backlog-note.md
```

`ExceptionHandlingMiddleware` and `RequestResponseLoggingMiddleware` are unchanged.

**Structure Decision**: a pipeline-order change in Infrastructure and the API entry point, tested in the existing
middleware test folder.

## Complexity Tracking

No constitution violations, so nothing to justify.
