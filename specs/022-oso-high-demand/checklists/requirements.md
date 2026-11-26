# Specification Quality Checklist: Turn the Water Heater's High Demand On and Off

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-11-26
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

- OSO is named, as in spec 011: it's the external service the feature exists for, not an implementation choice. The
  exact calls, URLs and status fields stay out of the spec and go into the plan's research.
- SC-001 and SC-002 ask for 3 tries, not 5, since each High Demand costs some power (analysis C1, 2026-11-26).
- FR-009 is a hand test against the real heater, run by Roy. Its outcome decides whether the frontend shows the
  switch.
- The backlog note `specs/_backlog/oso-high-demand.md` was never committed. Its details (the calls, the hand test of
  2026-11-26, the open questions) are now in research.md (R1, R8), and the note is deleted.
