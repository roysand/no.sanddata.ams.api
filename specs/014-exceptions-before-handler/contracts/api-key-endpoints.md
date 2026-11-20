# Contract: Unexpected Failures on API-Key Endpoints

**Applies to**: every endpoint that accepts an API key (`AuthSchemes(..., "ApiKey")`), for example
`POST /api/measurements`, when checking the key fails unexpectedly (such as a database outage). It also applies to
the cross-origin headers on every unexpected 500.

**Body and headers**: exactly the
[spec 013 error response](../013-exception-handling-middleware/contracts/error-response.md). No new field.

## Before

Production:

```http
HTTP/1.1 500 Internal Server Error
Content-Length: 0
```

Development, measured 2026-11-20:

```http
HTTP/1.1 500 Internal Server Error
Content-Type: text/plain; charset=utf-8

System.InvalidOperationException: An exception has been raised that is likely due to a transient failure.
 ---> Npgsql.NpgsqlException (0x80004005): Failed to connect to 127.0.0.1:5999
 ...full stack trace and request headers...
```

## After (all environments)

```http
HTTP/1.1 500 Internal Server Error
Content-Type: application/problem+json
Cache-Control: no-cache, no-store
Access-Control-Allow-Origin: <origin>     ← only for an allowed origin, as on any other answer
Vary: Origin

{ "statusCode": 500, "message": "An unexpected error occurred.", "traceId": "00-…-…-00" }
```

## Unchanged

| Situation | Answer |
|---|---|
| No `X-API-Key` header on an API-key-only endpoint | 401, as today |
| Empty, unknown or expired key | 401, as today |
| Valid key | the endpoint's normal answer |

None of these are logged as event 2020.

## Sensor (forwarder) compatibility

The forwarder only needs to treat any 5xx as "retry later", and it already sees a 500 today. A body is new on
Production, but a client that ignores bodies is unaffected.
