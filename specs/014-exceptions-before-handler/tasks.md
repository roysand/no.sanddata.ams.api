# Tasks: Catch Failures During Sign-In Checks

**Input**: Design documents from `/specs/014-exceptions-before-handler/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/api-key-endpoints.md](contracts/api-key-endpoints.md), [quickstart.md](quickstart.md)

**Tests**: Included, because the spec requires them (FR-007). They use xUnit + NSubstitute + TestServer in
`tests/Features.Tests/Middleware/ExceptionPipelineTests.cs`, with no database and no new package.

**Organization**:
- Phase 2 is a refactor that keeps behaviour the same. The five pipeline lines move into `UseRequestPipeline` *in the
  current order*, and the test host is rebuilt on it, so spec 013's tests prove nothing changed.
- US1 then writes a failing test and moves one line.
- US2 and US3 add tests against the new order. US3 has a conditional fix, used only if its test fails.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1–US3 from spec.md

## Facts from the code that the tasks rely on

- `src/api/Program.cs`, lines ~89–97:
  ```csharp
  // Add Authentication and Authorization middleware
  app.UseCors(FrontendCorsPolicy);
  app.UseAuthentication();
  // Request/response logging middleware needs authentication to populate claims
  app.UseMiddleware<RequestResponseLoggingMiddleware>();
  app.UseAuthorization();

  // Add exception handling middleware
  app.UseExceptionHandling();
  ```
  `UseForwardedHeaders(forwardedHeadersOptions)` comes just before these lines. The Scalar block, `UseHttpsRedirection`
  and `UseFastEndpoints` come after them. `const string FrontendCorsPolicy = "Frontend";` is defined above.
- Authentication setup in `src/Infrastructure/AddInfrastructureToDI.cs`:
  - `AddAuthentication` with `DefaultAuthenticateScheme` and `DefaultChallengeScheme` set to
    `JwtBearerDefaults.AuthenticationScheme`;
  - `.AddJwtBearer(...)`;
  - `.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationOptions.DefaultScheme,
    options => { })`;
  - then `services.AddAuthorization()`.
  - `ApiKeyAuthenticationOptions.DefaultScheme == "ApiKey"`, and the header is `X-API-Key`.
- `ApiKeyAuthenticationHandler` (public, `Infrastructure.Authentication`) takes
  `IApiKeyRepository<ApiKey>` (`Application.Common.Interfaces.Repositories`, entity `Domain.Common.Entities.ApiKey`).
  - It calls `FindActiveByKeyHashAsync(string keyHash, CancellationToken)`.
  - A `null` result gives `AuthenticateResult.Fail` → 401.
  - With no header it returns `NoResult`.
- `IngestMeasurementsEndpoint` (`Features.Measurements.Endpoints`) is `POST /api/measurements` with
  `AuthSchemes("ApiKey")`, and depends only on `IDispatcher`.
- `tests/Features.Tests/Middleware/ExceptionPipelineTests.cs` (spec 013) has:
  - a private `Pipeline` helper: request log → `UseExceptionHandling()` → `app.Run(terminal)`, with
    `RequestLoggingOptions { AttributesToLog = ["Path","Method"], LogResponseBody = true }` and a
    `CapturingLoggerProvider`;
  - three tests: `UnexpectedException_LeaksNothingInResponseOrRequestLog`, `PlannedFailure_PassesThroughUnchanged`
    and `StartedResponse_BehindRequestLog_StillGetsTheFixed500`;
  - constants `FixedBody`, `UnhandledExceptionEventId = 2020` and `ResponseErrorEventId = 2002`.
- `tests/Features.Tests/Http/EndpointHost.cs` shows FastEndpoints on TestServer:
  `AddFastEndpoints(o => o.Assemblies = [typeof(CreateUserEndpoint).Assembly])`, with a substituted `IDispatcher`.

---

## Phase 1: Setup

No setup. The test project already has every package and helper this needs.

## Phase 2: Foundational — behaviour-preserving refactor

**Purpose**: one pipeline method that both `Program.cs` and the tests use, still in the **current** order.

- [X] T001 Create `src/Infrastructure/Middleware/RequestPipelineExtensions.cs` (namespace `Infrastructure.Middleware`)
  with `public static IApplicationBuilder UseRequestPipeline(this IApplicationBuilder app, string corsPolicyName)`:
  - It calls, in today's order: `UseCors(corsPolicyName)`, `UseAuthentication()`,
    `UseMiddleware<RequestResponseLoggingMiddleware>()`, `UseAuthorization()`, `UseExceptionHandling()`. It returns
    `app`.
  - Move the `Program.cs` comment about the request log needing authentication for claims onto that line.
  - Add an XML doc comment: "The request steps between forwarded headers and the endpoints, in the order the API
    depends on. Shared by Program.cs and the pipeline tests so they run the same order."
- [X] T002 In `src/api/Program.cs`, replace the block quoted in the facts (the CORS line through
  `app.UseExceptionHandling();`, with its two comments) with:
  ```csharp
  // CORS, sign-in, request log, exception handling and authorization; see UseRequestPipeline for the order.
  app.UseRequestPipeline(FrontendCorsPolicy);
  ```
  - Remove the `using` for `RequestResponseLoggingMiddleware` if nothing else in the file uses it.
  - Run `dotnet build`. There must be no new warnings.
- [X] T003 In `tests/Features.Tests/Middleware/ExceptionPipelineTests.cs`, replace the private `Pipeline` helper's
  setup so it runs the shared order (research R5). Keep the class name `Pipeline` and the `Client` and `Logs`
  properties.
  - Signature: `StartAsync(RequestDelegate terminal, Action<IApiKeyRepository<ApiKey>>? arrangeKeys = null,
    bool logResponseBody = true)`.
  - Services:
    - `builder.Logging.ClearProviders()` and `AddProvider(provider)`;
    - `Configure<RequestLoggingOptions>` with `AttributesToLog = ["Path","Method"]` and
      `LogResponseBody = logResponseBody`;
    - `AddCors(o => o.AddPolicy(CorsPolicy, p => p.WithOrigins(AllowedOrigin).AllowAnyHeader().AllowAnyMethod()))`,
      with constants `CorsPolicy = "Test"` and `AllowedOrigin = "https://app.example"`;
    - `AddAuthentication(o => { o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
      o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme; })`;
    - `.AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters { IssuerSigningKey = new
      SymmetricSecurityKey(new byte[32]), ValidateIssuer = false, ValidateAudience = false })`;
    - `.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationOptions.DefaultScheme,
      _ => { })`;
    - `AddAuthorization()`;
    - a singleton `IApiKeyRepository<ApiKey>` substitute, passed to `arrangeKeys` when it is given;
    - a singleton `IDispatcher` substitute;
    - `AddFastEndpoints(o => o.Assemblies = [typeof(IngestMeasurementsEndpoint).Assembly])`.
  - Pipeline: `app.UseRequestPipeline(CorsPolicy); app.UseFastEndpoints(); app.Run(terminal);`.
  - Leave the three spec 013 tests' bodies unchanged. They use `/api/test`, which FastEndpoints doesn't map, so it
    falls through to `terminal`.
  - Run `dotnet test --filter "FullyQualifiedName~Features.Tests.Middleware"`. All 17 tests must pass, which proves
    the refactor kept behaviour.

**Checkpoint**: `Program.cs` uses `UseRequestPipeline`, and spec 013's middleware tests pass on the shared order. No
behaviour has changed yet.

---

## Phase 3: User Story 1 — A sensor upload during a database outage gets the standard error answer (P1) 🎯 MVP

**Goal**: a failing API-key lookup gives the fixed 500 and exactly one 2020 entry. Refusals stay 401.

**Independent Test**: `POST /api/measurements` with `X-API-Key` while the key repository throws. Expect a 500 with
the fixed body and one 2020 entry.

- [X] T004 [US1] In `tests/Features.Tests/Middleware/ExceptionPipelineTests.cs`, add
  `ApiKeyLookupFailure_Returns500AndOneUnhandledException`:
  - Set up `arrangeKeys` so that `keys.FindActiveByKeyHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())`
    throws `new InvalidOperationException("Failed to connect to 10.0.0.5:5432")`, using
    `.ThrowsAsync(...)` from `NSubstitute.ExceptionExtensions`.
  - The terminal is `_ => throw new InvalidOperationException("terminal must not run")`.
  - Send `POST /api/measurements` with header `X-API-Key: sensor-key` and body `{}` as `application/json`.
  - Wrap the send in `Record.ExceptionAsync`, because before the fix the exception escapes to the test server.
  - Assert:
    - no exception;
    - status 500 and a body that contains `FixedBody`;
    - the body has no `10.0.0.5`;
    - exactly one entry with EventId 2020, and its `Path` property is `/api/measurements`;
    - `pipeline.Logs.ContainsText("10.0.0.5")` is false.
  - Run it and confirm it **fails** on the current order.
- [X] T005 [P] [US1] In the same file, add the refusal cases `UnknownApiKey_IsStillRefused` and
  `MissingApiKey_IsStillRefused` (FR-005):
  - Unknown key: the substitute returns `Task.FromResult<ApiKey?>(null)`, the request sends `X-API-Key: unknown`, and
    the answer is 401.
  - Missing key: no `X-API-Key` header, and the answer is 401.
  - In both, there is no 2020 entry and the terminal is never reached.
  - These pass before and after T006. They guard against the move changing refusals.
- [X] T006 [US1] In `src/Infrastructure/Middleware/RequestPipelineExtensions.cs`, move `UseExceptionHandling()` so
  it is called **before** `UseAuthorization()`. The order becomes CORS, authentication, request log, exception
  handling, authorization.
  - Add a comment on the line saying that authorization runs the API-key scheme, which reads the database, so the
    handler must wrap it (spec 014).
  - Update the XML doc to state that everything from authorization inward is covered, and that CORS, authentication
    and the request log are not.

**Checkpoint**: T004, T005 and spec 013's tests pass.

---

## Phase 4: User Story 2 — The request log keeps recording failed requests the same way (P1)

**Goal**: a failed key check gives exactly one request-log error entry with status 500 and no failure text, for both
body-logging settings.

**Independent Test**: the T004 request, with the request log inspected for both `LogResponseBody` values.

- [X] T007 [US2] In `tests/Features.Tests/Middleware/ExceptionPipelineTests.cs`, add the `[Theory]`
  `ApiKeyLookupFailure_RequestLogHasOneEntryWithoutFailureText`, with `[InlineData(true)]` and
  `[InlineData(false)]` for `logResponseBody`. It uses the same failing request as T004. Assert:
  - The Error-level entries are exactly one 2020 entry and one 2002 entry.
  - The 2002 entry has `Properties["StatusCode"] == 500`.
  - When `logResponseBody` is true, `Properties["ResponseBody"]` contains `FixedBody`.
  - The 2002 entry's `Attributes` property does not contain `ExceptionType` or `ExceptionMessage`. Those keys only
    appear on the request log's `catch` path, which must no longer be used.
  - `pipeline.Logs.ContainsText("10.0.0.5")` is false.

**Checkpoint**: all pipeline tests pass.

---

## Phase 5: User Story 3 — The web app can still read the error answer (P2)

**Goal**: every unexpected 500 to an allowed origin carries `Access-Control-Allow-Origin`, and a disallowed origin
gets none.

**Independent Test**: failing requests with `Origin` headers, checking the response headers.

- [X] T008 [US3] In `tests/Features.Tests/Middleware/ExceptionPipelineTests.cs`, add these tests:
  - `ApiKeyLookupFailure_FromAllowedOrigin_KeepsCorsHeader`: the T004 request plus `Origin: https://app.example`.
    Expect 500 and `response.Headers` `Access-Control-Allow-Origin` equal to `https://app.example`.
  - `EndpointFailure_FromAllowedOrigin_KeepsCorsHeader`: `GET /api/test` with `Origin: https://app.example` and a
    terminal that sets `X-Partial: 1`, writes `partial` and throws. Expect 500, the CORS header present, and
    `X-Partial` absent. This proves `Response.Clear()` removed the endpoint's headers but not CORS's.
  - `Failure_FromDisallowedOrigin_HasNoCorsHeader`: the T004 request with `Origin: https://evil.example`. Expect 500
    and no `Access-Control-Allow-Origin` header.
  - Run them. If all pass, research R3 holds, so mark T009 `[X]` with the note "not needed: test passed" and record
    that in research.md R3.
- [X] T009 [US3] **Only if T008 fails.** In `src/Infrastructure/Middleware/ExceptionHandlingMiddleware.cs`
  `WriteErrorAsync`, copy the `Access-Control-*` headers and `Vary` from `context.Response.Headers` before
  `Response.Clear()`, and set them again after it (research R3, fallback). Add a comment explaining why. Then re-run
  T008.

**Checkpoint**: all middleware tests pass.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T010 [P] In `DevelopmentGuide.md`, add a subsection `### Request pipeline order` right before
  `### Standard failure reason codes` (FR-008). It contains:
  - the ordered list from research R1;
  - one sentence saying that the exception handler (spec 013, event 2020) covers everything from authorization
    inward, including the API-key database lookup;
  - one sentence saying that forwarded headers, CORS, JWT sign-in and the request log run before it, do no I/O, and
    fall back to the hosting layer's empty 500 if they ever throw;
  - a note that the order lives in `UseRequestPipeline` (`src/Infrastructure/Middleware/RequestPipelineExtensions.cs`)
    and is covered by `ExceptionPipelineTests`.
- [X] T011 Run `dotnet build` (no new warnings) and `dotnet test` (everything passes). Then run
  `dotnet format --verify-no-changes --include` on the touched source files: `RequestPipelineExtensions.cs`,
  `Program.cs`, and `ExceptionHandlingMiddleware.cs` if T009 ran. It must report nothing beyond line endings that
  match the file's existing style.
  - The whole-repo format check fails on `main` (spec 013 T015), so don't use it as the gate.
  - Write the new `.cs` file with CRLF, like `ExceptionHandlingMiddleware.cs`.
  - Keep `Program.cs`'s existing line endings.
- [X] T012 Manual check, quickstart §2: run the API locally with the database on port 5999, then send
  `POST /api/measurements` with `X-API-Key` and `Origin: http://localhost:5173`. Expected:
  - 500 with the fixed body and the CORS header, and no stack trace (compare with the baseline in research.md);
  - one 2020 entry with the same `TraceId`;
  - one 2002 entry without `ExceptionMessage`;
  - no `DeveloperExceptionPageMiddleware` or Kestrel error entry;
  - without `X-API-Key` the answer is 401.
- [X] T013 Answer each "Done when" item in `specs/_backlog/exceptions-before-handler.md`. Then move the file to
  `specs/014-exceptions-before-handler/source-backlog-note.md` (FR-009) and add the "Source note" line to the top of
  `spec.md`, as spec 013 does.

---

## Dependencies & Execution Order

- **Foundational (T001 → T002 → T003)** runs in order. T002 needs T001's method, and T003 needs both.
- **US1**: T004 and T005 come after T003, and T005 can run alongside T004. T006 comes after T004 has been seen to
  fail.
- **US2 (T007)** comes after T006.
- **US3 (T008 → T009 if needed)** comes after T006. It is independent of US2, but it is in the same test file, so
  run it after T007.
- **Polish**: T010 can be done any time after T006. T011–T013 come after all stories.

## Parallel Opportunities

- T004 ∥ T005: separate test methods in the same file, so write them together.
- T010 (docs) runs in parallel with US2 and US3.
- Most tasks touch the same two files, so there is little parallelism. That is expected for a change this size.

```text
# US1 tests together:
T004 ApiKeyLookupFailure_Returns500AndOneUnhandledException   (must fail first)
T005 UnknownApiKey_IsStillRefused / MissingApiKey_IsStillRefused (pass throughout)
```

## Implementation Strategy

1. **Refactor first (Phase 2)**: the same order, now testable. Spec 013's tests prove nothing changed.
2. **MVP = US1**: one failing test, then a one-line move. Sensor outages now give the standard 500 and event 2020.
3. **US2 and US3**: lock in request-log and CORS behaviour. The only code that may be added is T009, and only if the
   CORS test fails.
4. **Polish**: docs, the live check against the baseline, and moving the note.

Total: 13 tasks (T009 conditional).

## Implementation notes (2026-11-20)

- **T003**: the refactored host ran spec 013's 17 middleware tests green on the old order before anything moved.
- **T004**: it failed as intended before T006. The `InvalidOperationException` escaped the pipeline to the test
  server.
- **T009**: not needed. All three CORS tests passed with no change (research R3, "Verified in implementation").
- **T011**:
  - `dotnet build` shows no new warnings, and `dotnet test` passes 850/850.
  - `dotnet format` on the touched files reports only `ENDOFLINE` for `Program.cs` and the test file. Both were
    already LF.
  - The new `RequestPipelineExtensions.cs` is CRLF and clean.
- **T012**: the live run matched quickstart §2.
  - The sensor upload with the database down returned 500 `application/problem+json` with the fixed body, a
    `traceId` and `Access-Control-Allow-Origin: http://localhost:5173`. There was no stack trace; the baseline sent
    the full stack trace and `127.0.0.1:5999`.
  - The log had one 2020 entry, one 2002 entry without `ExceptionType`/`ExceptionMessage`, and no developer exception
    page or Kestrel entry.
  - No key gave 401, and login `{}` gave 400.
- **T013**: the note was moved with its "done when" items ticked and its status line updated.
