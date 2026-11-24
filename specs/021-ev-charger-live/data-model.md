# Data Model: The Car Charger's State Right Now

**Spec**: [spec.md](spec.md) | **Research**: [research.md](research.md)

**Nothing is stored.** There is no entity, column or migration. The location's charger comes from spec 016's
`EvCharger` row (`IEvChargerRepository.GetByLocationAsync`).

## Application types (`src/Application/EvChargers/`)

### `ZaptecStateValue` (in `EvChargerModels.cs`)

`record ZaptecStateValue<T>(T Value, DateTime TimestampUtc)`: one of Zaptec's state values and when it last changed.

### `ZaptecChargerState` (in `EvChargerModels.cs`)

The values the client keeps from `GET api/chargers/{id}/state` (research R1). Each is null when Zaptec didn't send it
or it couldn't be read.

| Field            | Type                              | From  |
| ---------------- | --------------------------------- | ----- |
| `Online`         | `ZaptecStateValue<bool>?`         | -2    |
| `LastCheckInUtc` | `DateTime?`                       | -1's timestamp |
| `PowerW`         | `ZaptecStateValue<decimal>?`      | 513   |
| `SessionKwh`     | `ZaptecStateValue<decimal>?`      | 553   |
| `OperationMode`  | `ZaptecStateValue<int>?`          | 710   |
| `SessionSetUtc`  | `DateTime?`                       | 721's timestamp |

No charger id, session id, serial, SIM number, meter data or PIN (FR-006).

### `ChargerLiveState` (new file `ChargerLiveState.cs`)

`static ChargerLiveState From(ZaptecChargerState values, DateTime readAtUtc, DateTime nowUtc)` plus `NoCharger(...)`.
It is pure and holds no state.

| Field              | Type        | Meaning                                                              |
| ------------------ | ----------- | -------------------------------------------------------------------- |
| `State`            | `string`    | `noCharger`, `offline`, `noCar`, `connected`, `charging`, `finished` or `unknown` |
| `Online`           | `bool?`     | null only with `noCharger`                                           |
| `PowerKw`          | `decimal?`  | only with `charging`                                                 |
| `SessionKwh`       | `decimal?`  | with `connected`, `charging`, `finished` or `unknown`                |
| `PluggedInAtUtc`   | `DateTime?` | with `connected`, `charging` or `finished`                           |
| `ChargingSinceUtc` | `DateTime?` | only with `charging`                                                 |
| `LastReportedAtUtc`| `DateTime?` | the last check-in; null only with `noCharger` or when Zaptec sent none |
| `ReadAtUtc`        | `DateTime?` | when Zaptec was read; null only with `noCharger`                     |

The state names are constants in `ChargerLiveState.States`, so the mapper and the tests share them.

## Rules

Spec references are in brackets.

1. **Offline** [clarification 2, US1-5]: `Online` is false, or `LastCheckInUtc` is missing or more than 10 minutes
   (`OfflineAfter`) before `nowUtc`. A missing `Online` with a recent check-in counts as online. When offline,
   `State = offline`, `Online = false`, and only `LastReportedAtUtc` and `ReadAtUtc` are set.
2. **State** [FR-001, edge case unknown mode]: 710 = 1 → `noCar`, 2 → `connected`, 3 → `charging`, 5 → `finished`.
   Anything else, or missing, is `unknown`.
3. **Power** [clarification 4]: `round(PowerW / 1000, 2)` only with `charging`. If 513 is missing while charging,
   it is null.
4. **kWh** [clarification 4, edge case new charge]: `round(SessionKwh, 3)` with `connected`, `charging`, `finished`
   or `unknown`. It is `0` when 553's timestamp is earlier than `SessionSetUtc` (the previous session's value). With
   `noCar` it is null.
5. **Plugged in at** [assumption]: `SessionSetUtc` with `connected`, `charging` or `finished`.
6. **Charging since** [assumption, research R2]: 710's timestamp with `charging`. After a pause, this is when charging
   resumed.
7. **Never counted** [FR-004]: no other slice uses these types.

## `ChargerStateCache` (`src/Features/EvChargers/Services/`, singleton)

| Per charger id   | Meaning                                                               |
| ---------------- | --------------------------------------------------------------------- |
| `Read`           | `Task<Result<ZaptecChargerState>>`: the running or finished read      |
| `ReadAtUtc`      | when the read finished                                                |
| `ExpiresAtUtc`   | `ReadAtUtc` + 3 minutes after a good read, + 1 minute after a failure |

`GetAsync(chargerId, locationId, ct)` returns the read when it is running or not yet expired. Otherwise it starts one
through `IEvChargerClient.GetStateAsync(chargerId, CancellationToken.None)`, with the client resolved from a new scope
for each read (research R3). It returns
`Result<(ZaptecChargerState Values, DateTime ReadAtUtc)>`.

## Interface change (`src/Application/Common/Interfaces/External/IEvChargerClient.cs`)

```text
Task<Result<ZaptecChargerState>> GetStateAsync(Guid chargerId, CancellationToken cancellationToken);
```

The method gives up after 3 seconds and doesn't wait and retry after a 429 (research R4). Like the other methods, it
never throws for HTTP, timeout or parsing failures, and it returns `EvCharger.NotConfigured` when the login isn't set
up.

## Errors

There are no new errors. The state uses `EvChargerErrors.NotConfigured`, `Unavailable`, `SignInRejected` and
`InvalidResponse`, plus `Location.NotFound`.
