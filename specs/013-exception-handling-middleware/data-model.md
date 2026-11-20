# Data Model: Safe Handling of Unexpected Errors

**Feature**: [spec.md](spec.md) | **Date**: 2026-11-20

Nothing is stored. No entity, table or migration changes. The two shapes below are what this feature produces.

## Error answer (HTTP body, sent to the caller)

| Field | Type | Value | Rule |
|---|---|---|---|
| `statusCode` | int | `500` | Always 500 (FR-003) |
| `message` | string | `"An unexpected error occurred."` | Fixed text, never from the exception (FR-001, FR-002) |
| `traceId` | string | `Activity.Current?.Id ?? TraceIdentifier` | The same value as in the log entry (research R3) |

- Content type is `application/problem+json`, with `Cache-Control: no-cache, no-store`.
- There is no `errors` field (research R2).
- It is not sent when the caller disconnected (FR-005) or the response had already started (FR-007).

Full contract: [contracts/error-response.md](contracts/error-response.md).

## Unexpected-error log entry

| Property | Source | Rule |
|---|---|---|
| EventId | `2020` / `UnhandledException` | Fixed, Infra/Logging range (FR-008) |
| Level | `Error` | Not written for a caller disconnect (FR-005) |
| `ReasonCode` | `"UnhandledException"` | Stable, documented in `DevelopmentGuide.md` (FR-011) |
| `ExceptionType` | `exception.GetType().FullName` | |
| `Method` | `Request.Method` | |
| `Path` | `Request.Path` | No query string, no body |
| `TraceId` | same as the error answer | Links the answer to the entry (SC-005) |
| Exception | the exception object | Message, stack trace and inner exceptions go to the log only |

## How an exception is handled

```text
exception reaches the handler
├─ OperationCanceledException and RequestAborted cancelled → return (no error log, no body)
├─ Response.HasStarted          → log 2020 → Abort() → return
└─ otherwise                    → log 2020 → Response.Clear() → 500 + error answer
```
