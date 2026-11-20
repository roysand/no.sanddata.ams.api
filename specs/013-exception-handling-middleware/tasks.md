# Tasks: Safe Handling of Unexpected Errors

**Input**: Design documents from `/specs/013-exception-handling-middleware/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/error-response.md](contracts/error-response.md), [quickstart.md](quickstart.md)

**Tests**: Included, because the spec requires them (FR-012). They use xUnit in `tests/Features.Tests/Middleware/`,
with no database and no new package. Each story's tests come first and must fail before the code change.

**Organization**: every story changes one method, `ExceptionHandlingMiddleware.InvokeAsync`.
- Phase 2 adds the log event all branches use.
- US1 writes the default branch: log, clear, fixed 500. That rewrite also removes the type switch, so US2 is tests
  only.
- US3 adds the disconnect branch and US4 the started-response branch. Both sit in front of the default branch.
- US5 checks the log entry's properties and documents the reason code.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1–US5 from spec.md

## Facts from the code that the tasks rely on

- `src/Infrastructure/Middleware/ExceptionHandlingMiddleware.cs`:
  - It is a public class with constructor `(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)`.
  - `UseExceptionHandling()` is a public extension in the same file.
  - Today it calls `_logger.LogError(...)`, switches on the exception type and writes `{ error = exception.Message }`.
- `src/Infrastructure/Logging/LogMessages.cs` is `internal static`. EventIds 2000–2002 and 2010–2012 are taken. The
  pattern is a private static `LoggerMessage.Define<...>` field plus a public static wrapper method.
- `RequestResponseLoggingMiddleware` (public, same folder):
  - Its constructor is `(RequestDelegate, ILogger<RequestResponseLoggingMiddleware>, IOptions<RequestLoggingOptions>)`.
  - `RequestLoggingOptions` is public, in `Infrastructure.Logging`, and has `AttributesToLog`, `MaskValue`,
    `LogRequestBody` and `LogResponseBody`.
  - It swaps `Response.Body` for a `MemoryStream`. It logs `ResponseError` (2002) for status ≥ 400 and `ResponseSent`
    (2001) otherwise.
  - `Program.cs` registers it before `UseExceptionHandling()`.
- `tests/Features.Tests` already references `Features.csproj`, which brings in Infrastructure and ASP.NET Core. It also
  references `Microsoft.AspNetCore.TestHost`. `tests/Features.Tests/Http/EndpointHost.cs` shows the
  `WebApplication.CreateBuilder()` + `UseTestServer()` pattern.
- The middleware's `LogMessages` is internal, so tests check log entries through the captured EventId and state
  properties, not by calling `LogMessages`.

---

## Phase 1: Setup

- [X] T001 [P] Create `tests/Features.Tests/Middleware/CapturingLogger.cs` (namespace `Features.Tests.Middleware`):
  - `CapturedLog` record: `LogLevel Level`, `EventId EventId`, `string Message` (formatted),
    `IReadOnlyDictionary<string, object?> Properties` (from the state when it is
    `IReadOnlyList<KeyValuePair<string, object?>>`, without `{OriginalFormat}`), `Exception? Exception`.
  - `CapturingLoggerProvider : ILoggerProvider`: one shared thread-safe `List<CapturedLog>` exposed as `Logs`, with
    `CreateLogger` returning a logger that appends to it. `IsEnabled` returns true and `BeginScope` returns null.
  - `static ILogger<T> CreateLogger<T>()` for unit tests: a `LoggerFactory` with this provider, plus access to the
    same `Logs`.
  - Add a helper `bool ContainsText(string text)` on the list wrapper. It searches `Message`, every property value's
    `ToString()` and nothing else. The `Exception` is *expected* to carry the text, because the log is allowed to.

## Phase 2: Foundational

- [X] T002 Add `UnhandledException` to `src/Infrastructure/Logging/LogMessages.cs` (research R7):
  - Field: `LoggerMessage.Define<string, string, string, string, string>(LogLevel.Error,
    new EventId(2020, nameof(UnhandledException)), "Unhandled exception {ReasonCode} {ExceptionType} {Method} {Path} {TraceId}")`.
  - Public constant: `UnhandledExceptionReasonCode = "UnhandledException"`.
  - Wrapper: `public static void UnhandledException(ILogger logger, string exceptionType, string method, string path,
    string traceId, Exception exception)`. It passes the reason code and the exception to the delegate.
  - Add a comment above it: `// Error handling 2020-2029`.

**Checkpoint**: `dotnet build` passes. Nothing calls the new method yet.

---

## Phase 3: User Story 1 — An unexpected failure reveals nothing internal (P1) 🎯 MVP

**Goal**: every unexpected failure is answered with a fixed 500 body and a trace id, never exception text. A partial
body already in the buffer is discarded.

**Independent Test**: throw an exception whose message holds `Host=db.internal;Port=5432`. The body is exactly
`statusCode`, `message` and `traceId`, and the text is absent.

### Tests for User Story 1

- [X] T003 [US1] Create `tests/Features.Tests/Middleware/ExceptionHandlingMiddlewareTests.cs`:
  - A private helper builds a `DefaultHttpContext` with `Response.Body = new MemoryStream()`, `Request.Method = "POST"`
    and `Request.Path = "/api/test"`. It runs `new ExceptionHandlingMiddleware(next,
    CapturingLogger.CreateLogger<ExceptionHandlingMiddleware>()).InvokeAsync(context)` and returns the context, the
    body text and the logs.
  - `UnexpectedException_Returns500WithFixedBody`: `next` throws `new Exception("boom")`.
    - Status is 500.
    - `ContentType` is `application/problem+json`.
    - `Cache-Control` is `no-cache, no-store`.
    - The parsed JSON has exactly the properties `statusCode` (500), `message` (`"An unexpected error occurred."`)
      and `traceId` (equal to `context.TraceIdentifier`, because no `Activity` runs in the test).
  - `ExceptionText_NeverReachesTheBody`: `next` throws `new InvalidOperationException("Host=db.internal;Port=5432",
    new Exception("inner-secret"))`. The body contains none of `db.internal`, `5432`, `inner-secret` or
    `InvalidOperationException`.
  - `PartialBody_IsDiscarded`: `next` writes `{"partial":` to `Response.Body`, sets header `X-Partial: 1`, then throws.
    The body is only the error JSON, and `X-Partial` is gone.
  - Run the tests and confirm they fail against the current middleware.

- [X] T004 [P] [US1] Create `tests/Features.Tests/Middleware/ExceptionPipelineTests.cs`. It runs the real middleware
  order on a TestServer (research R8):
  - Use `WebApplication.CreateBuilder()` + `builder.WebHost.UseTestServer()`, then `builder.Logging.ClearProviders()`
    and `builder.Logging.AddProvider(provider)` with a `CapturingLoggerProvider`.
  - Add `builder.Services.Configure<RequestLoggingOptions>(o => { o.AttributesToLog = ["Path", "Method"];
    o.LogResponseBody = true; })`, so the logged body is visible.
  - Pipeline: `app.UseMiddleware<RequestResponseLoggingMiddleware>(); app.UseExceptionHandling();
    app.Run(_ => throw new InvalidOperationException("Failed to connect to 10.0.0.5:5432"));`.
  - `UnexpectedException_LeaksNothingInResponseOrRequestLog` (FR-009, SC-001):
    - The client gets 500 with the fixed body.
    - `provider.Logs.ContainsText("10.0.0.5")` is false.
    - There is exactly one Error entry with EventId 2020.
    - There is exactly one 2002 entry, with status 500, whose logged body is the fixed body.
    - There are no other Error entries.

### Implementation for User Story 1

- [X] T005 [US1] Rewrite `src/Infrastructure/Middleware/ExceptionHandlingMiddleware.cs` with the default branch
  (research R1–R3, R6–R7):
  - `catch (Exception ex)` calls `LogMessages.UnhandledException(_logger, ex.GetType().FullName ?? ex.GetType().Name,
    context.Request.Method, context.Request.Path.Value ?? string.Empty, traceId, ex)`, with
    `traceId = Activity.Current?.Id ?? context.TraceIdentifier`.
  - Then `context.Response.Clear()`, `StatusCode = 500`, `ContentType = "application/problem+json"` and
    `Headers.CacheControl = "no-cache, no-store"`.
  - Write `JsonSerializer.Serialize(new { statusCode = 500, message = FixedMessage, traceId })`, where
    `private const string FixedMessage = "An unexpected error occurred."`.
  - Remove the exception-type switch and the `System.Net` using.
  - Update the class XML doc to say: any exception here is a server fault; the answer is a fixed 500 and never
    exception text.
  - Keep `UseExceptionHandling()` unchanged.

**Checkpoint**: T003 and T004 pass.

---

## Phase 4: User Story 2 — Server bugs are reported as server errors (P1)

**Goal**: no exception type gets a 4xx, and planned failures are untouched. T005 already removes the switch, so this
phase only proves it.

**Independent Test**: each exception type that used to map to a 4xx now gives 500.

- [X] T006 [US2] In `tests/Features.Tests/Middleware/ExceptionHandlingMiddlewareTests.cs`, add the `[Theory]`
  `AnyExceptionType_Returns500`. The cases are `InvalidOperationException`, `ArgumentException`,
  `ArgumentNullException`, `KeyNotFoundException`, `UnauthorizedAccessException`, `NotSupportedException`, and an
  `Exception` wrapping an `ArgumentException`. Each gives status 500 and one 2020 entry. Use `[MemberData]` with
  factories, because exceptions are not valid attribute arguments.
- [X] T007 [P] [US2] In `tests/Features.Tests/Middleware/ExceptionPipelineTests.cs`, add
  `PlannedFailure_PassesThroughUnchanged` (FR-010):
  - The terminal handler writes status 400, content type `application/problem+json` and the body
    `{"statusCode":400,"message":"One or more errors occurred!","errors":{"email":["Email is required."]}}`.
  - The client gets exactly that status, content type and body.
  - There is no 2020 entry.

**Checkpoint**: all tests so far pass, with no code change in this phase.

---

## Phase 5: User Story 3 — Dropped connections do not raise alarms (P2)

**Goal**: a caller disconnect logs no error and writes nothing. Other cancellations are still 500s.

**Independent Test**: a cancelled `RequestAborted` plus an `OperationCanceledException` gives no Error entry and an
empty body.

- [X] T008 [US3] In `tests/Features.Tests/Middleware/ExceptionHandlingMiddlewareTests.cs`, add these tests:
  - `CallerDisconnect_LogsNoErrorAndWritesNothing`:
    - Set `context.RequestAborted` to the token of a cancelled `CancellationTokenSource`, and make `next` throw
      `new OperationCanceledException(token)`.
    - `InvokeAsync` does not throw, and the status is still 200.
    - The body is empty, and there are no Error-level entries.
  - `InternalCancellation_Returns500`: `RequestAborted` is not cancelled and `next` throws
    `new TaskCanceledException("HttpClient timeout")`. Status is 500, with one 2020 entry.
  - Confirm `CallerDisconnect_...` fails before T009.
- [X] T009 [US3] In `src/Infrastructure/Middleware/ExceptionHandlingMiddleware.cs`, add a
  `catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { return; }` before the
  general `catch`. Add a comment saying the caller left, so this is not a server fault and nobody is there to read
  an answer (research R4).

**Checkpoint**: US1–US3 tests pass.

---

## Phase 6: User Story 4 — A failure mid-response is handled once (P2)

**Goal**: if the response has started, the handler logs once, aborts and does not write. The handler itself never
throws.

**Independent Test**: with `HasStarted = true`, the connection is aborted, the body is not extended, and there is
exactly one 2020 entry.

- [X] T010 [US4] In `tests/Features.Tests/Middleware/ExceptionHandlingMiddlewareTests.cs`, add the test
  `StartedResponse_IsAbortedNotWritten`:
  - Private test features:
    - `StartedResponseFeature : HttpResponseFeature` with `HasStarted => true`.
    - `RecordingLifetimeFeature : IHttpRequestLifetimeFeature` with `AbortCount`, where `Abort()` increments it and
      `RequestAborted` is `CancellationToken.None`.
  - Set both through `context.Features.Set<...>()`, keeping `Response.Body` as a `MemoryStream`.
  - `next` writes `partial` and throws `new Exception("late")`.
  - Expected:
    - `InvokeAsync` does not throw.
    - `AbortCount == 1`.
    - The body is still `partial`, and the status is not 500.
    - There is exactly one 2020 entry.
  - Confirm it fails before T011.
- [X] T011 [US4] In `src/Infrastructure/Middleware/ExceptionHandlingMiddleware.cs`, after logging in the general
  `catch`, add `if (context.Response.HasStarted) { context.Abort(); return; }` before `Response.Clear()`.
  - `Clear()` throws on a started response, so this must come first.
  - Add a comment saying the status and headers are already sent. Aborting tells the caller the response is
    incomplete, and rethrowing would only log the same failure twice more (research R5).
- [X] T012 [P] [US4] In `tests/Features.Tests/Middleware/ExceptionPipelineTests.cs`, add
  `StartedResponse_BehindRequestLog_LogsOneUnhandledException`. It is the R5 "to verify" item:
  - The terminal handler calls `await ctx.Response.StartAsync()`, writes `partial` and throws.
  - Expected: exactly one 2020 entry, and `ContainsText` of the exception message is false. Assert the client's
    response read either throws or returns an incomplete body, whichever the test server does, and document the
    choice in a test comment.
  - If the request log's copy to the aborted connection throws and its own `catch` adds a 2002 entry, accept it only
    if that entry carries no exception text (masked by default). Record the observed behaviour in research.md R5 under
    "To verify in implementation".

**Checkpoint**: US1–US4 tests pass.

---

## Phase 7: User Story 5 — The unexpected-error log is consistent and queryable (P3)

**Goal**: the 2020 entry has the documented properties, no request body or query string, and is documented.

**Independent Test**: inspect one captured 2020 entry's properties.

- [X] T013 [US5] In `tests/Features.Tests/Middleware/ExceptionHandlingMiddlewareTests.cs`, add the test
  `UnhandledException_LogEntryHasDocumentedShape`:
  - Request setup: `Request.QueryString = new QueryString("?email=someone@example.com")`, and a request body stream
    `{"password":"hunter2"}`. `next` reads nothing and throws `new InvalidOperationException("x")`.
  - The single entry has:
    - Level `Error`.
    - `EventId.Id == 2020` and `EventId.Name == "UnhandledException"`.
    - Properties `ReasonCode = "UnhandledException"`,
      `ExceptionType = "System.InvalidOperationException"`, `Method = "POST"`, `Path = "/api/test"`, and `TraceId`
      equal to the body's `traceId`.
    - `Exception` is the same instance that was thrown.
  - `ContainsText("someone@example.com")` and `ContainsText("hunter2")` are both false.
- [X] T014 [P] [US5] In `DevelopmentGuide.md`, under "Standard failure reason codes", add a row to the table:
  `| UnhandledException | An exception reached the global exception handler; the caller gets a fixed 500 with a traceId, the details are only in the log | EventId 2020 (Infra/Logging) |`.
  `CLAUDE.md`'s EventId table already covers 2000–2099, so leave it unchanged.

**Checkpoint**: all middleware tests pass.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T015 Run `dotnet build` (no new warnings), `dotnet format --verify-no-changes` and `dotnet test` from the repo
  root, and fix anything that fails (quickstart §1).
- [X] T016 Manual check, quickstart §2: start the API locally with an unreachable database port and call
  `POST /api/auth/login`.
  - Confirm the 500 body and content type.
  - Confirm one 2020 log entry with the Npgsql exception and stack trace, the same `TraceId`, and no host or port in
    the response.
  - Confirm a validation error still returns the unchanged 400 shape.
  - Needs a local PostgreSQL. If none is available, say so and leave it to Roy.
- [X] T017 Manual check, quickstart §3: with the database still unreachable, sign in from the web app
  (`../no.sanddata.ams.frontend`). The form shows "An unexpected error occurred.". No web app code change is expected.
- [X] T018 [P] Create `specs/_backlog/exceptions-before-handler.md`. It follows the style of the existing backlog note
  (status line, what, why it matters, what to decide, done when), and records:
  - Exceptions thrown before `UseExceptionHandling()` (CORS, `UseAuthentication` including the API-key DB lookup, and
    `RequestResponseLoggingMiddleware`) are not handled by it.
  - Hosting answers them with an empty 500 body and logs them through Kestrel, not with event 2020.
  - Moving the handler outward changes what the request log sees (its `catch` would then fire for every exception).
- [X] T019 Go through every point and "Done when" item in `specs/_backlog/exception-handling-middleware.md` and
  confirm each is met by the tasks above, then delete the file (FR-014). Tick the items in
  `specs/013-exception-handling-middleware/checklists/requirements.md` if anything changed.

### Implementation notes (2026-11-20)

- **T012**: behind the request log, a response never counts as started, because its buffer swallows `StartAsync()`.
  The pipeline test asserts the fixed 500 instead of an abort, and research R5 records this.
- **T015**:
  - `dotnet build` shows no new warnings, and `dotnet test` passes 842/842.
  - `dotnet format --verify-no-changes` also fails on `main`, with about 42,000 line-ending and whitespace findings in
    untouched files. That is a separate cleanup.
  - The touched source files are clean. The new test files are LF like their neighbour `Http/EndpointHost.cs`. The
    rewritten middleware kept its CRLF.
- **T016**: no local PostgreSQL was needed. An unreachable port (5999) is the test.
  - The failure is EF Core's `InvalidOperationException` wrapping Npgsql "Failed to connect to 127.0.0.1:5999". The
    old code answered this with a 400 that carried the message.
  - Now it gives a 500 with the fixed body, one 2020 entry with the stack trace and the same `TraceId`, a request-log
    2002 entry with no exception text, and no Kestrel entry.
- **T017**: checked by reading `src/lib/apiClient.ts` and `src/features/auth/LoginPage.tsx` against the real 500
  body from T016 (`serverError = ApiError.message`), not by clicking through the web app.
- **T019**: the note had never been committed, so it was moved to `source-backlog-note.md` in this folder instead of
  being deleted. `_backlog/` no longer has it, and nothing is lost.

---

## Dependencies & Execution Order

- **Setup (T001)** → **Foundational (T002)**. T001 and T002 touch different projects and can run in parallel.
- **US1 (T003–T005)** depends on T001 and T002. It is the MVP: it closes the security fault.
- **US2 (T006–T007)** depends on T005. It adds tests only.
- **US3 (T008–T009)** and **US4 (T010–T012)** depend on T005. They edit the same method, so do them one after the
  other (US3, then US4), not in parallel.
- **US5 (T013–T014)** depends on T005. T014 (docs) can be done any time after T002.
- **Polish (T015–T019)** comes after all stories. T018 can be written at any time.

Within each story, the tests come first and must fail before the code change (except US2, which only adds tests).

## Parallel Opportunities

- T001 ∥ T002 (test helper and log event).
- T003 ∥ T004 (unit test file and pipeline test file).
- T006 ∥ T007, T010 ∥ T012, and T013 ∥ T014 (different files).
- T018 alongside anything.

```text
# US1 tests in parallel:
T003 ExceptionHandlingMiddlewareTests.cs (unit cases)
T004 ExceptionPipelineTests.cs (real middleware order)
```

## Implementation Strategy

1. **MVP = Phases 1–3 (US1).** After T005, no exception text reaches a caller, so the security fault is closed. It
   could ship alone.
2. **Add US2 (tests only).** It locks in "always 500".
3. **Add US3, then US4.** Each adds one branch in front of the default one, test first.
4. **Add US5 and polish.** This covers the log shape, docs, manual checks, the follow-up backlog note, and deleting
   the original note.

Total: 19 tasks.
