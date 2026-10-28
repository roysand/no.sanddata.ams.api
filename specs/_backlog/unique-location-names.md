# Backlog: Unique location names

Status: idea, not specified yet. Found by Roy during the manual test of 006 (T029), 2026-10-25. Roy decided on
2026-10-28 to do this later. Discuss before running `speckit-specify`.

(The second finding of that test, the three location lists on `GET /api/users`, was fixed on 2026-10-28.)

## Duplicate location names are allowed

Two locations can have the same name. Roy thinks that should not be possible.

- Spec 004 only requires the **serial number** to be unique (FR-003). Nothing covers the name.
- Open question: unique across all locations, or unique per user? A location can have several owners, so
  "per user" needs a rule for whose names count. Across all locations means two users cannot both have "Home".
- Decide how existing duplicates are handled (migration check, or only enforce on create and rename).
