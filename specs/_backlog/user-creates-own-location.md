# Backlog: Normal users create their own location

Status: idea, not specified yet. Written 2026-10-08 while planning the frontend feature
`no.sanddata.ams.frontend` -> `specs/001-location-meter-onboarding/`. Start with `speckit-specify` on a new
branch. **Blocks** the frontend's User Story 1 (a user sets up their own location and meter).

## Why this matters

A location belongs to the user who owns it. Today only administrators can create locations
(`POST /api/admin/locations`, `Roles(RoleNames.Admin)`) and only administrators can link a user to a
location (`PUT /api/users/{id}/locations/{locationId}`). A normal user therefore cannot set up their own
location, even though they can already register a meter at a location they are linked to
(`POST /api/meters`).

Also verified: `CreateLocationCommandHandler` does not link the creating admin, so the admin flow needs the
explicit link call. Do not change that.

## Proposed scope (to be refined in the spec)

`POST /api/locations`

- **Auth:** any signed-in user (JWT).
- **Body:** same as `POST /api/admin/locations`:
  `{ name, address, serialNumber, zone, hasNorgesPriceAgreement?, isActive? }`. Reuse `CreateLocationValidator`.
- **Behaviour:** create the location, generate its sensor key and link the caller to it in one transaction.
- **Success:** `201 { location, apiKey }` (same `CreatedLocationResponse` as the admin endpoint). The key is returned
  only in this response.
- **Errors:** `400` validation (incl. invalid zone), `401`, `409` serial number already used.

## Open questions

- Limit on the number of locations a non-admin can create?
- Should non-admins be able to rotate their own location's key (`POST .../api-key/rotate`)? Not needed for the frontend
  feature, but a lost key currently needs an administrator.
- Should the user's own locations be returned as `AdminLocationResponse` or the slimmer summary used by
  `GET /api/locations`? The frontend only needs the id, name, address, zone and meters.

## Related

- Frontend contract: `no.sanddata.ams.frontend/specs/001-location-meter-onboarding/contracts/backend-required.md`
- Public self-registration (a person creating their own account) is a further, separate feature; today only
  administrators can create users (`POST /api/users`).
