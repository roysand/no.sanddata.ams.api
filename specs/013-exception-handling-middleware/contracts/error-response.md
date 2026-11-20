# Contract: Unexpected-Error Response

**Applies to**: every endpoint, whenever an exception reaches the global handler.

**Does not change**: validation errors (400) and `Result` errors (400/401/403/404/409) sent by endpoints. They keep
FastEndpoints' `ErrorResponse` exactly as today.

## Before (today)

```http
HTTP/1.1 400 Bad Request            ← or 404/401/500, depending on the exception type
Content-Type: application/json

{ "error": "Failed to connect to 10.0.0.5:5432" }   ← the raw exception message
```

## After

```http
HTTP/1.1 500 Internal Server Error
Content-Type: application/problem+json
Cache-Control: no-cache, no-store

{
  "statusCode": 500,
  "message": "An unexpected error occurred.",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
}
```

| Field | Always present | Notes |
|---|---|---|
| `statusCode` | yes | Always `500` |
| `message` | yes | Fixed text |
| `traceId` | yes | Quote it when reporting a problem. The maintainer finds the full details in the log under EventId 2020 with this `TraceId` |

There is no `errors` field. No field ever contains exception text.

## No response body

- **The caller disconnected**: nothing is sent. The caller is gone.
- **The response had already started**: the connection is reset. The caller sees an incomplete response, not a 500.

## Web app compatibility

`no.sanddata.ams.frontend/src/lib/apiClient.ts` builds `ApiError(status, body.code ?? 'Unknown', message)`, where
`message` comes from `body.errors` if present, otherwise `body.message`, otherwise `statusText`. For this body that
gives `ApiError(500, 'Unknown', 'An unexpected error occurred.')`. Today users see "Internal Server Error" (the old
`error` field was never read). No web app change is needed.
