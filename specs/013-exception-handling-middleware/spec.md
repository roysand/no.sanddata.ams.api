# Feature Specification: Safe Handling of Unexpected Errors

**Feature Branch**: `feature/013-exception-handling-middleware`

**Created**: 2026-11-20

**Status**: Draft

**Input**: User description: "Fix the global exception handling middleware, from
specs/_backlog/exception-handling-middleware.md (every point in that note must be covered): never send the exception
text to the client, answer 500 with a fixed message and a trace id; treat InvalidOperationException and
ArgumentException as server errors; do not log a client disconnect as an error; do not write over a response that has
already started; log through a compiled LoggerMessage in the Infra/Logging range with a stable reason code; confirm the
request log does not leak exception text; check the frontend's error parsing against the new body; add tests; update
the docs that list reason codes; remove the backlog note when done."

**Source note**: the backlog note was moved here, to [source-backlog-note.md](source-backlog-note.md), when this
feature was done (FR-014). Links to `specs/_backlog/exception-handling-middleware.md` in these documents mean that
file.

## Background

When something goes wrong that the API did not plan for (a bug, a database outage, a broken dependency), one
catch-all step answers the request. Planned failures (not found, conflict, invalid input) are already answered by the
endpoints themselves and never reach that step. A review on 2026-11-19 found five faults in the catch-all step. It
exposes internal error text, reports server bugs as client mistakes, logs dropped connections as errors, can fail a
second time on a half-sent response, and logs in a way the project's logging rules do not allow.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An unexpected failure reveals nothing internal (Priority: P1)

A user of the web app, or a sensor sending readings, hits a request that fails unexpectedly, for example while the
database is down. They get a plain "something went wrong" answer and a reference id. They do not see server names,
ports, table or column names, file paths or any other internal error text. The maintainer can use the reference id to
find the full details in the server log.

**Why this priority**: today the raw error text goes to the caller. A database outage can reveal the database host and
port. This is the one fault with a security impact.

**Independent Test**: make a request fail with an error whose text contains a recognisable secret-like string. The
answer is a server error with the fixed message and a reference id, and the string does not appear in it. The server
log has an entry with the same reference id and the full details.

**Acceptance Scenarios**:

1. **Given** a request whose processing fails unexpectedly, **When** the API answers, **Then** the status is 500, the
   body has the same fixed message every time and a reference id, and nothing from the original error text.
2. **Given** that failed request, **When** the maintainer searches the server log for the reference id, **Then** they
   find one error entry with the error type, the full details, the request path and the method.
3. **Given** the web app gets that answer, **When** it shows the error, **Then** the user sees the fixed message, not
   a raw status text or internal details.

---

### User Story 2 - Server bugs are reported as server errors (Priority: P1)

The maintainer watches the logs and alerts for server errors. A bug in the API, such as a query that expects a row
that is not there or a data-tracking conflict, must show up as a server error. Today it is reported as a client error
("bad request") and gets lost among real client mistakes.

**Why this priority**: misclassified errors hide real bugs. The fix is cheap and makes every later fault visible.

**Independent Test**: make a request fail with each kind of error that is reported as a client error today ("invalid
operation", "invalid argument", "not found", "unauthorized"). Each answer is a 500 and is logged as an unexpected
error.

**Acceptance Scenarios**:

1. **Given** a request whose processing fails with an "invalid operation" or "invalid argument" error, **When** the
   API answers, **Then** the status is 500, not 400.
2. **Given** a request whose processing fails with any other unplanned error type, **When** the API answers, **Then**
   the status is 500.
3. **Given** a planned failure (validation error, not found, conflict, unauthorized) returned by an endpoint, **When**
   the API answers, **Then** its status and body are unchanged by this feature.

---

### User Story 3 - Dropped connections do not raise alarms (Priority: P2)

A user closes the browser tab, or a sensor loses its connection, while a request is still running. The work is
stopped. This is normal and must not appear in the log as an error or be counted as a server failure.

**Why this priority**: it keeps the error log meaningful. Without it, normal disconnects look like server faults.

**Independent Test**: start a request and cancel it from the client side while it runs. No error entry is written and
no error body is sent.

**Acceptance Scenarios**:

1. **Given** a request whose caller disconnects mid-processing, **When** the processing stops because of that,
   **Then** no error-level log entry is written and no error body is sent.
2. **Given** a request that is cancelled for a reason other than the caller disconnecting (for example an internal
   timeout), **When** the API answers, **Then** it is treated as an unexpected failure (500, logged as an error).

---

### User Story 4 - A failure mid-response is handled once (Priority: P2)

A response has already started streaming to the caller when an unexpected failure happens. The API must not try to
replace it with an error answer, which fails a second time and buries the original error. It logs the original
failure once, and the caller can tell that the response did not finish.

**Why this priority**: rare, but when it happens today the log shows a second, misleading error and the original cause
is harder to find.

**Independent Test**: make a request fail after part of its response has been sent. The log has the original failure,
no secondary error comes from the error-handling step itself, and the partly sent response is not changed or extended
with an error body.

**Acceptance Scenarios**:

1. **Given** a response that has already started, **When** an unexpected failure happens, **Then** the original
   failure is logged, the status already sent is not changed, and no error body is appended.
2. **Given** that situation, **When** the caller reads the response, **Then** the connection ends without completing
   the response normally, so the caller can tell it is incomplete.

---

### User Story 5 - The unexpected-error log is consistent and queryable (Priority: P3)

The maintainer filters the log for unexpected failures by a fixed event id and reason code. These are documented with
the project's other event ids and reason codes, so alerts and searches can depend on them.

**Why this priority**: it follows the project's logging rules and makes Stories 1 and 2 actionable. It is useful only
together with them.

**Independent Test**: trigger an unexpected failure and check that the log entry has the documented event id, the
reason code `UnhandledException`, the error type, path and method, and no request body.

**Acceptance Scenarios**:

1. **Given** an unexpected failure, **When** it is logged, **Then** the entry has an event id in the Infra/Logging
   range (2000-2099), the reason code `UnhandledException`, the error type, the request path and method, and the
   reference id sent to the caller.
2. **Given** a request that carries a body, **When** its unexpected failure is logged, **Then** the body is not in the
   entry.
3. **Given** the project's documentation of event ids and reason codes, **When** this feature is done, **Then** the new
   event id and reason code are listed there.

---

### Edge Cases

- **Request log after a 500**: the request/response log entry for a failed request must not contain the original error
  text. This holds in both the normal case (the error answer was written) and the mid-response case, unless the
  maintainer has configured the request log to include error text.
- **Error text containing secrets**: a database error whose text holds a connection detail reaches the caller as the
  fixed message only.
- **An error wrapped in another error**: the outer error decides the status (always 500). Nothing from the inner error
  goes to the caller.
- **Cancellation not caused by the caller**: handled as an unexpected failure (Story 3, scenario 2).
- **Failures before the request reaches the endpoints** (for example in authentication, which can query the database
  for API keys): out of scope, see Assumptions.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: An unexpected failure MUST be answered with status 500 and a body that holds a fixed, generic message and
  a reference id unique to the request.
- **FR-002**: The answer to an unexpected failure MUST NOT contain any text taken from the error: no message, type
  name, stack trace or inner error.
- **FR-003**: Every error type that reaches the catch-all step MUST be answered with 500, including "invalid
  operation", "invalid argument", "key not found" and "unauthorized access" errors. Mapping a specific error type to a
  4xx status is allowed only for a case shown to need it. None is known today.
- **FR-004**: The body of the 500 answer MUST use the same shape as the API's other error answers. That means the
  `statusCode` and `message` fields the web app already reads, plus the reference id.
- **FR-005**: A failure caused by the caller disconnecting MUST NOT be logged at error level and MUST NOT get an error
  body.
- **FR-006**: A cancellation not caused by the caller disconnecting MUST be handled as an unexpected failure (FR-001).
- **FR-007**: If the response has already started when an unexpected failure happens, the system MUST log the failure
  once. It MUST NOT change the status or write an error body, and MUST end the response so it is visibly incomplete.
  The error-handling step itself MUST NOT cause a secondary failure.
- **FR-008**: Each unexpected failure MUST be logged once at error level with the full error details (type, message,
  stack trace), the reason code `UnhandledException`, the request path, the method and the reference id. The log entry
  MUST NOT include the request body, and MUST use a fixed, documented event id in the Infra/Logging range (2000-2099).
- **FR-009**: The request/response log MUST NOT record the original error text for a failed request unless the
  maintainer has explicitly configured it to.
- **FR-010**: Planned failures (validation errors and `Result` errors returned by endpoints) MUST keep their current
  status codes and bodies.
- **FR-011**: The new event id and the `UnhandledException` reason code MUST be added to the project's documentation of
  event ids and reason codes.
- **FR-012**: Automated tests MUST cover each case:
  - an unexpected error gives 500 with the fixed message and no error text;
  - "invalid operation" and "invalid argument" errors give 500;
  - a caller disconnect logs no error and writes no body;
  - a failure after the response started causes no second failure and no appended body;
  - the log entry has the event id and reason code.
- **FR-013**: The web app's error handling MUST be checked against the new body. It must show the fixed message for a
  500 without a change on its side.
- **FR-014**: The backlog note `specs/_backlog/exception-handling-middleware.md` MUST be removed when this feature is
  done.

### Key Entities

- **Error answer**: what the caller receives for an unexpected failure. It holds the status code (500), a fixed
  message and a reference id. It never holds error text.
- **Unexpected-error log entry**: what the maintainer sees. It holds the event id, the reason code
  `UnhandledException`, the error type and full details, the path, the method and the reference id.
- **Reference id**: links one error answer to its log entry. It is unique per request.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For 100% of tested unexpected failures, including one whose error text contains a database host and
  port, the answer contains none of the error text.
- **SC-002**: 100% of errors that reach the catch-all step are answered with 500. None of the error types reported as
  400, 401 or 404 today keeps that status.
- **SC-003**: A cancelled-by-caller request produces 0 error-level log entries.
- **SC-004**: A failure after the response started produces exactly 1 error entry for the original failure and 0
  secondary failures from the error-handling step.
- **SC-005**: Given a reference id from an error answer, the maintainer finds its full details in the log with a single
  search.
- **SC-006**: The web app shows the fixed message for a 500, not "Internal Server Error", with no change to the web
  app.
- **SC-007**: Every scenario in FR-012 has a passing automated test, and the full test suite passes.

## Assumptions

- **Body shape**: the API's other error answers use FastEndpoints' default shape: `statusCode`, `message` and an
  optional `errors` map. The web app (`no.sanddata.ams.frontend`, `src/lib/apiClient.ts`) reads `message`, `code` and
  `errors`. It never reads the `error` field that the catch-all step sends today, so a 500 currently shows the browser's
  status text. The 500 body therefore uses the same shape plus a reference id (the trace id), not a ProblemDetails
  shape. That would make 500 answers differ from every other error answer and would need a web-app change for no gain.
  The web app needs no change, and its message improves from "Internal Server Error" to the fixed message.
- **Fixed message**: "An unexpected error occurred." (or equivalent short English text), the same for every
  unexpected failure.
- **Reference id**: the request's existing trace identifier, so it also matches the request log and any tracing.
- **Request log**: the request/response logging step runs outside the catch-all step. In the normal case it only sees
  the 500 and the fixed body. It sees the original error only in the mid-response case. There it records the error
  text masked by default, because `ExceptionMessage` is not in the default `AttributesToLog`. This default stays. A
  maintainer who adds `ExceptionMessage` to `AttributesToLog` chooses to log it (FR-009).
- **Scope**: only failures that reach the catch-all step, which is everything from the endpoints inward. Failures in
  steps that run before it (CORS, authentication including the API-key database lookup, request logging) are out of
  scope. Hosting answers them with an empty 500 body, which leaks nothing. Moving the catch-all step earlier would
  change how the request log sees errors and is left for a later backlog item if needed.
- **Background jobs and startup**: price fetching, admin bootstrap and migrations at startup were reviewed in the
  backlog note and need no change.
- **No new configuration**: the behaviour is fixed. There is no switch to send error text to callers, even in
  development.
