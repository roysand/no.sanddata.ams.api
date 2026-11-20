# Research: Catch Failures During Sign-In Checks

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Date**: 2026-11-20

## Baseline (measured on `main` at `c04c08e`, 2026-11-20)

The API ran locally with the database pointed at an unused port (5999) and `ASPNETCORE_ENVIRONMENT=Development`. The
request was `POST /api/measurements` with `X-API-Key: anything` and `Origin: http://localhost:5173`. The result:

- **Status 500, `text/plain`, with the full exception and stack trace in the body**, including
  `Failed to connect to 127.0.0.1:5999` and the request headers. ASP.NET Core's developer exception page answered,
  because the failure never reached `ExceptionHandlingMiddleware`. In Production the page is not used, so the caller
  gets an empty 500 and hosting logs the error.
- **The stack trace shows where the lookup runs**: `ApiKeyEfRepository.FindActiveByKeyHashAsync` ←
  `ApiKeyAuthenticationHandler.HandleAuthenticateAsync` ← `PolicyEvaluator.AuthenticateAsync` ←
  **`AuthorizationMiddleware.Invoke`** ← `RequestResponseLoggingMiddleware.InvokeAsync` ← `AuthenticationMiddleware`.
- **The request log** wrote `ResponseError` (2002) through its own `catch`, with `ExceptionType` and a masked
  `ExceptionMessage`. There was no event 2020.
- **`Access-Control-Allow-Origin: http://localhost:5173` was on the 500**, even though the developer exception page
  clears the response before writing.

## R1 — The key lookup runs in `UseAuthorization`, not `UseAuthentication`

**Decision**: move `UseExceptionHandling()` from after `UseAuthorization()` to just before it, after the request log.
This is the order Roy chose (spec, Background).

**Rationale**:
- `AddAuthentication` sets `DefaultAuthenticateScheme` to JWT (`AddInfrastructureToDI.cs`), so `UseAuthentication`
  only validates a bearer token, in memory.
- The API-key scheme runs only when `AuthorizationMiddleware` evaluates an endpoint whose policy names it.
  FastEndpoints' `AuthSchemes("ApiKey")` (for example `IngestMeasurementsEndpoint`) puts the scheme on the policy,
  and `PolicyEvaluator` calls `AuthenticateAsync` for each scheme on it.
- The baseline stack trace confirms this. Moving the handler in front of `UseAuthorization` therefore covers the only
  step before the endpoints that does I/O.

**New order**:

```text
UseForwardedHeaders
UseCors
UseAuthentication            (JWT, in memory)
RequestResponseLoggingMiddleware
UseExceptionHandling         ← moved: now before authorization
UseAuthorization             (runs the API-key lookup for ApiKey endpoints)
(Scalar / OpenAPI in Development or when EnableScalarDocs is set)
UseHttpsRedirection          (only when an https URL is bound)
UseFastEndpoints
```

**Alternatives considered**: a single handler at the front, two handlers, or keeping the order. All were rejected in
the spec's Background.

## R2 — The request log needs no change

**Decision**: `RequestResponseLoggingMiddleware` and its `catch` stay as they are.

**Rationale**:
- The handler is still inside the request log. A failure in authorization is now turned into the fixed 500 before
  the request log sees it, so the request log takes its normal path: one `ResponseError` 2002 with status 500, and the
  fixed body when `LogResponseBody` is on. That is the same path spec 013 tested for endpoint failures (FR-003).
- Its `catch` now only runs if the handler itself fails while writing. Keeping it is harmless. It is the only record
  in that case.

## R3 — CORS headers survive the handler's `Response.Clear()`

**Decision**: no CORS change. A test pins the behaviour (FR-004).

**Rationale**:
- `CorsMiddleware` evaluates the policy before calling the next step, but applies the response headers in a
  `Response.OnStarting` callback. `Response.Clear()` resets headers already set, not the start callbacks, so the
  headers are added when the 500 starts.
- The baseline shows it: the developer exception page also clears the response, and the 500 still had
  `Access-Control-Allow-Origin`.
- `UseCors` stays before the handler, so it has already run for every request the handler answers.

**Verified in implementation (2026-11-20)**: the CORS tests in `ExceptionPipelineTests` pass with no change:
- `ApiKeyLookupFailure_FromAllowedOrigin_KeepsCorsHeader` passes.
- `EndpointFailure_FromAllowedOrigin_KeepsCorsHeader` passes. The endpoint's own `X-Partial` header is cleared and
  `Access-Control-Allow-Origin` is kept.
- `Failure_FromDisallowedOrigin_HasNoCorsHeader` passes.

The fallback below was not written (task T009).

**If the test had failed**: in-scope fallback (spec, Assumptions). The handler would capture the `Access-Control-*` and
`Vary` headers before `Clear()` and put them back. This is only written if the test shows it is needed.

## R4 — One shared pipeline method, so tests run the real order

**Decision**: add `UseRequestPipeline(this IApplicationBuilder app, string corsPolicyName)` in
`src/Infrastructure/Middleware/RequestPipelineExtensions.cs`. It calls `UseCors`, `UseAuthentication`, the request
log, `UseExceptionHandling` and `UseAuthorization`, in that order. `Program.cs` calls it in place of those five lines.
`UseForwardedHeaders`, with its Caddy-specific options and comment, stays in `Program.cs` in front of it.

**Rationale**:
- FR-007 asks for tests "using the real order". The test project references Features → Infrastructure, not `Api`,
  so it cannot run `Program.cs`.
- A copy of the order in the test would pass even if `Program.cs` were reordered later. A shared method means a
  reorder in one place is what the tests check.
- This is the smallest abstraction that makes the order testable (constitution VIII).

**Alternatives considered**:
- **A `WebApplicationFactory<Program>` test**: it needs a reference to `Api` and the whole startup, including EF and
  migrations, background services and configuration. That is much heavier.
- **Copying the order into the test**: it doesn't guard `Program.cs`.

## R5 — Tests: the real auth handlers, a failing key repository, and the real ingest endpoint

**Decision**: replace the private `Pipeline` helper in `ExceptionPipelineTests` with a host that uses the shared
method. Run spec 013's existing cases through it, and add the spec 014 cases.

The host's services:
- logging to `CapturingLoggerProvider`;
- `RequestLoggingOptions` with `LogResponseBody = true`;
- `AddCors` with a policy that allows `https://app.example`;
- `AddAuthentication` as in Infrastructure: the JWT default with a dummy key (no I/O), plus the real
  `ApiKeyAuthenticationHandler` under `"ApiKey"`;
- `AddAuthorization`;
- `IApiKeyRepository<ApiKey>` as an NSubstitute substitute that throws or returns `null`, depending on the test;
- `IDispatcher` as a substitute;
- `AddFastEndpoints` on the Features assembly.

The host's pipeline is `UseRequestPipeline(policy)` → `UseFastEndpoints()` → `app.Run(terminal)`. FastEndpoints
answers `/api/measurements`. Any other path falls through to the terminal delegate the test supplies, which is how
spec 013's cases keep running.

**Cases**:
- **US1**: `POST /api/measurements` with `X-API-Key` while the substitute throws
  `InvalidOperationException("Failed to connect to 10.0.0.5:5432")`. Expect:
  - 500 with the fixed body;
  - one 2020 entry;
  - one 2002 entry with status 500 and the fixed body;
  - no other Error entries;
  - no `10.0.0.5` anywhere.
- **US2**: covered by the same request's 2002 assertions, plus a second run with `LogResponseBody = false`. That run
  expects one 2002 entry and no exception text.
- **US3**: the same failing request with `Origin: https://app.example` gets `Access-Control-Allow-Origin:
  https://app.example`. A request with `Origin: https://evil.example` gets none. A terminal-delegate failure from
  the allowed origin also carries the header, which covers every web-app 500.
- **FR-005**: the substitute returns `null` (unknown key). Expect 401, not 500, and no 2020 entry.
- **FR-006**: spec 013's three pipeline cases, unchanged except that the helper is now the shared one.

**Verifying that "the hosting layer logs no error"**: TestServer has no Kestrel, so the test checks that only 2020
and 2002 are at Error level. The live run (quickstart §2) checks the real Kestrel log.

## R6 — Documentation of the order

**Decision** (FR-008):
- Add a short "Request pipeline order" subsection to `DevelopmentGuide.md`, next to the logging section. It gives the
  order from R1 and says which steps the exception handler covers (everything from authorization inward) and which it
  doesn't.
- Add an XML doc comment on `UseRequestPipeline` that says the same in one sentence.
- Replace the comments in `Program.cs` around the five moved lines with a pointer to the method.
