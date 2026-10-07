# Quickstart: validating location management and hashed keys

Prerequisites: dev database with the existing `Home` location and its sensor key; an Admin to log in with; the API
running from this branch (Development, so the dev frontend origin is allowed); for the UI parts, the frontend
dev server. Contracts: [contracts/endpoints.md](./contracts/endpoints.md); data rules: [data-model.md](./data-model.md).

## 0. The existing sensor keeps working (User Story 2) - do this FIRST, before and after the migration

1. **Before** applying the migration, note the current key: `select "Key" from "ApiKey"` (dev only) and confirm it
   authenticates: `POST /api/measurements` with `X-API-Key: <key>` and an unregistered device id returns **404**
   `Meter.NotRegistered` (authenticated, but unknown reader), not 401.
2. Apply the migration. `select "KeyHash","KeyHint" from "ApiKey"` shows a 64-character hash and 4 characters; the
   `Key` column no longer exists.
3. The same request with the same key still returns **404** (not 401). A wrong key returns **401**.
4. Confirm the running forwarder's next batch is stored (count of recent `Measurement` rows grows).

## 1. Create a location (User Story 1)

1. Log in as Admin, open **Locations**, click **Add location**: name "Cabin", address, serial number, zone NO1.
2. A dialog shows the key **once** with a copy button; it cannot be closed without confirming the key was copied.
3. Close it and reopen the location: no key anywhere, only description, hint, expiry and "Active".
4. Register a reader for the cabin (device id), then send a reading with the new key: accepted and stored under Cabin.
5. Repeat the create with the same serial number: refused with a message. A zone outside NO1-NO5 is refused.
6. As a regular user, try the admin endpoints (403) and confirm they see only their own locations.

## 2. Rotate and deactivate keys (User Story 3)

1. Rotate the Cabin key: the dialog warns, then shows a new key once.
2. The old key now returns 401 immediately; the new key works.
3. Deactivate the key: readings return 401. Rotate again to restore service.
4. Set an expiry in the past in the database for a throwaway location (dev only): the key is rejected and the
   Locations page shows it as **Expired**.

## 3. Edit, list all, deactivate a location (User Story 4)

1. The Locations page lists **all** locations, including ones the Admin is not linked to; the Users page offers all of
   them as checkboxes.
2. Edit the cabin's address: saved; key unchanged. Change its zone: a warning about recalculated past costs appears first.
3. Deactivate the cabin: readings with its (valid) key return 401; a user linked to it no longer sees it and gets
   404 on its readings/costs; the Admin still sees it, marked inactive. Reactivate: everything returns.

## 4. Readers (User Story 5)

Register a reader from the Locations page for a location the Admin is not linked to (works); register the same
device id twice (409); a linked regular user can still register at their own active location, and gets 404 for
others.

## 5. Secrets stay out of logs (SC-007)

Run the steps above with request logging set to the most verbose options (`Headers` in `AttributesToLog`,
`LogResponseBody` true), then search the log output for the full key: **0 matches**. Also search for the new key's
hash: 0 matches.

## 6. Final checks

`dotnet build`, `dotnet test`, `dotnet format` on touched files, frontend `tsc`, lint and build.
