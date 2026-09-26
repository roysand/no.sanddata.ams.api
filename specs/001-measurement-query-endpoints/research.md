# Phase 0 Research: Measurement Query Endpoints

All ambiguities flagged in the spec (`[NEEDS CLARIFICATION]`) were already resolved
interactively before the spec was finalized (raw readings only, latest-reading query in scope,
one location per query). This document covers the remaining *implementation-level* decisions
needed before design (Phase 1), each with rationale and the alternative considered.

## 1. Pagination style

**Decision**: Offset-based pagination (`page`, `pageSize`), default `pageSize` 500, max 2000.

**Rationale**: At this project's scale (one household, single-digit number of locations, one
reading roughly per minute per meter), even a full year of data for one meter is on the order of
500K rows — offset pagination is simple, well-understood, and sufficient. A 500-row default page
covers a full day of minute-level readings in a single response, keeping typical "recent usage"
and "browse a week" queries to one or two pages.

**Alternatives considered**: Cursor/keyset pagination (better for very large, high-churn
datasets) — rejected as unnecessary complexity for this scale; nothing here is deleted or
reordered mid-query, so offset pagination's usual pitfalls don't apply. Matches the
constitution's Minimal-Footprint-Changes principle.

## 2. Default "recent" window (FR-004)

**Decision**: When no `from`/`to` is supplied, default to the last 24 hours (matches the spec's
own User Story 1 acceptance scenario). If only `from` is supplied, `to` defaults to now. If only
`to` is supplied, `from` defaults to `to` minus 24 hours.

**Rationale**: Directly stated in the spec's acceptance criteria; extending the same 24h default
to the partial-range cases keeps behavior predictable without inventing a new constant.

## 3. Authorization pattern: hide existence of locations that aren't the user's (FR-001)

**Decision**: A `locationId` that either doesn't exist or isn't associated with the current user
returns the same `404 Location.NotFound` — the handler checks association via a repository call
before running any query, and returns the same error either way.

**Rationale**: Directly required by FR-001 ("without revealing whether the location exists").
This follows the existing authentication pattern in the codebase (`IHttpContextAccessor` /
`ClaimTypes.NameIdentifier` in handlers, see `DevelopmentGuide.md`).

**Alternatives considered**: `403 Forbidden` for "not yours" vs `404` for "doesn't exist" —
rejected because that distinction itself leaks whether a given ID exists, which FR-001
explicitly forbids.

## 4. Response shape when no "latest" measurement exists yet (FR-009, FR-006)

**Decision**: `GET /api/measurements/latest` returns `204 No Content` when the location/meter has
no measurements yet, and `200 OK` with the reading when one exists.

**Rationale**: Matches FR-006's "empty result, not an error" requirement without needing a
nullable wrapper object in the 200 response; `204` is the standard REST idiom for "valid request,
nothing to return."

## 5. Testing approach

**Decision**: No new automated test project is introduced by this feature. Verification is
manual end-to-end (via `curl` / the Scalar UI), consistent with how the existing ingestion and
meter-registration features were verified.

**Rationale**: No test project exists anywhere in this solution yet (confirmed: no xUnit/NUnit/
MSTest package reference in the repo). Introducing a test framework is a cross-cutting decision
bigger than one feature and out of scope here; matches Minimal-Footprint-Changes.

## 6. Logging EventId range

**Decision**: Claim `1300-1399` for the Measurements feature (query events) and `1400-1499` for
the new Locations feature, per the constitution's requirement to keep `CLAUDE.md`/
`DevelopmentGuide.md`'s EventId table in sync when a new range is claimed. This is tracked as an
explicit implementation task (updating that table is part of "Done," not optional cleanup).

**Rationale**: Neither Measurements nor Meters/Locations currently has a `LogMessages` class or
claimed range; the next free ranges after the existing table (`1000-1099` Users, `1100-1199`
Auth, `1200-1299` Test, `2000-2099` Infra/Logging) are `1300+`.
