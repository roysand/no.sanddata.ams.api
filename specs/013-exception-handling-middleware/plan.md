# Implementation Plan: Safe Handling of Unexpected Errors

**Branch**: `feature/013-exception-handling-middleware` | **Date**: 2026-11-20 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/013-exception-handling-middleware/spec.md`

## Summary

`ExceptionHandlingMiddleware` is rewritten to follow three branches (data-model, "How an exception is handled"). One
log event is added, and tests cover every case.

1. **Always 500, fixed body (FR-001–FR-004).** The exception-type switch is removed (research R1). The body is
   `{ statusCode, message, traceId }` as `application/problem+json`. That is FastEndpoints' error shape, which the web
   app already reads (research R2, [contract](contracts/error-response.md)). The trace id is
   `Activity.Current?.Id ?? TraceIdentifier` (R3).
2. **Clear first (R6).** The request log buffers every response, so a partial body can be waiting in the buffer.
   `Response.Clear()` runs before the 500 is written, and `Cache-Control: no-cache, no-store` is set.
3. **Caller disconnect (FR-005, FR-006).** For an `OperationCanceledException` while `RequestAborted` is cancelled,
   the handler returns without an error log or a body. Other cancellations are 500s (R4).
4. **Response started (FR-007).** The handler logs once, calls `context.Abort()` and returns. It doesn't rethrow,
   because a rethrow adds two more error entries from the request log and Kestrel (R5).
5. **Logging (FR-008, FR-011).** `LogMessages.UnhandledException` is added: EventId 2020, Error, with `ReasonCode`,
   `ExceptionType`, `Method`, `Path` (no query string) and `TraceId`, and the exception attached (R7). The reason code
   goes into the `DevelopmentGuide.md` table.
6. **Wrap-up (FR-013, FR-014).** The web app is checked as in quickstart §3. The backlog note is deleted. A short new
   backlog note, `specs/_backlog/exceptions-before-handler.md`, records the out-of-scope case: exceptions in
   authentication, CORS or the request log never reach the handler.

`RequestResponseLoggingMiddleware` is not changed. With the handler inside it, it only sees the fixed 500 (FR-009,
verified by the pipeline test in R8).

## Technical Context

**Language/Version**: C# 14 / .NET 10, ASP.NET Core 10

**Primary Dependencies**: ASP.NET Core middleware (`HttpContext`, `ResponseExtensions.Clear`,
`IHttpRequestLifetimeFeature`), `System.Text.Json`, `System.Diagnostics.Activity`. **No new packages.**

**Storage**: none, and no migration.

**Testing**: xUnit + NSubstitute + `Microsoft.AspNetCore.TestHost` in `tests/Features.Tests`, all already referenced.
- The middleware unit tests use `DefaultHttpContext` and test features for `HasStarted` and `Abort()`.
- One TestServer test uses the real middleware order.
- Logs are captured by an in-test logger (research R8).

**Target Platform**: a Linux container on a Hetzner VPS, as before.

**Project Type**: web-service (API).

**Performance Goals**: none. This runs only on the exception path, and the happy path is unchanged.

**Constraints**:
- No exception text in any response (SC-001).
- Exactly one error log entry per unexpected failure, from this handler (SC-004). The request log's `ResponseError`
  2002 for the 500 also appears, as it does for every response of 400 or higher, but carries no exception text.
- Planned-failure responses are unchanged (FR-010).

**Scale/Scope**:
- One middleware class rewritten (about 60 lines).
- One `LoggerMessage` added.
- Two test files and one test logger helper.
- One docs table row, one backlog note deleted and one added.

## Constitution Check

_GATE: Must pass before Phase 0 research. Re-check after Phase 1 design._

| Principle | How the plan meets it | Status |
|---|---|---|
| I. Clean layering | The middleware and `LogMessages` stay in Infrastructure. Nothing moves into Domain or Application. | ✅ |
| II. Vertical slices | No feature slice is touched. | ✅ |
| III. Results over exceptions | This makes the rule stronger: an exception reaching the handler is treated as a server fault (500), not as a business failure in disguise. | ✅ |
| IV. Validate at the boundary | Validation errors still come from FastEndpoints, unchanged (FR-010). | ✅ |
| V. CQRS | Not affected. | ✅ |
| VI. Logging | Replaces the ad-hoc `_logger.LogError` with a compiled `LoggerMessage` (EventId 2020, inside 2000–2099) and the stable reason code `UnhandledException`. No body and no query string are logged. | ✅ |
| VII. Manual mapping | Not affected. | ✅ |
| VIII. Minimal footprint | No new package, setting, abstraction or log event beyond the one required. The debug-level disconnect event and `FakeLogger` were considered and left out (research R4, R8). | ✅ |

**Re-check after Phase 1**: unchanged, no violations.

## Project Structure

### Documentation (this feature)

```text
specs/013-exception-handling-middleware/
├── plan.md              # This file
├── research.md          # Phase 0: R1–R8
├── data-model.md        # Phase 1: the error answer, the log entry, the three branches
├── quickstart.md        # Phase 1: how to check it
├── contracts/
│   └── error-response.md # Phase 1: before/after body, web app compatibility
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/Infrastructure/
├── Middleware/ExceptionHandlingMiddleware.cs   # rewritten: three branches, fixed body, Clear(), Abort()
└── Logging/LogMessages.cs                      # + UnhandledException (2020)

tests/Features.Tests/Middleware/
├── ExceptionHandlingMiddlewareTests.cs         # new: unit cases (research R8)
├── ExceptionPipelineTests.cs                   # new: request log outside the handler, no leak
└── CapturingLogger.cs                          # new: in-test ILogger/ILoggerProvider

DevelopmentGuide.md                             # + UnhandledException in the reason-code table
specs/_backlog/exception-handling-middleware.md # deleted
specs/_backlog/exceptions-before-handler.md     # new: the out-of-scope follow-up
```

`src/api/Program.cs` and `RequestResponseLoggingMiddleware.cs` are unchanged.

**Structure Decision**: a contained change in the Infrastructure middleware and logging, and a new `Middleware` test
folder next to the feature test folders.

## Complexity Tracking

No constitution violations, so nothing to justify.
