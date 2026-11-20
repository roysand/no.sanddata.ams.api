# Quickstart: Checking Failures During Sign-In Checks

**Feature**: [spec.md](spec.md) | **Contract**: [contracts/api-key-endpoints.md](contracts/api-key-endpoints.md)

## 1. Automated tests

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~Features.Tests.Middleware"
dotnet test
dotnet format --verify-no-changes --include <touched files>   # the whole-repo check fails on main, see spec 013 T015
```

Expected: every case in research R5 passes, spec 013's middleware tests still pass, and the full suite passes.

## 2. Real pipeline, by hand (local)

Repeat the baseline from research.md with the change in place:

```bash
ApplicationSettings__DbConnectionString="Host=localhost;Port=5999;Database=amsdb;Username=x;Password=x;Timeout=3" \
RunMigrationsAtStartup=false ASPNETCORE_ENVIRONMENT=Development \
dotnet run --project src/api/api.csproj --launch-profile http

curl -i -X POST http://localhost:5231/api/measurements \
  -H 'Content-Type: application/json' -H 'X-API-Key: anything' -H 'Origin: http://localhost:5173' -d '{}'
```

Expected:
- **Response**: `500`, `application/problem+json`, the fixed body with a `traceId`, and
  `Access-Control-Allow-Origin: http://localhost:5173`. No `5999`, `Npgsql` or stack trace (the baseline had all
  three).
- **Log**:
  - one `ExceptionHandlingMiddleware[2020]` entry with the same `TraceId` and the Npgsql stack trace;
  - one `RequestResponseLoggingMiddleware[2002]` entry with status 500 and no `ExceptionMessage`;
  - no `DeveloperExceptionPageMiddleware` or Kestrel "unhandled exception" entry.

Then check the refusals, which need no database:
- the same request without `X-API-Key` → 401;
- a validation error on `POST /api/auth/login` with `{}` → 400, unchanged.

## 3. Done-when list from the backlog note

Tick each item in `specs/_backlog/exceptions-before-handler.md` against the above. Then move the note into this folder
as `source-backlog-note.md` (FR-009).
