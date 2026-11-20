# Data Model: Catch Failures During Sign-In Checks

**Feature**: [spec.md](spec.md) | **Date**: 2026-11-20

No data changes. Nothing is stored, and there is no entity, table or migration.

The two shapes this feature produces are spec 013's, reused unchanged:

- **Error answer**: `{ statusCode: 500, message, traceId }`. See
  [013 data-model](../013-exception-handling-middleware/data-model.md#error-answer-http-body-sent-to-the-caller).
- **Unexpected-error log entry**: event 2020, `UnhandledException`. See
  [013 data-model](../013-exception-handling-middleware/data-model.md#unexpected-error-log-entry).

## What changes: which requests reach those shapes

| Where a failure happens | Before (spec 013) | After (this feature) |
|---|---|---|
| Endpoint, handler, EF Core inside an endpoint | 2020 + fixed 500 | unchanged |
| **Authorization, including the API-key lookup** | Production: empty 500 + hosting log. Development: stack trace in the body. Request log: 2002 through its `catch` | **2020 + fixed 500. Request log: normal 2002 with the fixed body** |
| Request log itself, JWT authentication, CORS, forwarded headers | Empty 500 + hosting log | unchanged (out of scope) |
