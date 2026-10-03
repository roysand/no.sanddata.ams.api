# Feature Specification: User Roles and Endpoint Security

**Feature Branch**: `feature/003-user-roles-and-endpoint-security`

**Created**: 2026-10-03

**Status**: Draft

**Input**: User description: "User roles and endpoint security (feature 003). Every user-management endpoint is currently open to anonymous callers and no roles exist, so anyone who can reach the API can list or delete users or change passwords. Introduce two roles, Admin and User, restrict user management accordingly, make sure a first Admin exists, and make sure the sign-in token carries the role. Must be fixed before the API is deployed to the internet." (Background: `specs/_backlog/003-user-roles-and-endpoint-security.md`)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Close the anonymous user-management endpoints (Priority: P1)

Today anyone who can reach the system can list every user, create or delete users, and change anyone's password without signing in. After this feature, every user-management action requires a signed-in caller and the right to perform it. Only sign-in and token refresh stay open to anonymous callers.

**Why this priority**: This is an open security hole and the reason for the feature. Nothing can be deployed to the internet until it is closed.

**Independent Test**: Call every user-management action without signing in and confirm each is refused; then call each as a signed-in ordinary user and as an Admin and confirm the outcomes in the scenarios below.

**Acceptance Scenarios**:

1. **Given** a caller who is not signed in, **When** they attempt any user-management action (list, view, create, update, delete, change password), **Then** the request is refused as unauthenticated and nothing changes.
2. **Given** a signed-in ordinary user, **When** they list all users, create a user, or delete a user, **Then** the request is refused as forbidden and nothing changes.
3. **Given** a signed-in ordinary user, **When** they view, update or change the password of **their own** account, **Then** the action succeeds.
4. **Given** a signed-in ordinary user, **When** they try to view, update or change the password of **another** user's account, **Then** the request is refused and nothing changes.
5. **Given** a signed-in Admin, **When** they list, view, create, update, delete or change the password of any user, **Then** the action succeeds.
6. **Given** anyone, **When** they sign in or refresh a token, **Then** this still works without prior authentication.

---

### User Story 2 - A first Admin exists, and roles carry into sign-in (Priority: P1)

The system has exactly two roles, Admin and User, available from the moment it is set up. The owner (roy@sanddata.no) is an Admin without anyone having to edit the database by hand. A user's roles are reflected in their sign-in so role-restricted actions work immediately after signing in.

**Why this priority**: Without roles and a first Admin, closing the endpoints (Story 1) would lock everyone out of user management, including the owner.

**Independent Test**: On a freshly set-up system, sign in as the owner and confirm Admin rights; sign in as any other existing user and confirm ordinary-user rights.

**Acceptance Scenarios**:

1. **Given** a newly set-up or upgraded system, **When** it starts, **Then** the roles "Admin" and "User" exist, exactly once each.
2. **Given** the owner's account exists, **When** the system is set up or upgraded, **Then** the owner holds the Admin role.
3. **Given** existing users who held no role, **When** the system is upgraded, **Then** each of them holds the User role, so they keep working as ordinary users.
4. **Given** a user who holds the Admin role, **When** they sign in or refresh their token, **Then** the issued token identifies them as an Admin.
5. **Given** a user whose roles changed since they last signed in, **When** they refresh or sign in again, **Then** the new token reflects their current roles.

---

### User Story 3 - Admins manage who is an Admin (Priority: P2)

An Admin can create users and decide their role. Newly created users are ordinary users by default. An Admin can promote a user to Admin or demote one back, but the system never allows the last remaining Admin to be removed or demoted.

**Why this priority**: Without this, the only way to add a second Admin is editing the database. It is important but not a security hole by itself.

**Independent Test**: As Admin, create a user, confirm they are an ordinary user, promote them, confirm they now have Admin rights, demote them, and try to demote or delete the last Admin.

**Acceptance Scenarios**:

1. **Given** an Admin creates a user, **When** no role is specified, **Then** the new user holds the User role.
2. **Given** an Admin, **When** they grant the Admin role to another user, **Then** that user has Admin rights from their next sign-in or token refresh.
3. **Given** an Admin, **When** they remove the Admin role from another user, **Then** that user loses Admin rights from their next sign-in or token refresh.
4. **Given** exactly one Admin exists, **When** anyone tries to demote or delete that Admin, **Then** the request is refused and the Admin remains.
5. **Given** a signed-in ordinary user, **When** they try to change anyone's roles (including their own), **Then** the request is refused.

---

### User Story 4 - Meter endpoints respect location membership (Priority: P2)

Reading or registering a meter is only allowed for users who belong to the meter's location, consistent with how measurements and cost data are already protected. Today any signed-in user can read any meter and register meters at any location.

**Why this priority**: It is the same class of problem (a signed-in user reaching data that is not theirs), found while investigating, but exposes less than the user-management hole.

**Independent Test**: As a user who is not linked to a location, try to read a meter of that location and to register a meter there; as a linked user, do both.

**Acceptance Scenarios**:

1. **Given** a signed-in user linked to a location, **When** they read a meter at that location or register one there, **Then** the action succeeds.
2. **Given** a signed-in user not linked to a location, **When** they read a meter at that location or register one there, **Then** the request is refused as "not found", the same way other location data is, and nothing changes.
3. **Given** a caller who is not signed in, **When** they use the meter endpoints, **Then** the request is refused as unauthenticated.

---

### User Story 5 - Admins link users to locations (Priority: P3)

An Admin can link a user to a location and remove the link, so that user can see that location's data. Today this can only be done by editing the database directly.

**Why this priority**: A convenience and the natural counterpart to roles, but not needed to secure the system. It is included here because without it a newly created user cannot see any data.

**Independent Test**: As Admin, link a user to a location and confirm they can now read that location's data; remove the link and confirm they can no longer.

**Acceptance Scenarios**:

1. **Given** an Admin, **When** they link an existing user to an existing location, **Then** that user can read the location's data from their next request.
2. **Given** an Admin, **When** they remove a link, **Then** the user can no longer read that location's data.
3. **Given** a signed-in ordinary user, **When** they try to link or unlink anyone, **Then** the request is refused.

---

### Edge Cases

- A user is demoted or deleted while holding a still-valid sign-in token: the change takes effect at their next sign-in or token refresh (tokens last up to 6 hours). Immediate revocation is not required (decision recorded in Assumptions).
- An Admin changes their own password or edits their own account: allowed, as for any user viewing their own account.
- An ordinary user passes another user's identifier to a "my own account" action: refused, and the response must not reveal whether that user exists.
- An Admin tries to remove their own Admin role while other Admins exist: allowed. If they are the last Admin: refused.
- Role names are matched exactly; a user cannot be given a role that does not exist.
- The owner's account does not exist yet when the system is set up (for example, a fresh environment): the roles still exist and no Admin is granted. The first time the account named by the owner-email setting exists while no Admin exists, it is promoted automatically. Once any Admin exists, this setting has no effect.
- The owner-email setting names an account that is not the owner's, or is changed later: it has no effect while at least one Admin exists, so it can never be used to take over a running system.
- Sensor/API-key ingestion is unaffected by roles and keeps working exactly as before.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST refuse every user-management action (list, view, create, update, delete, change password) from callers who are not signed in.
- **FR-002**: The system MUST allow only Admins to list all users, create users and delete users.
- **FR-003**: The system MUST let a signed-in user view, update and change the password of their own account, and MUST refuse these actions on other users' accounts unless the caller is an Admin.
- **FR-004**: The system MUST let an Admin view, update and change the password of any user's account.
- **FR-005**: The system MUST provide exactly two roles, "Admin" and "User", present on every new and upgraded system, with no duplicates.
- **FR-006**: The system MUST ensure that the owner holds the Admin role without manual database editing, and that every other existing user holds the User role. The owner is identified by an owner-email setting that defaults to roy@sanddata.no. The system MUST promote that account automatically when it exists and no Admin exists yet, and MUST NOT act on the setting once any Admin exists.
- **FR-006a**: An Admin MUST also be able to use the system as an ordinary user: Admin rights are a superset of User rights, and an Admin can be linked to locations and see their data like any other user.
- **FR-007**: Newly created users MUST hold the User role by default.
- **FR-008**: The system MUST let an Admin grant and remove the Admin role for other users, and MUST refuse any request from a non-Admin to change roles.
- **FR-009**: The system MUST NOT allow the last remaining Admin to be demoted or deleted.
- **FR-010**: The user's current roles MUST be reflected in the sign-in and token-refresh results, so role-restricted actions work immediately after signing in.
- **FR-011**: Sign-in and token refresh MUST remain available without prior authentication; sensor ingestion by API key MUST be unchanged.
- **FR-012**: The meter-read and meter-register actions MUST require a signed-in user who is linked to the meter's location, and MUST report "not found" otherwise.
- **FR-013**: Refusals MUST distinguish "not signed in" (unauthenticated) from "signed in but not allowed" (forbidden) for user-management actions, and MUST NOT reveal whether another user's account exists to a caller who may not see it.
- **FR-014**: The system MUST record a structured log entry for security-relevant events: refused user-management attempts, role changes, and user deletions, without logging passwords, tokens or other secrets.
- **FR-015**: An Admin MUST be able to link and unlink a user and a location, and ordinary users MUST NOT. This is the only way a newly created user gains access to a location's data.

### Key Entities

- **User**: A person who signs in. Has zero or more roles and is linked to zero or more locations.
- **Role**: A named permission level. Exactly two exist: Admin (can manage users and roles) and User (can use the system for their own account and their own locations).
- **User–Role assignment**: Which roles a user holds, with when it was granted.
- **User–Location link**: Which locations a user may see data for. Already exists; this feature adds a way to manage it.
- **Meter / Location**: Existing entities; this feature only changes who may reach a meter through its location.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of user-management actions are refused for callers who are not signed in, verified for every action.
- **SC-002**: 100% of Admin-only actions (list users, create user, delete user, change roles) are refused for ordinary users, verified for every action.
- **SC-003**: An ordinary user can never read or change another user's account; verified with at least one attempt per account action.
- **SC-004**: After setting up or upgrading the system, the owner can use Admin actions with no manual database edit, and every previously existing user can still sign in and reach the same location data as before.
- **SC-005**: The system never reaches a state with zero Admins through any request it accepts.
- **SC-006**: A signed-in user who is not linked to a location cannot read or register meters at that location (0 successful attempts in tests).
- **SC-007**: After the feature is complete, a review of all endpoints finds no endpoint that is open to anonymous callers other than sign-in, token refresh and health/documentation pages.

## Assumptions

- Roles are system-wide, not per location: an Admin can manage users everywhere. Admin rights do **not** automatically grant access to other users' locations' measurement or cost data; that still requires a location link (kept minimal; can be revisited later).
- Public self-registration is out of scope: only Admins create users. Self-signup can be a later feature.
- The sign-in token format already has a place for the user's roles, but in the current system a user's roles are never actually loaded when signing in or refreshing, so tokens carry none. Making roles reach the token is therefore part of this feature (FR-010); the token format itself does not change.
- A role change or deletion takes effect at the user's next sign-in or token refresh (tokens last up to 6 hours); immediate revocation is not required. (Decision, 2026-10-03.)
- This is a personal/hobby system, not a product to be sold: a single owner acts as the system's default Admin, creates every other user and links each new user to their locations. Self-service, multi-tenant or customer-facing administration is out of scope. (Decision, 2026-10-03.)
- The owner's account (roy@sanddata.no) already exists in the current environments.
- No role management beyond the two fixed roles (no creating, renaming or deleting roles) is needed now; the earlier unmerged roles CRUD attempt is reference material only and is not merged.
- Passwords continue to follow the existing password rules; this feature does not change them.
- Out of scope: password reset by email, multi-factor sign-in, account lockout, audit-log screens, and any frontend changes.
