# Quickstart: validating Location Roles and Owner Editing

## Prerequisites

- Local PostgreSQL running (`compose.db.yaml`), API started with `dotnet run --project src/api/api.csproj`
  (migrations run at startup when `RunMigrationsAtStartup` is true).
- A throwaway local database: links and test rows cannot be removed through the API.
- Accounts: an admin, an owner, and a third user who will be a viewer.

## Automated

```bash
dotnet build
dotnet test
dotnet format --verify-no-changes
```

Expected: all pass. New tests cover the link role rules (default owner, viewer, role change, last-owner
refusals), the owner location edit (only name, address and active flag change; `404` for viewers and
strangers), the meter comment edit and owner-only meter registration, the read models, and the owner-only limit.

## Manual, against a running API

1. **Migration**: before starting the new build note the existing links; after it starts, every link is an
   owner link (`select "UserId","LocationId","Role" from "UserLocation"`), and no link was removed.
2. **Link as viewer** (admin token): `PUT /api/users/{viewerId}/locations/{locationId}` with `{"role":"Viewer"}`
   returns 204; with no body it returns 204 and the role is `Owner`.
3. **Last owner**: `DELETE` the only owner's link and try to demote them: both return 409.
4. **Owner edit** (owner token): `PUT /api/locations/{id}` with `{"name":"New","address":"New","isActive":true}`
   returns 200 and `GET /api/locations` shows it. Send the same as the viewer: 404. Send extra fields
   (`serialNumber`, `zone`): ignored, and the stored values are unchanged.
5. **Deactivate** (owner): `isActive:false` returns 200; the owner still sees the location in
   `GET /api/locations` with `isActive:false`, the viewer no longer does, and a sensor reading for it is rejected.
6. **Meters**: owner `PUT /api/meters/{id}` with a comment returns 200; viewer gets 404; viewer
   `POST /api/meters` gets 404; owner `POST` works.
7. **Admin lists**: `GET /api/admin/locations` shows `users` with roles; `GET /api/users` shows `locationAccess`.
8. **Limit**: make a user viewer of four locations, then `POST /api/locations` as them: 201.
9. **Viewer reads**: the viewer fetches consumption and cost for the shared location and gets data.

See [data-model.md](./data-model.md) and [contracts/endpoints.md](./contracts/endpoints.md).
