# Data Model: Read the OSO Water Heater

Phase 1 of [plan.md](plan.md). Decisions behind it: [research.md](research.md) R1, R4, R5, R8.

## Stored

### `WaterHeater` (Domain entity, new table)

One connected heater. Created by connect, replaced by connect again, deleted by remove.

| Field | Type | Rules |
|---|---|---|
| `Id` | `Guid` | Primary key |
| `LocationId` | `Guid` | FK → `Location` (`Restrict`); **unique**: at most one heater per location |
| `DeviceId` | `string`, `varchar(200)` | Lowercased hex as entered; **unique**: one location per device (FR-005). Never returned in full, never logged |
| `ProtectedSubscriptionKey` | `string`, `varchar(500)` | `v1:` + base64(nonce ‖ ciphertext ‖ tag), AES-256-GCM, location id as associated data (R1). Never returned, never logged |
| `ConnectedAtUtc` | `DateTime` (UTC) | When first connected |
| `UpdatedAtUtc` | `DateTime` (UTC) | When the key or device id last changed |

Domain behaviour:

- `WaterHeater(Guid id, Guid locationId, string deviceId, string protectedKey, DateTime nowUtc)`
- `Replace(string deviceId, string protectedKey, DateTime nowUtc)`: sets both, keeps `ConnectedAtUtc`, sets
  `UpdatedAtUtc`.

The Domain only holds the protected string; encrypting and decrypting stay behind `ISecretProtector` (Application
interface, Infrastructure implementation), so no crypto reaches the Domain.

### `WaterHeaterPowerSample` (Domain entity, new table)

One reading of the heater's power. Append-only.

| Field | Type | Rules |
|---|---|---|
| `Id` | `Guid` | Primary key |
| `LocationId` | `Guid` | FK → `Location` (`Restrict`). **No FK to `WaterHeater`**, so samples outlive a removed connection (R8) |
| `SampledAtUtc` | `DateTime` (UTC) | When OSO was asked, truncated to the second |
| `PowerKw` | `decimal`, `numeric(9,6)` | As OSO answers (e.g. `0.011774`); never rounded to 0 (US3, scenario 6) |

Index: `(LocationId, SampledAtUtc)`, not unique (two samples in the same second cannot happen with one sampler, and a
duplicate does no harm).

### Migration

`AddWaterHeater`: both tables, the two unique indexes on `WaterHeater`, the index on `WaterHeaterPowerSample`. No
change to existing tables.

## Not stored

### `WaterHeaterCredentials` (Application record)

`(Guid LocationId, string DeviceId, string SubscriptionKey)`: the decrypted values, built in a handler or the sampler
just before calling OSO and passed to `IWaterHeaterClient`. Its `ToString()` is overridden to print only the location
id, so an accidental log or exception message cannot reveal the values.

### `WaterHeaterReading` (Application record, from OSO)

What `IWaterHeaterClient.GetStateAsync` returns, already cleaned (R5):

| Field | Type | From OSO |
|---|---|---|
| `ReadingTimeUtc` | `DateTime` | `Status.lastTelemetry.timestamp` |
| `LastActivityUtc` | `DateTime?` | `Status.lastActivityTime` |
| `IsOnline` | `bool` | `Status.connectionState == 1` |
| `IsHeating` | `bool` | `Status.lastTelemetry.control.heater == 1` |
| `SetPointCelsius` | `decimal?` | `Status.lastTelemetry.control.currentSetPoint` |
| `TemperatureTopCelsius` | `decimal?` | `CurrentTemperature.temperatureTop` |
| `TemperatureMidCelsius` | `decimal?` | `CurrentTemperature.temperatureMid` |
| `TemperatureLowCelsius` | `decimal?` | `CurrentTemperature.temperatureLow` |
| `TemperatureFourthCelsius` | `decimal?` | `CurrentTemperature.temperatureOne`; `null` when 0 (FR-009) |
| `AvailableLitres40` | `int?` | `Status.availableMix40Volume` |
| `MaxLitres40` | `int?` | `Status.maxMix40Capacity` |
| `AvailableShowers` | `int?` | `Status.availableShowers` |
| `AvailableBaths` | `int?` | `Status.availableBaths` |
| `PowerKw` | `decimal` | `ActualLoad.actualLoad` |
| `StoredEnergyKwh` | `decimal?` | `TappingCapacityKwh.tappingCapacitykWh` |
| `SpecialMode` | enum `None`, `Holiday`, `Supercharge`, `BlueBattery` | `Status.profileStatus.isIn…Mode` |
| `SpecialModeStartUtc`, `SpecialModeEndUtc` | `DateTime?` | the matching `…StartTime` / `…EndTime` (no zone → UTC) |
| `ProfileName` | `string?` | `Status.profileStatus.meaning` |
| `OsoCodes` | `(int? ControlMode, int? OptimizationOption, int? OptimizationSubOption, int? ProfileId)` | passed on, not interpreted |

### Derived in the handler

- `AgeSeconds` = now − `ReadingTimeUtc`, never negative (a reading a few seconds "in the future" from clock skew is
  0).
- `IsStale` = age > 15 minutes (FR-008).
- `DeviceIdHint` = first 6 + `…` + last 6 characters (R4).

## State transitions

```text
(no heater) --connect (OSO accepts)--> Connected
Connected   --connect again (OSO accepts)--> Connected (key/device replaced, UpdatedAtUtc set)
Connected   --remove--> (no heater)        samples kept
Connected + location active   : sampled every minute
Connected + location inactive : not sampled, state answers 404 (location not visible)
```

## Validation (at the boundary, FluentValidation)

- `SubscriptionKey`: required, 1–200 characters, no whitespace.
- `DeviceId`: required, 1–200 characters, hexadecimal only (`^[0-9a-fA-F]+$`), stored lowercased.
- `LocationId` (route): non-empty GUID.

Whether OSO accepts the pair, and whether the device is already at another location, are decided by the handler
(they depend on OSO and on the database), as `Bill.MonthNotStarted` depends on the clock in spec 010.
