# Feature Specification: Unique Location Names

**Feature Branch**: `feature/009-unique-location-names`

**Created**: 2026-11-18

**Status**: Draft

**Input**: Backlog note `specs/_backlog/unique-location-names.md` (found during the manual test of spec 006 on
2026-10-25, and again on 2026-11-18, when pricing data was entered on the wrong one of two locations with similar
names). User description: "A location name must be unique across all locations in the system (not per user). Spec 004
only required the serial number to be unique. Cover creating a location (the user setup flow, the owner flow and the
admin 'add location on behalf of a user' flow), renaming it (owner edit and admin edit), the error the API returns, and
what happens to existing duplicates in the database when the rule is introduced."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A new location cannot take a name that is already used (Priority: P1)

A user setting up their first location, an owner adding another one, or an administrator adding a location on behalf
of a user, picks a name. If any location in the system already has that name, the API refuses it and says the name is
taken, so the person picks another one. Two locations can then never be confused by name in the admin pages, the
location picker or the logs.

**Why this priority**: It stops new duplicates at the source, which is what the backlog note asks for.

**Independent Test**: With a location named "Hytta", try to create another location named "Hytta" (and " hytta ")
through each of the three create flows; each is refused with the same error, and nothing is created.

**Acceptance Scenarios**:

1. **Given** a location named "Hytta", **When** a user creates a location named "Hytta" in the setup flow, **Then**
   the API refuses with a conflict, the error code `Location.NameExists` and the message "Another location already
   uses this name", and no location, meter or key is created.
2. **Given** the same, **When** an owner creates another location or an administrator creates one on behalf of a
   user with that name, **Then** the same refusal.
3. **Given** a location named "Hytta", **When** the new name differs only in letter case or in spaces before or after
   it ("hytta", " Hytta "), **Then** it is refused as the same name.
4. **Given** a location named "Hytta", **When** the new name is "Hytta 2" or "Hytta Sjusjøen", **Then** it is
   accepted.
5. **Given** the name is taken by an inactive location, **Then** it is still refused: the rule covers every location.
6. **Given** both the name and the serial number are taken, **Then** both conflicts are reported in the one answer,
   so the person can fix both at once.

---

### User Story 2 - A location cannot be renamed to a name that is already used (Priority: P1)

An owner or an administrator renames a location. If another location already has the new name, the rename is
refused with the same error. Saving a location without changing its name, or changing only the letter case of its own
name, always works.

**Why this priority**: Without it a duplicate can still be made by renaming, so Story 1 alone would not hold.

**Independent Test**: With "Hytta" and "Hjemme", rename "Hjemme" to "Hytta" as the owner and as an administrator;
both are refused. Rename "Hytta" to "HYTTA"; it is accepted.

**Acceptance Scenarios**:

1. **Given** locations "Hytta" and "Hjemme", **When** the owner of "Hjemme" renames it to "Hytta", **Then** the API
   refuses with `Location.NameExists` and the location keeps its name.
2. **Given** the same, **When** an administrator renames it through the admin edit, **Then** the same refusal.
3. **Given** "Hytta", **When** it is saved with its own name, or renamed to "HYTTA", **Then** it is accepted (a
   location never conflicts with itself).
4. **Given** the owner is a viewer-only user of the other location, or cannot see it at all, **Then** the message
   still only says the name is taken; it does not reveal who owns the other location or where it is.

---

### User Story 3 - Existing duplicates are handled when the rule is introduced (Priority: P1)

The rule is introduced on a database where duplicates may already exist (the backlog note was raised because two
locations shared a name). Nothing is renamed automatically: before the change is deployed, an administrator lists the
duplicates and renames them by hand, choosing the new names (decided by Roy on 2026-11-18). The database change that
enforces the rule then refuses to apply while any duplicate remains, and says which names are duplicated.

Because the production API applies database changes when it starts, a deploy with duplicates left would stop the API
from starting. The check is therefore a step before merging, not after.

**Why this priority**: If this is not decided, the deploy either fails or the rule does not hold for the data that
caused the problem.

**Independent Test**: On a copy of the database with two locations named "Hytta", run the duplicate check (it lists
"Hytta" with both locations), apply the change (it refuses and names "Hytta"), rename one location, and apply it again
(it succeeds).

**Acceptance Scenarios**:

1. **Given** a database with no duplicate names, **When** the change is deployed, **Then** nothing is renamed and the
   rule applies from then on.
2. **Given** a database with duplicates, **When** the duplicate check is run, **Then** it lists each duplicated name
   with the locations that share it (id, name, owner), so the administrator can rename them through the existing edit.
3. **Given** a database where duplicates remain, **When** the change is applied, **Then** it refuses, names the
   duplicated names, and leaves the database as it was.
4. **Given** the duplicates have been renamed, **When** the change is applied, **Then** it succeeds and no two
   locations share a name.
5. **Given** the change is about to be merged, **Then** the pull request states that the duplicate check must return
   nothing on the production database first, and how to run it.

### Edge Cases

- **Two creates at the same time with the same name.** Only one succeeds; the other gets the same `Location.NameExists`
  answer, not a server error.
- **Names that differ only inside** ("Hytta  1" with two spaces and "Hytta 1"): treated as different names; only
  letter case and leading or trailing spaces are ignored.
- **Letters outside a–z** (æ, ø, å): compared without regard to case, so "Ørnes" and "ørnes" are the same name.
- **Deleted locations**: locations are deactivated, not deleted; a deactivated location keeps its name and blocks it.
- **Validation order**: an empty or too long name is refused by the existing checks before the uniqueness check.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST refuse to create a location whose name is already used by any other location, active or
  not, in all three create flows (user setup, owner creating another location, administrator on behalf of a user).
- **FR-002**: The system MUST refuse to rename a location to a name already used by another location, in both the owner
  edit and the administrator edit.
- **FR-003**: Names MUST be compared after removing spaces before and after them, and without regard to letter case
  (including æ, ø and å). The name is stored as typed, apart from the spaces before and after it.
- **FR-004**: A refused create or rename MUST answer with a conflict, the error code `Location.NameExists` and the
  message "Another location already uses this name", attached to the name field, and MUST change nothing.
- **FR-005**: The refusal MUST NOT reveal anything about the other location (its owner, address or id).
- **FR-006**: When the name and the serial number are both taken, the answer MUST report both.
- **FR-007**: The rule MUST hold even when two requests with the same name arrive at the same time.
- **FR-008**: Existing duplicates MUST NOT be renamed automatically. A duplicate check MUST list every duplicated name
  with the locations that share it, and the database change that enforces the rule MUST refuse to apply, naming the
  duplicated names and changing nothing, while any duplicate remains.
- **FR-009**: Refused attempts MUST be logged with a stable reason code (`NameExists`) and without the name itself.
- **FR-010**: The duplicate check and the requirement to run it on production before merging MUST be documented with
  the change (in the pull request and the deployment notes).

### Key Entities

- **Location**: its name becomes unique across all locations (compared without case and outer spaces), in addition to
  the serial number, which already is.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After the deploy, no two locations in the system share a name (compared without case and outer spaces).
- **SC-002**: 100% of attempts to create or rename a location to a used name, through any of the five flows, are
  refused with the same error and change nothing.
- **SC-003**: A person who picks a used name sees why on the name field and can save with a different name on the
  next try.
- **SC-004**: Saving a location without changing its name never fails because of this rule.

## Assumptions

- "Across all locations" means every location in the system, whoever owns it and whether it is active (decided by Roy
  on 2026-11-18).
- Letter case and outer spaces are ignored because names that differ only there are the ones people confuse; anything
  else (a number, another word) makes a different name.
- The frontend needs no change for the rule to work: its location forms already show the API's message. Showing it on
  the name field (as for the serial number) is a small frontend follow-up.
- Similar but different names ("Hytta" and "Hytta 2") are allowed. Telling them apart in the admin pages is handled in
  the frontend (Location pricing shows the address and the owners, frontend PR #22).
- The production database can be checked for duplicates before the deploy, and renaming uses the existing owner or
  administrator edit; no new rename tool is needed.
- A deploy with duplicates left would stop the API from starting (the database change runs at startup); this is
  accepted because the check before merging prevents it, and the fix is to rename and restart.
