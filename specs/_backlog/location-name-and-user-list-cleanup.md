# Backlog: Unique location names and a single location list on users

Status: idea, not specified yet. Found by Roy during the manual test of 006 (T029), 2026-10-25. Discuss before
running `speckit-specify`.

## 1. Duplicate location names are allowed

Two locations can have the same name. Roy thinks that should not be possible.

- Spec 004 only requires the **serial number** to be unique (FR-003). Nothing covers the name.
- Open question: unique across all locations, or unique per user? A location can have several owners, so
  "per user" needs a rule for whose names count. Across all locations means two users cannot both have "Home".
- Decide how existing duplicates are handled (migration check, or only enforce on create and rename).

## 2. `GET /api/users` returns the same locations three times

Each user carries `locations` (names), `locationIds` (ids) and `locationAccess` (`locationId`, `name`, `role`).
`locationAccess` holds all the information of the other two.

- The old two fields stay on purpose: 006 FR-013 and `specs/006-location-roles-and-owner-edit/contracts/endpoints.md`
  say they remain so the web app keeps working until it switches to `locationAccess`.
- Plan: after 006 is deployed and the frontend uses `locationAccess` (006 T031), remove `locations` and
  `locationIds` from `UserListResponse` and the mapper, and update the contract docs.
- Alternative Roy may prefer: do it inside 006, together with the frontend change.
