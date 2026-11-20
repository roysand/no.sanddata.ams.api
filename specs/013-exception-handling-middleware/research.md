# Research: Safe Handling of Unexpected Errors

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Date**: 2026-11-20

## What the code does today

- `Program.cs` sets up the pipeline in this order: forwarded headers → CORS → `UseAuthentication` →
  `RequestResponseLoggingMiddleware` → `UseAuthorization` → `UseExceptionHandling` → (Scalar) → `UseFastEndpoints`.
  The request log is therefore **outside** the exception handler.
- `RequestResponseLoggingMiddleware` always replaces `Response.Body` with a `MemoryStream`. It copies that stream to
  the real one only after the inner pipeline returns. Endpoint writes land in the buffer, so `Response.HasStarted`
  stays `false` unless code calls `Response.StartAsync()` (nothing in `src/` does). It logs `ResponseError` (2002,
  error level) for any status ≥ 400, and `ResponseSent` (2001) otherwise. Its `catch` block runs only when an
  exception gets past the exception handler. It then records `ExceptionType` and `ExceptionMessage` (masked unless
  listed in `AttributesToLog`, which defaults to `[Path, Method]`) and rethrows.
- `src/` never throws `KeyNotFoundException` or `UnauthorizedAccessException` to produce a 404 or 401. The only
  `InvalidOperationException` and `ArgumentException` throws are configuration faults and the guard in
  `Result.Value`, and all of them are programmer or setup errors. The 400, 404 and 401 mappings serve no case.
- FastEndpoints 7.1.1 sends errors as `ErrorResponse` (`statusCode`, `message`, `errors`) with content type
  `application/problem+json`. The web app (`no.sanddata.ams.frontend/src/lib/apiClient.ts`) reads `message`, `code`
  and `errors`, and falls back to `response.statusText`.

## R1 — Status for every exception: always 500

**Decision**: remove the type switch. Every exception that reaches the handler is answered with 500.

**Rationale**: business and validation failures are `Result` values or FastEndpoints validation errors (constitution
III/IV), so an exception here is a server fault. The research above finds no case that needs a 4xx (FR-003).

**Alternatives considered**: keep `KeyNotFoundException` → 404 and `UnauthorizedAccessException` → 401. Rejected,
because no code relies on them, and a stray `KeyNotFoundException` from a dictionary lookup is a bug, not a missing
resource.

## R2 — Body: the API's existing error shape plus `traceId`

**Decision**: `{ "statusCode": 500, "message": "An unexpected error occurred.", "traceId": "<id>" }` with content type
`application/problem+json`, the same as FastEndpoints' errors. It is serialised with camelCase names and has no
`errors` field.

**Rationale**: the web app shows `message`, so it improves with no change (FR-004, FR-013, SC-006). Leaving out
`errors` matters: the web app prefers the `errors` texts over `message` when they are present.

**Alternatives considered**: ProblemDetails (`type`, `title`, `status`, `detail`, `traceId`). The web app doesn't
read `title` or `detail`, so it needs a change, and 500s would differ from every other error. Rejected in the spec
(Assumptions).

## R3 — Reference id: `Activity.Current?.Id ?? HttpContext.TraceIdentifier`

**Decision**: use the same expression ASP.NET Core's own ProblemDetails uses. The same value goes into the body and
into the log entry as the `TraceId` property.

**Rationale**: it is unique per request, already exists, and matches the W3C trace id in any tracing (SC-005).

**Alternatives considered**: a new `Guid`. Rejected, because it is a second id for the same request.

## R4 — Caller disconnect: `OperationCanceledException` while `RequestAborted` is cancelled

**Decision**: when `ex is OperationCanceledException && context.RequestAborted.IsCancellationRequested`, return
without logging at error level and without writing anything (FR-005). Any other `OperationCanceledException`, for
example an `HttpClient` timeout (`TaskCanceledException`) while the caller is still connected, is an unexpected
failure (FR-006).

**Rationale**: checking the token, not only the exception type, is what tells "the caller left" apart from "something
inside timed out".

**Known effect**: the request log then records a `ResponseSent` entry with the status left as it was (normally 200)
and an empty body. That is an information-level entry, so no error is logged, which is what FR-005 asks for. Setting
a status such as 499 was rejected: the request log would turn it into an error-level `ResponseError`.

**Alternatives considered**: a debug-level `RequestAborted` log event. Not needed now (constitution VIII). It can be
added if disconnects ever need counting.

## R5 — Response already started: log once, abort, don't rethrow

**Decision**: if `context.Response.HasStarted`, log the `UnhandledException` entry, call `context.Abort()` and return
(FR-007).

**Rationale**:
- Writing the status or a body throws a second exception. That is the fault in the backlog note.
- Rethrowing makes `RequestResponseLoggingMiddleware` log a second error entry (`ResponseError` 500), and then Kestrel
  logs a third ("unhandled exception was thrown by the application"), which breaks SC-004 (exactly one error entry).
- `Abort()` resets the connection, so the caller sees an incomplete response instead of a body that looks finished.

**Behind the buffer**: in the real pipeline this branch is rare (see "What the code does today"). The common hidden
case is the opposite one: the response has *not* started, but part of a body is already in the buffer. That's R6.

**Verified in implementation (2026-11-20)**: behind the request log this branch cannot be reached at all.
- The request log sets `Response.Body` to its `MemoryStream`, which replaces the response body feature with a
  `StreamResponseBodyFeature`. That feature's `StartAsync()` does not start the real response, so `HasStarted` stays
  `false` even after an endpoint calls `Response.StartAsync()`.
- The handler then takes the normal branch. It clears the partial body and sends the fixed 500, which is the better
  outcome.
- `ExceptionPipelineTests.StartedResponse_BehindRequestLog_StillGetsTheFixed500` pins this behaviour.
- The abort branch stays for a pipeline without that buffer, for example if the request log is ever removed or moved
  inside the handler. `ExceptionHandlingMiddlewareTests.StartedResponse_IsAbortedNotWritten` covers it. The question of
  the request log copying to an aborted connection therefore does not arise in the current pipeline.

## R6 — Clear the response before writing the error

**Decision**: before writing the 500, call `context.Response.Clear()`. It resets the status, the headers and, because
the buffer can seek, the body length. Then set `Cache-Control: no-cache, no-store` (as ASP.NET Core's own exception
handler does) and write the body.

**Rationale**: without this, an endpoint that wrote part of its body and then threw would send that half body
followed by the error JSON, in a 500. CORS headers survive: `CorsMiddleware` adds them in `Response.OnStarting`, which
runs after the clear. The built-in `ExceptionHandlerMiddleware` relies on the same thing.

## R7 — Logging: one `LoggerMessage`, EventId 2020

**Decision**: add `UnhandledException` to `Infrastructure/Logging/LogMessages.cs`:
- EventId `2020`, named `UnhandledException`, at level `Error`.
- Template: `"Unhandled exception {ReasonCode} {ExceptionType} {Method} {Path} {TraceId}"`.
- The exception object is passed in, so the sink gets the message, the stack trace and inner exceptions.
- `ReasonCode` is always `"UnhandledException"`.
- `Path` is `Request.Path` without the query string, which can carry personal data. The body is never included
  (FR-008).

**Rationale**: 2000–2012 are taken (request/response 2000–2002, migrations 2010–2012). 2020 starts a new block for
error handling in the Infra/Logging range. `LogMessages` is `internal` to Infrastructure, and the middleware lives
there too.

**Docs**: add the code to the reason-code table in `DevelopmentGuide.md`. The EventId range table is already correct
(2000–2099 Infra/Logging), so `CLAUDE.md` needs no change (FR-011).

## R8 — Tests: middleware unit tests, plus one test with the real pipeline order

**Decision**: add `tests/Features.Tests/Middleware/`.
- `ExceptionHandlingMiddlewareTests` call `InvokeAsync` on a `DefaultHttpContext` with a throwing `RequestDelegate`:
  - 500, fixed message, `traceId`, and no exception text, for a plain exception, an `InvalidOperationException`, an
    `ArgumentException` and an exception whose message holds `Host=db.internal;Port=5432`;
  - an aborted request (cancelled `RequestAborted`) writes nothing and logs no error;
  - a non-caller `OperationCanceledException` gives 500;
  - an already-started response, through a test `IHttpResponseFeature` with `HasStarted = true` and an
    `IHttpRequestLifetimeFeature` that records `Abort()`, is aborted, not written, and doesn't throw;
  - a partial body in a seekable buffer is cleared before the error is written;
  - the log entry has EventId 2020, level Error, `ReasonCode = UnhandledException` and the trace id.
- `ExceptionPipelineTests` run TestServer (already referenced) with `RequestResponseLoggingMiddleware` outside
  `UseExceptionHandling`, the way `Program.cs` orders them, and an endpoint that throws. They check that the body is
  fixed and that neither middleware's log entries contain the exception text (FR-009).
- Logs are captured with a small in-test `ILogger`/`ILoggerProvider` that records level, EventId, state properties and
  formatted text. That avoids a new package (constitution VIII).

**Alternatives considered**: `Microsoft.Extensions.Diagnostics.Testing` (`FakeLogger`). It is a new package for
something about 30 lines of test code do. Rejected for now.

## Out of scope (from the spec)

Exceptions in steps before `UseExceptionHandling` (CORS, authentication including the API-key database lookup, the
request log itself) are not caught here. Hosting answers them with an empty 500 body. A follow-up backlog note
records this (see plan, Summary step 6).
