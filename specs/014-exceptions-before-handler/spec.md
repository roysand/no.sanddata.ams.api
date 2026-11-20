# Feature Specification: Catch Failures During Sign-In Checks

**Feature Branch**: `feature/014-exceptions-before-handler`

**Created**: 2026-11-20

**Status**: Draft

**Input**: User description: "Handle exceptions thrown before the global exception handler, from
specs/_backlog/exceptions-before-handler.md (every point must be covered). Failures before the handler, including the
API-key database lookup during a database outage, are answered with an empty 500 and logged by the hosting layer
instead of as event 2020 with a trace id. They should be logged as event 2020 and answered with the fixed 500 body and
trace id (spec 013 contract). The request log must still record each failed request once and never the exception
text, and CORS headers must stay on the 500. Decide where the handler goes, add tests for the new order, and move the
backlog note out of _backlog/ when done."

**Source note**: the backlog note was moved here, to [source-backlog-note.md](source-backlog-note.md), when this
feature was done (FR-009). Links to `specs/_backlog/exceptions-before-handler.md` in these documents mean that file.

## Background

Spec 013 made the API's catch-all step answer every unexpected failure the same way:
- a fixed message and a reference id (trace id) for the caller;
- one log entry with event 2020 and the reason code `UnhandledException` for the maintainer.

The catch-all step only covers what runs after it. Checking a sensor's API key runs before it, and that check reads
the database. When the database is down during a sensor's reading upload, the failure escapes:
- the sensor gets an empty 500 with no reference id;
- the failure is logged by the hosting layer in its own format, so searches and alerts on event 2020 miss it.

**Decision (2026-11-20)**: the catch-all step moves one step earlier, to just before the sign-in and permission checks
for each endpoint, after the request log. This was chosen over the other options in the backlog note:
- **A single catch-all at the very front** would change how the request log sees failures.
- **A second catch-all as a safety net** covers steps that do no I/O and have never failed.
- **Leaving the order as it is** keeps the sensor outage case uncaught.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A sensor upload during a database outage gets the standard error answer (Priority: P1)

A field sensor sends readings with its API key while the database is unreachable. Checking the key fails because the
key cannot be looked up.
- The sensor gets the same fixed 500 answer and reference id as any other unexpected failure.
- The maintainer finds the failure under event 2020 with the reason code `UnhandledException`, the same as every other
  unexpected failure.

**Why this priority**: this is the one failure in the backlog note that happens in practice. A database outage hits
every sensor at once, and today it is invisible to event-2020 alerts.

**Independent Test**: send a request with an API key to a sensor endpoint while the key lookup fails. The answer is a
500 with the fixed body and a reference id, and the log has one event-2020 entry with the same reference id.

**Acceptance Scenarios**:

1. **Given** an endpoint that accepts API keys and a key lookup that fails unexpectedly, **When** a request with an
   API key arrives, **Then** the answer is 500 with the fixed message and a reference id, and no text from the
   failure.
2. **Given** that failed request, **When** the maintainer searches the log for the reference id, **Then** they find
   exactly one event-2020 entry with the error details, path and method.
3. **Given** that failed request, **When** the hosting layer finishes the request, **Then** it logs no error of its
   own for it.

---

### User Story 2 - The request log keeps recording failed requests the same way (Priority: P1)

The maintainer relies on the request log: one entry per request, with the status and the masked attributes. For a
failed key check, it must show the same thing it shows for any other unexpected failure: one error entry with status
500, the fixed body when body logging is on, and never the failure's text.

**Why this priority**: moving the catch-all step must not degrade the existing log. Today the request log records a
failed key check through a fallback path that logs the error text, masked by default.

**Independent Test**: trigger a failed key check and inspect the request log. There is one error entry with status
500, no failure text, and the fixed body if body logging is on.

**Acceptance Scenarios**:

1. **Given** a failed key check, **When** the request log records the request, **Then** there is exactly one
   request-log error entry for it, with status 500.
2. **Given** body logging is on, **When** that entry is written, **Then** it contains the fixed error body.
3. **Given** any request-log setting, **When** that entry is written, **Then** it contains no text from the failure.

---

### User Story 3 - The web app can still read the error answer (Priority: P2)

The web app runs on a different origin from the API, so the browser only lets it read a response that carries the
cross-origin permission headers. When an unexpected failure happens, the 500 answer must keep those headers. The web
app can then show the fixed message instead of a generic network error.

**Why this priority**: the catch-all step clears the response before it writes the error. If that also removed the
cross-origin headers, the web app would get "failed to fetch" and the fixed message would never reach the user. The
web app does not use API keys, so this applies to every unexpected 500 it receives. Spec 013 assumed the headers
survive but did not test it.

**Independent Test**: send a cross-origin request from an allowed origin that fails unexpectedly. The 500 answer
carries the allow-origin header for that origin.

**Acceptance Scenarios**:

1. **Given** a request from an allowed origin that fails unexpectedly, **When** the API answers 500, **Then** the
   answer carries the allow-origin header for that origin.
2. **Given** a request from an origin that is not allowed, **When** it fails unexpectedly, **Then** the answer carries
   no allow-origin header, as for any other answer to that origin.

---

### Edge Cases

- **Key rejected, not failed**: a missing, empty, unknown or expired key is a planned refusal (401/403), not a
  failure. It keeps its current answer and is not logged as event 2020.
- **Signed-in user with a valid token on a key-or-token endpoint**: the key check still runs if a key header is sent.
  If no key header is sent, no lookup happens. Either way the behaviour is unchanged unless the lookup fails.
- **Caller disconnects during the key lookup**: handled as in spec 013. There is no error log and no body.
- **Failures in the steps that stay before the catch-all** (forwarded headers, cross-origin handling, token sign-in,
  the request log itself): out of scope by decision. They do no I/O and have no known failure. If one ever fails, the
  hosting layer answers with an empty 500 as today.
- **Endpoints that allow anonymous access**: no key check runs, so nothing changes.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: An unexpected failure during the sign-in and permission checks for an endpoint, including the API-key
  lookup, MUST be answered as in spec 013: status 500, the fixed message, a reference id, and no failure text.
- **FR-002**: Such a failure MUST be logged exactly once as event 2020 with the reason code `UnhandledException`, the
  error details, path, method and reference id. The hosting layer MUST NOT log a separate error for it.
- **FR-003**: The request log MUST record such a failure with exactly one error entry with status 500. It holds the
  fixed body when body logging is on, and never the failure text, whatever the request-log settings are.
- **FR-004**: Every unexpected 500 answer MUST carry the same cross-origin headers that a successful answer to the
  same request would carry.
- **FR-005**: Planned refusals (missing, empty, unknown or expired key, missing token, wrong role) MUST keep their
  current status codes and bodies.
- **FR-006**: Everything spec 013 guarantees for failures inside endpoints MUST still hold: always 500, caller
  disconnect not logged, started response not written over, one event-2020 entry.
- **FR-007**: Automated tests MUST cover each case, using the real order of the request-processing steps:
  - a failing key lookup gives the fixed 500 and one event-2020 entry;
  - the request log has one error entry without failure text;
  - an allowed origin's 500 carries the allow-origin header;
  - a rejected key is still a refusal, not a 500.
- **FR-008**: The documentation that describes the order of the request-processing steps MUST be updated to say which
  steps the catch-all covers.
- **FR-009**: The backlog note `specs/_backlog/exceptions-before-handler.md` MUST be moved out of `_backlog/` into
  this feature's folder when the feature is done, with its "done when" items answered.

### Key Entities

- **Catch-all step**: the step from spec 013 that turns an unexpected failure into the fixed 500 and an event-2020
  entry. This feature only changes where it sits.
- **Key check**: the check that looks up a sensor's API key in the database. It runs as part of the permission check
  for endpoints that accept API keys.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: During a database outage, 100% of API-key requests get the fixed 500 body with a reference id. 0% get
  an empty body.
- **SC-002**: Each such failure produces exactly 1 event-2020 entry, 1 request-log error entry and 0 hosting-layer
  error entries.
- **SC-003**: 0 log entries or answers contain text from the failure, across all request-log settings tested.
- **SC-004**: The web app receives a readable 500 from an allowed origin in 100% of tested unexpected failures.
- **SC-005**: All existing tests, including spec 013's middleware tests, pass unchanged or with only pipeline-order
  updates.

## Assumptions

- **Where the key check runs**: the API's default sign-in scheme is the token scheme, which does no I/O. The API-key
  check runs only when an endpoint that accepts API keys is checked for permission. That happens after the request
  log and before today's catch-all step, which is why moving the catch-all one step earlier is enough. The plan
  confirms this with a test.
- **Request log unchanged**: the request log stays outside the catch-all step, as spec 013 tested. Its own fallback
  path for exceptions stays. After this change it should only run for failures in the steps that remain uncovered.
- **Cross-origin headers**: these are expected to survive the catch-all step clearing the response, because they are
  added when the response starts. The tests confirm it. If they don't survive, restoring them is in scope.
- **The fixed body and the event 2020 shape** are reused from spec 013 unchanged. No new log event or reason code.
- **Production order**: production uses the same step order as development. The only differences are the API
  documentation page and HTTPS redirection, which sit after the catch-all and are not affected.
