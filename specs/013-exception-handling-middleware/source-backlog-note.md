# Backlog: Fix the global exception handling middleware

Status: not started. Found 2026-11-19 while checking whether the API has unhandled exceptions. Roy marked it
important: go through every point below before closing this. Remove this note when it is done.

## What was checked

The API has no unhandled exceptions in the sense of crashes or lost errors:

- Every HTTP request passes `ExceptionHandlingMiddleware`
  (`src/Infrastructure/Middleware/ExceptionHandlingMiddleware.cs`), registered with `app.UseExceptionHandling()` in
  `src/api/Program.cs` just before `app.UseFastEndpoints()`. An exception from an endpoint, handler or EF Core is
  caught, logged with its stack trace, recorded by `RequestResponseLoggingMiddleware`, and answered with a JSON error.
- Expected failures are values (`Result` + `Error`), and validation errors are handled by FastEndpoints, so they never
  reach the middleware.
- `PriceFetchService` wraps each cycle in `try/catch` (except `OperationCanceledException`). This matters: since .NET 8
  an exception that escapes a `BackgroundService` stops the whole host by default.
- `AdminBootstrapService.StartAsync` is wrapped the same way.
- No fire-and-forget tasks (`Task.Run`, `_ = ...Async()`, `ContinueWith`) in `src/`.
- Migrations at startup can stop the app, on purpose: a failed migration must not run.

## What is wrong in the middleware

1. **The exception message goes to the client, also on a 500.** `HandleExceptionAsync` writes
   `{ error = exception.Message }`. A database outage can then return Npgsql's text with host and port; other
   exceptions can reveal table, column or file names. Log the details, send a fixed message (and a trace id).
2. **`InvalidOperationException` (and `ArgumentException`) becomes 400.** EF Core and LINQ throw
   `InvalidOperationException` for real server bugs ("Sequence contains no elements", tracking and concurrency
   errors). They are reported as client errors and hide among the 400s instead of standing out as 500s. Business and
   validation failures are already `Result` values, so an exception reaching the middleware is a server error: answer
   500. Keep a 4xx mapping only for a case that is shown to need it.
3. **A client that disconnects is logged as an error.** `OperationCanceledException` from `HttpContext.RequestAborted`
   is logged with `LogError` and answered with a 500 nobody receives. Skip it (no error log, no body).
4. **It writes after the response has started.** If an exception happens once the response has begun, setting the
   status code and writing the body throws a second exception. Check `context.Response.HasStarted`: log and rethrow
   (or stop) instead of writing.
5. **The log is not a `LoggerMessage`.** `_logger.LogError(ex, "An unhandled exception occurred.")` breaks the
   project's logging rule (compiled `LoggerMessage` with a stable reason code, `CLAUDE.md`). Add it to the
   Infra/Logging range (2000-2099) with a reason code such as `UnhandledException`, the exception type, the path and
   the method, never the request body.

## Also check

- `RequestResponseLoggingMiddleware` logs `ExceptionMessage` (masked or not, depending on `RequestLogging` settings)
  when it sees an exception; after the fix the exception handler catches first, so confirm what the request log
  records for a 500 and that no exception message leaks there either.
- The response body: a ProblemDetails shape (`type`, `title`, `status`, `traceId`) would match what ASP.NET Core and
  FastEndpoints clients expect; agree it with the frontend, which may parse `error` today.
- There are no tests for the middleware. Add tests: an unexpected exception gives 500 with a fixed message and no
  exception text; an `InvalidOperationException` gives 500; a cancelled request logs no error; an exception after the
  response started does not throw a second time; the log entry has the event id and reason code.

## Done when

- [ ] An unexpected exception returns 500 with a fixed message, never `exception.Message`.
- [ ] `InvalidOperationException` and `ArgumentException` return 500 (unless a shown case needs 4xx).
- [ ] A client disconnect is not logged as an error and gets no error body.
- [ ] An exception after the response started is logged and not written over the response.
- [ ] The log uses a `LoggerMessage` with an EventId in 2000-2099 and a stable reason code; `CLAUDE.md` and
      `DevelopmentGuide.md` list it if they document reason codes.
- [ ] The frontend's error handling is checked against the new body.
- [ ] Middleware tests cover the cases above; `dotnet test` passes.
