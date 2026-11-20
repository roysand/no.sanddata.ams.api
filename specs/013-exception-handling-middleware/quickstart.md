# Quickstart: Checking the Unexpected-Error Handling

**Feature**: [spec.md](spec.md) | **Contract**: [contracts/error-response.md](contracts/error-response.md)

## 1. Automated tests

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~Features.Tests.Middleware"
dotnet test
dotnet format --verify-no-changes
```

Expected: every case in research R8 passes, the full suite passes, and there are no formatting changes or new build
warnings.

## 2. Real pipeline, by hand (local)

No endpoint throws on purpose, so make the database unreachable:

1. Start the API with a wrong database port, for example by overriding `ApplicationSettings:DbConnectionString` with
   `Port=5999` in `local.settings.json`, and `RunMigrationsAtStartup=false`.
2. Sign in is not possible without the database. Call an anonymous endpoint that reads the database instead:
   `POST /api/auth/login` with any email and password.
3. Expected answer: `500`, `Content-Type: application/problem+json`, the body from the contract with a `traceId`. Check
   that the body contains no `5999`, no `localhost`, and no `Npgsql`.
4. Expected log: one error entry with EventId 2020, `ReasonCode=UnhandledException`, the Npgsql exception type and
   stack trace, `Path=/api/auth/login`, `Method=POST`, and the same `TraceId`. There is also one `ResponseError`
   (2002) entry from the request log with status 500, no exception text in its attributes, and no third entry from
   Kestrel.
5. Restore the connection string.

Compare with a validation error, `POST /api/auth/login` with an empty body against a working database. It is still a
400 in the unchanged FastEndpoints shape, which confirms FR-010 and the content type in research R2.

## 3. Web app

With the database still unreachable (step 2), sign in from the web app. Expected: the login form shows "An unexpected
error occurred." instead of "Internal Server Error".

## 4. Done-when list from the backlog note

Tick each item in `specs/_backlog/exception-handling-middleware.md` against the above. Then delete the note (FR-014)
and add the follow-up note for out-of-scope failures (plan, Summary step 6).
