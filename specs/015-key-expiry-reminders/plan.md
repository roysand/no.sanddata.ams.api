# Implementation Plan: Reminders Before Keys Expire

**Branch**: `feature/015-key-expiry-reminders` | **Date**: 2026-11-20 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/015-key-expiry-reminders/spec.md`

## Summary

1. **The OSO key's expiry date (US1).**
   - **Stored:** `WaterHeater` gains `SubscriptionKeyExpiresOn`, a `DateOnly` stored as a PostgreSQL `date`.
   - **Migration:** the migration fills existing rows with "the Oslo date of `CreatedAt` + 1 year", so Hjemme gets
     2027-11-19 (research R2).
   - **Connect:** the connect request (`PUT /api/admin/locations/{locationId}/water-heater`) gains an optional
     `keyExpiresOn`. Without it, the default is one year from today.
   - **Change the date alone:** a new `PUT …/water-heater/key-expiry` changes only the date.
   - **Read:** `WaterHeaterSettingsResponse` gains the date.
2. **The reminder list (US2).**
   - **Endpoint:** a new `GET /api/admin/key-reminders` in the Locations slice.
   - **Data:** it reads the locations with their sensor keys (the existing `ILocationRepository.GetAllWithKeyAsync`)
     and the heaters of active locations (the existing `IWaterHeaterRepository.GetForActiveLocationsAsync`). It is two
     queries and needs no new repository method.
   - **Rules:** the pure `KeyReminders.Due(...)` picks the keys that expire within 30 days or have expired, works out
     the whole Oslo days left, and sorts the soonest first (research R3, R4).
3. **Nothing changes for existing clients.** A connect request without the date still works, and every existing field
   stays as it is (FR-010).

## Technical Context

**Language/Version**: C# 14 / .NET 10, ASP.NET Core 10

**Primary Dependencies**: FastEndpoints, EF Core (Npgsql), `Cqrs.SourceGenerator`. **No new packages.**

**Storage**: PostgreSQL. One migration adds the `WaterHeater."SubscriptionKeyExpiresOn" date NOT NULL` column and fills
it for existing rows. There is no new table.

**Testing**: xUnit + NSubstitute in `tests/Features.Tests`.
- `KeyRemindersTests`, pure:
  - the 30-day window and its edges
  - expired keys, with negative days
  - Oslo midnight and a clock-change month
  - deactivated sensor keys and inactive locations left out
- The connect handler tests gain cases for:
  - the date is stored
  - the default one year
  - a date in the past or today, or more than five years ahead, is refused
  - replacing the heater sets the new date
- New handler tests:
  - changing the date alone
  - the reminder list
- HTTP tests:
  - the JSON shape of the list
  - `401` and `403`
- The migration's SQL is checked by hand on the local database (quickstart §2).

**Target Platform**: a Linux container on a Hetzner VPS (`compose.prod.yaml`), as before.

**Project Type**: web-service (API). The frontend follows in its own spec.

**Performance Goals**: the reminder list is two reads, at about the speed of the Admin location list (SC-003).

**Constraints**:
- No key, key hash or full device id in any response or log (FR-009).
- The window is 30 days, fixed (spec Assumptions).

**Scale/Scope**:
- 1 column and 1 migration
- 1 endpoint for the date and 1 for the list
- 1 pure calculator
- 1 request field and 1 response field
- 2 new LoggerMessage events in the existing ranges (research R6).

## Constitution Check

_GATE: Must pass before Phase 0 research. Re-check after Phase 1 design._

| Principle | How the plan meets it | Status |
|---|---|---|
| I. Clean layering | The date is a domain property on `WaterHeater`, set only by its methods. The pure `KeyReminders` is in `Application/Common/ApiKeys`, next to `ApiKeyCrypto`, with no EF or HTTP. EF mapping and the migration are in Infrastructure. | ✅ |
| II. Vertical slices | The date belongs to the WaterHeaters slice: connect, change, settings. The reminder list is in the Locations slice, which already reads heaters through the Application interface `IWaterHeaterRepository`, as `GetMyLocationsQueryHandler` does since PR #40. No slice uses another slice's internals. | ✅ |
| III. Results over exceptions | An out-of-range date is a `Result` failure (`WaterHeater.KeyExpiryOutOfRange`), like `Bill.MonthNotStarted`. | ✅ |
| IV. Validate at the boundary | Validators check the shape: a valid date, and a non-empty location id. The checks against today need the clock, so they are in the handler with `TimeProvider`, the same split as the bill's "month not started". | ✅ |
| V. CQRS | One new command (`SetWaterHeaterKeyExpiryCommand`) and one new query (`GetKeyRemindersQuery`), each with its own handler. | ✅ |
| VI. Logging | Two new events in the existing ranges: 1806 (WaterHeater, the date changed) and 1406 (Locations, the list was read). They log ids, dates and counts only. The EventId table needs no new range. | ✅ |
| VII. Manual mapping | `WaterHeaterMapper.ToSettings` and a new `KeyReminderMapper` map by hand. | ✅ |
| VIII. Minimal footprint | No new repository method, table, background job or setting. The date endpoint exists so that correcting a date doesn't require pasting the key again (FR-004). | ✅ |

**Re-check after Phase 1**: unchanged, all pass, with no deviations.

## Project Structure

### Documentation (this feature)

```text
specs/015-key-expiry-reminders/
├── plan.md              # This file
├── research.md          # Phase 0: R1–R7
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── contracts/
│   └── endpoints.md     # Phase 1
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/Domain/Common/Entities/
└── WaterHeater.cs                     # + SubscriptionKeyExpiresOn; constructor and Replace take it; + SetKeyExpiry

src/Application/
├── Common/ApiKeys/KeyReminders.cs     # new: pure Due(...) → KeyReminder list (window, days left, order)
└── WaterHeaters/WaterHeaterModels.cs  # + WaterHeaterErrors.KeyExpiryOutOfRange

src/Infrastructure/Database/
├── Configuration/WaterHeaterConfiguration.cs        # + the date column
└── Migrations/<timestamp>_AddWaterHeaterKeyExpiry.cs  # add the column, fill existing rows (research R2)

src/Features/WaterHeaters/
├── Commands/WaterHeaterCommands.cs    # + ExpiresOn on connect; new SetWaterHeaterKeyExpiryCommand
├── Endpoints/WaterHeaterAdminEndpoints.cs  # + keyExpiresOn; new SetWaterHeaterKeyExpiryEndpoint
├── Handlers/ConnectWaterHeaterCommandHandler.cs     # default, range check, store the date
├── Handlers/SetWaterHeaterKeyExpiryCommandHandler.cs  # new
├── Queries/WaterHeaterQueries.cs      # + SubscriptionKeyExpiresOn on WaterHeaterSettingsResponse
├── Mappers/WaterHeaterMapper.cs       # map the date
└── Logging/LogMessages.cs             # + 1806 WaterHeaterKeyExpiryChanged

src/Features/Locations/
├── Queries/GetKeyRemindersQuery.cs    # new: the query, KeyRemindersResponse, KeyReminderResponse
├── Handlers/GetKeyRemindersQueryHandler.cs  # new
├── Endpoints/GetKeyRemindersEndpoint.cs     # new: GET /api/admin/key-reminders (Admin)
├── Mappers/KeyReminderMapper.cs       # new
└── Logging/LogMessages.cs             # + 1406 KeyRemindersListed

tests/Features.Tests/
├── Locations/KeyRemindersTests.cs, GetKeyRemindersQueryHandlerTests.cs  # new
├── WaterHeaters/ConnectWaterHeaterHandlerTests.cs, WaterHeaterAdminHandlerTests.cs  # + date cases
└── Http/KeyRemindersHttpTests.cs      # new: shape, 401, 403
```

**Structure Decision**: The date is part of the heater connection, so it goes in the WaterHeaters slice. The reminder
list is a view over locations and their keys for administrators, so it goes in the Locations slice next to the Admin
location list, reading heaters through the Application interface.

## Complexity Tracking

None. No principle is broken.
