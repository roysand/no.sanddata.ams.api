# Feature Specification: Measurement Query Endpoints

**Feature Branch**: `001-measurement-query-endpoints`

**Created**: 2026-09-24

**Status**: Draft

**Input**: User description: "As a registered household user, I want to view my own power-usage
measurement data (collected from my registered meters at my locations) through the API, so a
future frontend can show me recent and historical consumption. I should only ever be able to see
data for locations I'm associated with. Measurements are already being ingested from my sensors;
this feature is about reading that data back out."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - View recent usage for a location (Priority: P1)

A household user who has already registered a location and at least one meter wants to check
their recent power usage — "what has my house been using lately?" — without having to pick an
exact date range.

**Why this priority**: This is the smallest slice that delivers real, visible value: it's the
first time any user-facing surface can show *anything* from the data the system has been
ingesting. Without it, the ingestion feature has no observable outcome for the user.

**Independent Test**: Can be fully tested by authenticating as a user with a registered location
that has ingested measurements, requesting recent measurements for that location, and confirming
the readings returned belong to that location and fall within the recent window.

**Acceptance Scenarios**:

1. **Given** a user with a registered location that has measurement data from the last 24 hours,
   **When** the user requests recent measurements for that location, **Then** the system returns
   the measurements from that window, ordered from oldest to newest.
2. **Given** a user with a registered location that has no measurement data yet, **When** the
   user requests recent measurements for that location, **Then** the system returns an empty
   result rather than an error.

---

### User Story 2 - Browse historical usage over a chosen date range (Priority: P2)

A household user wants to look back over a specific period (e.g. last month, or a specific week)
to understand consumption patterns or compare against a bill.

**Why this priority**: Builds directly on User Story 1's query capability by adding a
user-chosen time range; it's the natural next step for trend analysis but isn't needed for the
first usable view.

**Independent Test**: Can be fully tested by requesting measurements for a location with an
explicit start and end date and confirming only measurements within that range are returned.

**Acceptance Scenarios**:

1. **Given** a user with measurement data spanning several weeks, **When** the user requests
   measurements for a specific two-week window, **Then** the system returns only the
   measurements whose timestamps fall within that window.
2. **Given** a user requests a date range with no matching measurements, **When** the request is
   made, **Then** the system returns an empty result rather than an error.
3. **Given** a user requests a date range where the end is before the start, **When** the request
   is made, **Then** the system rejects the request with a clear validation error.

---

### User Story 3 - Discover which locations and meters are available to query (Priority: P3)

A household user (or the frontend acting on their behalf) needs to know which of their
registered locations and meters actually have data, before picking one to query.

**Why this priority**: Supporting capability rather than the core value itself — a user with
only one location can be assumed to know it, but this becomes necessary as soon as someone has
more than one, and it's what lets a frontend build a location/meter picker instead of hardcoding
IDs.

**Independent Test**: Can be fully tested by authenticating as a user associated with multiple
locations and confirming the returned list contains exactly those locations (and their meters),
and no others.

**Acceptance Scenarios**:

1. **Given** a user associated with two locations, **When** the user requests their list of
   locations and meters, **Then** both locations and their registered meters are returned, with
   no locations belonging to other users included.

---

### Edge Cases

- What happens when a user requests measurements for a location they are not associated with?
  The system must not reveal whether the location exists at all — reject the same way for
  "not mine" and "doesn't exist."
- What happens when a user requests measurements for a specific meter that does not belong to
  the specified location?
- What happens when a user requests an extremely large time range (e.g. multiple years) in one
  call?
- What happens when a location has multiple meters and no specific meter is requested — are
  readings from all its meters combined, or is a meter selection required?
- How does the system behave for a user who is not associated with any location yet?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow an authenticated user to retrieve measurement data only for
  locations they are associated with; requests for locations the user is not associated with
  MUST be rejected without revealing whether the location exists.
- **FR-002**: System MUST allow a user to request measurements for a specific location and,
  optionally, narrow the request to a specific meter registered at that location.
- **FR-003**: System MUST allow a user to request measurements within an explicit start/end time
  range, and MUST reject a request where the end is before the start.
- **FR-004**: System MUST allow a user to request their most recent measurements for a location
  without specifying an explicit time range, to support a "recent usage" view.
- **FR-005**: System MUST return measurements ordered chronologically (oldest to newest).
- **FR-006**: System MUST return an empty result (not an error) when no measurements exist for an
  otherwise valid location/meter/time-range combination.
- **FR-007**: System MUST allow a user to list the locations they are associated with, and the
  registered meters at each, so they can select what to query.
- **FR-008**: System MUST return raw, per-reading measurement data (as ingested) — it does not
  perform server-side aggregation (e.g. hourly/daily totals or averages) in this feature. System
  MUST limit the number of measurements returned in a single request and provide a way to
  retrieve further results, to keep responses usable for very large time ranges or
  high-frequency data.
- **FR-009**: System MUST let a user retrieve their single latest measurement for a location or
  meter as a distinct, lightweight "current status" query, in addition to range queries.
- **FR-010**: Every query MUST be scoped to exactly one location at a time; the system does not
  provide a combined view across a user's multiple locations in this feature.

### Key Entities *(include if feature involves data)*

- **Measurement**: An existing entity representing one power reading (timestamp, power in watts)
  tied to a meter and a location; this feature only reads it, it does not change how
  measurements are created.
- **Location**: An existing entity representing a place a user owns/manages sensors at; a user
  may be associated with more than one location. Query access is scoped by this association.
- **Meter**: An existing entity representing a registered physical reading device at a location;
  a location may have more than one meter.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can retrieve their recent usage for a location in a single request, with a
  response returned in under 1 second under normal load.
- **SC-002**: A user can never retrieve, in any form, measurement data belonging to a location
  they are not associated with.
- **SC-003**: A user browsing a historical date range receives exactly the measurements within
  that range, with no missing or extra readings, 100% of the time.
- **SC-004**: A user with no data yet for a location receives a clear empty result rather than an
  error or timeout.

## Assumptions

- The existing measurement-ingestion feature continues to be the only way data enters the
  system; this feature is read-only and does not modify ingestion behavior.
- A user's association with a Location (already modeled today) is the sole basis for query
  authorization — there is no separate per-Location read/write permission distinction yet.
- Consumers of this feature are the not-yet-built frontend and manual/API testing; no specific
  client technology is assumed.
- Standard pagination (a bounded page size with a way to fetch subsequent pages) is an acceptable
  default approach for bounding large raw-reading result sets.
- Server-side aggregation (hourly/daily rollups) and cross-location combined views are explicitly
  out of scope for this feature and may become their own future features.
