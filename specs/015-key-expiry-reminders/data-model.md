# Data Model: Reminders Before Keys Expire

Phase 1 of [plan.md](plan.md). The wire format is in [contracts/endpoints.md](contracts/endpoints.md), and the
decisions in [research.md](research.md).

## Changed entity: `WaterHeater` (Domain)

| Field | Type | Rules |
|---|---|---|
| `SubscriptionKeyExpiresOn` | `DateOnly` (PostgreSQL `date`, NOT NULL) | The last Oslo day the OSO key is valid. Set by the constructor, `Replace` and `SetKeyExpiry`. There is no public setter. |

The methods:
- `WaterHeater(id, locationId, deviceId, protectedKey, keyExpiresOn)`
- `Replace(deviceId, protectedKey, keyExpiresOn)`: a new key brings its own date
- `SetKeyExpiry(keyExpiresOn)`: the date alone, with the key and the device id unchanged

**Range rule:** `today < date ≤ today + 5 years`, where today is in Oslo. It is checked in the handlers, with
`TimeProvider`, before these methods are called (research R1, R5).

**Existing rows:** the Oslo date of `CreatedAt` + 1 year (research R2).

## New pure type: `KeyReminders` (Application/Common/ApiKeys)

### Inputs

- `SensorKeyFacts(Guid LocationId, string LocationName, bool LocationActive, bool KeyActive, DateTime ExpiresAtUtc)`
- `HeaterKeyFacts(Guid LocationId, string LocationName, DateOnly ExpiresOn)`

### Output: `KeyReminder`

| Field | Type | Rule |
|---|---|---|
| `LocationId`, `LocationName` | `Guid`, `string` | From the location |
| `Kind` | `SensorKey` \| `WaterHeaterKey` | |
| `ExpiresOn` | `DateOnly` | The last valid Oslo day. For a sensor key: the Oslo date of `ExpiresAtUtc − 1 tick`. |
| `ExpiresAtUtc` | `DateTime?` | Sensor keys only: the exact instant. null for a heater key. |
| `DaysLeft` | `int` | `ExpiresOn − today`. 0 means its last day is today; negative means it has expired. |
| `Expired` | `bool` | `DaysLeft < 0` |

### `Due(DateOnly today, sensorKeys, heaterKeys, int windowDays = 30)`

- **Kept:** the keys with `DaysLeft ≤ windowDays`, including every expired one.
- **Left out:** sensor keys with `!LocationActive || !KeyActive`. Heater keys come only from active locations.
- **Order:** by `DaysLeft`, then by `LocationName`, then `SensorKey` before `WaterHeaterKey`.

## Invariants (tested)

- 31 days left → not listed; 30 → listed; 0 → listed, not expired; −1 → listed, expired.
- A sensor key expiring at 2028-11-20T10:00Z has the last day 2028-11-20.
- A sensor key expiring at exactly Oslo midnight between the 19th and the 20th has the last day 2028-11-19.
- Days left are whole days, including across a clock change.
- No key, hash or device id is in any of these types.
