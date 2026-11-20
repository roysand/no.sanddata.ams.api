# Research: Reminders Before Keys Expire

Phase 0 of [plan.md](plan.md). The Technical Context had no open questions. These are the decisions behind it.

## R1. How the OSO date is stored and sent

- **Decision**:
  - **Stored:** a `DateOnly` on `WaterHeater`, named `SubscriptionKeyExpiresOn`, in a PostgreSQL `date` column,
    `NOT NULL`. It is the last Oslo calendar day the key is valid.
  - **In requests:** `"yyyy-MM-dd"`, as a string. It is checked by the validator with the same pattern as the bill's
    `month`, and parsed in the endpoint. It is optional on connect and required on the date endpoint.
  - **In responses:** the same `"yyyy-MM-dd"`.
  - **Field name on the wire:** `keyExpiresOn`, not `subscriptionKeyExpiresOn`. An existing safety test
    (`WaterHeaterAdminHttpTests.Connect_AsAdmin_Is200WithoutTheKey`) fails if a response contains "subscriptionKey",
    and it should stay strict. The entity and the database column keep the full name.
- **Rationale**:
  - OSO keys are valid "until" a date, and Roy speaks of a day ("19 November 2027"), not an instant.
  - A date column can't drift with the clock changes.
  - The string-in-request pattern is how the API already takes dates (`GetMonthlyBillEndpoint`,
    `GetCapacityChargeEndpoint`), so the 400 messages look the same.
- **Alternatives**:
  - A UTC `DateTime` like `ApiKey.ExpiresAt`. It would invite off-by-one-day errors around midnight in Oslo, for a
    value the administrator types as a day.

## R2. Existing heaters

- **Decision**: The migration adds the column with a temporary default, then fills it with SQL:

  ```sql
  UPDATE public."WaterHeater"
  SET "SubscriptionKeyExpiresOn" = (("CreatedAt" AT TIME ZONE 'Europe/Oslo')::date + interval '1 year')::date
  ```

  Then it drops the default, so new rows always get the value from the code. `CreatedAt` is when the heater was first
  connected; `Replace` keeps it.
- **Rationale**:
  - FR-006 and SC-001: every heater has a date after deploy.
  - Hjemme was connected on 2026-11-19, so it gets 2027-11-19, which is the date Roy gave. Quickstart §2 checks it.
  - If a heater's key was replaced after connecting, the date may be too early. That is the safe side for a reminder,
    and the administrator can correct it with the date endpoint.
- **Alternatives**:
  - `UpdatedAt` + 1 year. It is wrong when only something else changed.
  - Nullable, with "unknown" shown. Then the reminder could silently miss the one heater that exists.

## R3. The window, days left and "expired"

- **Decision**: Everything is counted in whole Oslo calendar days, against `today` = the Oslo date of the clock
  (`TimeProvider`):
  - **Days left** = the key's last valid day − today. A key valid through today has 0 days left and is still valid. A
    key whose last day was yesterday has −1 and **has expired**.
  - **For the sensor key**, the last valid day is the Oslo date of `ExpiresAt − 1 tick`. A key that expires at an
    instant is valid up to that instant, so the day it expires on counts as its last day.
  - **The reminder window:** a key is listed when its days left are ≤ 30, which includes every expired key. A key with
    31 days left is not listed (SC-002).
  - **The response** carries:
    - `expiresOn`: the last valid day
    - `daysLeft`
    - `expired`: daysLeft < 0
    - `expiresAt` for the sensor key, which is the exact instant the API stops accepting it.
- **Rationale**:
  - One rule for both kinds keeps the banner's wording simple: "expires in 12 days", "expires today", "expired 3 days
    ago".
  - Whole days avoid a count that changes during the day.
- **Alternatives**:
  - Hours left. That is too precise for a reminder 30 days ahead.
  - A separate window per kind. Nothing asks for it (Principle VIII).

## R4. Which keys are listed

- **Decision**: `KeyReminders.Due(today, sensorKeys, heaterKeys, windowDays: 30)` is pure. It returns the listed keys,
  soonest first, then by location name.
  - **Sensor keys:**
    - from `GetAllWithKeyAsync`
    - only for active locations (`Location.IsActive`) with an active key (`ApiKey.IsActive`). A deactivated key is
      switched off on purpose, and an inactive location is refused anyway (spec US2 scenarios 4 and 5).
  - **Heater keys:**
    - from `GetForActiveLocationsAsync`, so for active locations only
    - the location name is taken from the location list.
- **Rationale**: These two existing reads cover the whole list, so there is no new repository method (Principle VIII).
  - The location list is already loaded whole for the Admin page; the number of locations is small (a household's).
  - `GetForActiveLocationsAsync` already exists for the sampler.
- **Alternatives**:
  - One SQL query over both tables. It would be faster for thousands of locations, which this system doesn't have.
  - Reading each heater's settings: N queries.

## R5. The date endpoint

- **Decision**:
  - **Request:** `PUT /api/admin/locations/{locationId}/water-heater/key-expiry` with `{ "keyExpiresOn":
    "yyyy-MM-dd" }`.
  - **Answer:** it returns the updated `WaterHeaterSettingsResponse`.
  - **Refusals:** `404 WaterHeater.NotConnected` when the location has no heater, as the settings endpoint does today.
    `400 WaterHeater.KeyExpiryOutOfRange` for a date that isn't after today or is more than five years ahead.
  - **OSO is not called:** the key isn't changing.
- **Rationale**: FR-004. Correcting a typo in the date, or extending it after renewing the subscription with the same
  key, must not need the key again. The key is shown only in OSO's portal.
- **Alternatives**:
  - Re-sending the whole connect request: that needs the key, which the administrator may not have at hand.
  - A PATCH of the settings: the API has no PATCH endpoints, and a dedicated route is clearer.

## R6. Logging and errors

- **Decision**:
  - **Events:** 1806 `WaterHeaterKeyExpiryChanged` (location id, acting user id, the new date) and 1406
    `KeyRemindersListed` (acting user id, count). Both are in their slices' existing ranges.
  - **Connect:** the existing connect events stay as they are. Connecting logs no date, to keep it unchanged.
  - **Error:** `WaterHeaterErrors.KeyExpiryOutOfRange`, a validation error.
- **Rationale**:
  - Principle VI: the existing ranges have room, so the EventId table in `DevelopmentGuide.md` needs no new range.
  - That also avoids a clash with the uncommitted change to that file on `feature/013-exception-handling-middleware`.
- **Alternatives**: A new 19xx range for reminders. That isn't needed for one event.

## R7. What the reminder list does not do

- **Decision**:
  - No "seen" or snooze state.
  - No background job, e-mail or push.
  - The list is computed on each request.
- **Rationale**: The spec's edge cases and Assumptions: the reminder stays until the key is replaced. The frontend asks
  once when an administrator opens the app.
- **Alternatives**:
  - A daily job that stores reminders. That is nothing to show for the cost (Principle VIII).
