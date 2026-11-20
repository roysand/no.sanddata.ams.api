# Data Model: Read the Zaptec EV Charger and Optionally Count It with the House

Phase 1 of [plan.md](plan.md). One migration, `AddEvCharger`, adds three tables and one column.

## Entities (Domain, `src/Domain/Common/Entities/`)

### EvCharger (the connection)

| Field | Type | Rules |
|---|---|---|
| `Id` | Guid | |
| `LocationId` | Guid | FK → Location, **unique** (one charger per location) |
| `ChargerId` | Guid | Zaptec charger id, **unique** (one location per charger) |
| `ChargerName` | string (≤ 200) | From Zaptec when connected |
| `InstallationId` | Guid | Zaptec installation id, kept for the later UserGroup route |
| `InstallationName` | string (≤ 200) | |
| `ReadUpToUtc` | DateTime? | End of the last successful read window (research R3); null until the first read |
| `CreatedAt` / `UpdatedAt` | from `Entity` | When it was connected / last changed |

- **Methods:** `Replace(chargerId, chargerName, installationId, installationName)` resets `ReadUpToUtc` to null, so
  the new charger's history is read back; `MarkReadUpTo(DateTime utc)`.
- **No secrets.** `ToString()` prints only the location id, as `WaterHeater` does.

### ChargingSession

| Field | Type | Rules |
|---|---|---|
| `Id` | Guid | |
| `LocationId` | Guid | FK → Location. Kept when the connection is removed |
| `ZaptecSessionId` | Guid | **unique** |
| `ChargerId` | Guid | Zaptec charger id it was read from |
| `StartUtc` / `EndUtc` | DateTime (UTC) | `EndUtc ≥ StartUtc` |
| `Kwh` | decimal(12,4) | ≥ 0; the session's `energy` |
| `HasEnergyDetails` | bool | false when the hours were spread evenly (research R5) |
| `Hours` | collection of `ChargingSessionHour` | Owned by the session; cascade delete |

- **Index:** `(LocationId, EndUtc)` for the sessions list.

### ChargingSessionHour

| Field | Type | Rules |
|---|---|---|
| `Id` | Guid | |
| `ChargingSessionId` | Guid | FK → ChargingSession, cascade delete |
| `LocationId` | Guid | Copied from the session, for the hourly query |
| `HourStartUtc` | DateTime (UTC, whole hour) | |
| `Kwh` | decimal(12,4) | ≥ 0 |
| `Estimated` | bool | true when spread evenly |

- **Index:** `(LocationId, HourStartUtc)`.
- **Unique:** `(ChargingSessionId, HourStartUtc)`.

### Location (changed)

| Field | Type | Rules |
|---|---|---|
| `IncludeEvChargerInTotals` | bool, default false | Set by owner or Admin; reset to false when the charger is removed |

- **Method:** `SetIncludeEvCharger(bool)`.

## Application types (`src/Application/EvChargers/`)

| Type | Shape | Use |
|---|---|---|
| `ZaptecCharger` | `(Guid Id, string Name, Guid InstallationId, string InstallationName)` | Admin list and connect check |
| `ZaptecSession` | `(Guid Id, DateTime StartUtc, DateTime EndUtc, decimal Kwh, IReadOnlyList<EnergyPoint> Details, bool Discard)` | One session from Zaptec; `Discard` is true when it is voided or replaced |
| `EnergyPoint` | `(DateTime TimestampUtc, decimal Kwh)` | kWh delivered in the period ending at the timestamp (research R5) |
| `ChargingHourSplit` | `(DateTime HourStartUtc, decimal Kwh, bool Estimated)` | Output of `ChargingEnergySplitter.Split` |
| `EvChargerHourEnergy` | `(DateTime HourStartUtc, decimal Kwh, bool Estimated)` | Charger kWh per hour for a range (repository) |
| `EvChargerHour` | `(DateTime HourStartUtc, decimal Kwh, bool Estimated, bool Included)` | Attached to `HourCost` |
| `EvChargerTotal` | `(decimal Kwh, bool Estimated, bool Included, int HoursWithoutMeterReading, DateTime FirstHourUtc, DateTime LastHourUtc)` | Day and bill sums (`EvChargerShare.Sum`) |
| `EvChargerErrors` | static `Error`s | See below |

### Errors

| Code | Kind | HTTP |
|---|---|---|
| `EvCharger.NotConfigured` | Problem | 503 (sign-in not set up on the server) |
| `EvCharger.SignInRejected` | Problem | 503 on reads; 400 on connect |
| `EvCharger.Unavailable` | Problem | 503 |
| `EvCharger.InvalidResponse` | Problem | 502 |
| `EvCharger.UnknownCharger` | Validation | 400 (the sign-in cannot see that charger) |
| `EvCharger.ChargerInUse` | Conflict | 409 (connected to another location) |
| `EvCharger.NotConnected` | NotFound | 404 for settings; 400 when turning the setting on |
| `EvCharger.Forbidden` | Failure (mapped by code) | 403 (a viewer changing the setting) |

## Interfaces (`src/Application/Common/Interfaces/`)

- **`External/IEvChargerClient`** (read only):
  - `GetChargersAsync(ct) → Result<IReadOnlyList<ZaptecCharger>>`
  - `GetCompletedSessionsAsync(chargerId, fromUtc, toUtc, ct) → Result<IReadOnlyList<ZaptecSession>>`
  - `IsConfigured` (bool)
- **`Repositories/IEvChargerRepository`:**
  - `GetByLocationAsync`, `GetByChargerIdAsync`, `GetAllForActiveLocationsAsync`, `Insert`, `Remove`,
    `SaveChangesAsync`
- **`Repositories/IChargingSessionRepository`:**
  - `UpsertAsync(locationId, chargerId, IReadOnlyList<ZaptecSession>, ct)`: replaces each session and its hours, and
    deletes discarded ones (research R4)
  - `GetHourlyEnergyAsync(locationId, fromUtc, toUtc, ct) → IReadOnlyList<EvChargerHourEnergy>`
  - `GetSessionsAsync(locationId, fromUtc, toUtc, ct)`, newest first
  - `AnyForLocationAsync(locationId, ct)`

## Rules (mapped to the spec)

| Rule | Where | Spec |
|---|---|---|
| Hours sum to the session's kWh | `ChargingEnergySplitter` | SC-002 |
| A session read again replaces the earlier version and its hours | `UpsertAsync` | FR-008 |
| A failed read stores and deletes nothing, and does not move `ReadUpToUtc` | `EvChargerReadService` | FR-009 |
| `totalKwh = consumptionKwh + (included ? evCharger.kwh : 0)` | mappers | FR-011 |
| The part is null when there are no charger hours | `EvChargerShare.Sum`, mappers | FR-013 |
| Costs use the meter's kWh only | unchanged `CostCalculator` and `MonthlyBillBuilder` | FR-014 |

## State

The connection has no state machine. It is connected (a row exists) or not, and `ReadUpToUtc` moves forward only
after a successful read. Removing the row stops reading and keeps `ChargingSession` and `ChargingSessionHour`.
