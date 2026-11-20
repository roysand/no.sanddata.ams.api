# Quickstart: Check the Key Expiry Reminders

## Prerequisites

- **Build:** the .NET 10 SDK.
- **Database:** the local TimescaleDB from `compose.db.yaml`, with the migrations applied.
- **Sign-in:** an Admin token, and a token for a user without the Admin role.

## 1. Automated checks

```sh
dotnet build && dotnet test
dotnet format --verify-no-changes --include <the changed files>
```

The repo-wide format check already fails on `main`, because of line endings in files this feature doesn't touch.

**Expected:** everything passes. The new tests cover:
- **`KeyRemindersTests`:**
  - the window edges: 31, 30, 0 and −1 days
  - a sensor key's last day: an instant during the day, and one at exactly Oslo midnight
  - a clock-change month
  - left out: deactivated keys and inactive locations
  - the order
- **Connect handler:**
  - the date is stored
  - absent gives today + 1 year
  - today, yesterday and more than five years ahead are refused before OSO is called
  - replacing sets the new date
- **Date handler:**
  - changes only the date
  - `404` without a heater
  - the range check
- **Reminder list:** the handler maps both kinds, with the location names.
- **HTTP:** the JSON shape, `401` and `403`.

## 2. The migration (manual, FR-006, SC-001)

1. On a copy of the production database, or the local one with a heater connected, apply the migration.
2. Run `SELECT "LocationId", "CreatedAt", "SubscriptionKeyExpiresOn" FROM public."WaterHeater";`. Each date is the Oslo
   date of `CreatedAt` + 1 year. For Hjemme in production, it must be **2027-11-19**.
3. Check that the column is `NOT NULL`, and has no default left.

## 3. The endpoints (manual)

1. **Settings:** `GET /api/admin/locations/{hjemme}/water-heater` includes `"keyExpiresOn": "2027-11-19"`.
2. **Change the date:** `PUT …/water-heater/key-expiry` with `{"keyExpiresOn":"2027-12-01"}` returns `200`
   with the new date. The heater's state (`GET /api/locations/{hjemme}/water-heater`) still reads, so the key is
   unchanged. Set it back to 2027-11-19.
3. **Out of range:** the same request with today's date, or with a date six years ahead, gives `400
   WaterHeater.KeyExpiryOutOfRange`.
4. **The list:** `GET /api/admin/key-reminders` gives `items: []` today, since nothing is due within 30 days.
   1. Temporarily set Hjemme's date to 10 days ahead. It is listed with `daysLeft: 10`.
   2. Set it to tomorrow: `daysLeft: 1`.
   3. Set it back.
5. **A sensor key close to expiry:** on the local database only, run `UPDATE public."ApiKey" SET "ExpiresAt" = now() +
   interval '5 days' WHERE …`.
   1. The location is listed as `SensorKey` with `daysLeft` 5 (or 4, close to Oslo midnight).
   2. Rotate the key (`POST /api/admin/locations/{id}/api-key/rotate`). It leaves the list (SC-004).
6. **Non-administrator:** with a user who isn't an administrator, both new endpoints give `403`.
7. **Logs:** neither new endpoint writes a key, a hash or a device id to the log (SC-005).
