# Backlog: Exceptions thrown before the global exception handler

Status: done in spec 014 (2026-11-20). The handler was moved to just before authorization, not to the front. See
[spec.md](spec.md), Background. Found 2026-11-20 while doing spec 013 (`specs/013-exception-handling-middleware/`),
where it was put out of scope.

## What

`ExceptionHandlingMiddleware` (`app.UseExceptionHandling()` in `src/api/Program.cs`) only catches exceptions from the
steps after it: Scalar, `UseFastEndpoints` and everything the endpoints call. These steps run before it and are not
covered:

- forwarded headers and CORS;
- `UseAuthentication`, including the API-key handler, which reads the database. A database outage during an
  `X-API-Key` request from a sensor throws here;
- `RequestResponseLoggingMiddleware` itself;
- `UseAuthorization`.

## Why it matters

Nothing leaks. Outside Development, hosting answers an uncaught exception with an empty 500 body. But:

- the failure is logged by Kestrel ("An unhandled exception was thrown by the application"), not as event 2020 with
  the `UnhandledException` reason code, so searches and alerts on 2020 miss it;
- the caller gets no `traceId`, and the web app shows the browser's "Internal Server Error" instead of the fixed
  message.

## What to decide

- **Move `UseExceptionHandling()` to the front**, right after `UseForwardedHeaders`. Then the request log runs inside
  it and its `catch` fires for every exception, recording `ExceptionType` and a masked `ExceptionMessage` and logging
  `ResponseError` 500 before rethrowing. It would no longer see the 500 status or the fixed body. Check that this still
  gives one 2020 entry per failure, and decide whether the request log should keep its own `catch`.
- **Or keep the order** and accept Kestrel's log for these rare cases.
- Whichever is chosen, CORS headers must still be on the 500. `CorsMiddleware` adds them in `OnStarting`, so this
  should hold, but it needs a test.

## Done when

- [x] An exception in authentication (for example, the API-key lookup with the database down) is logged as event
      2020 and answered with the fixed 500 body and a `traceId`, or the decision to leave it is recorded here and
      the note removed.
- [x] The request log still records each failed request once and never the exception text.
- [x] Tests in `tests/Features.Tests/Middleware/` cover the new order.
