# Data Model: Turn the Water Heater's High Demand On and Off

Nothing is stored (FR-010). There is no migration.

## Application (`Application/WaterHeaters`, `Application/Common/Interfaces/External`)

### `IWaterHeaterClient` (changed)

```text
Task<Result> SetHighDemandAsync(WaterHeaterCredentials credentials, bool on, CancellationToken ct)
```

- `on = true` sends `POST …/HighDemand/`, and `on = false` sends `DELETE …/HighDemand/` (research R1).
- Success: OSO answered 2xx. Failures map as in research R2.
- Sent once, never retried (R3).

### `WaterHeaterErrors` (one new error)

| Error | Type | Code | Message |
|---|---|---|---|
| `HighDemandNotOffered` | Problem | `WaterHeater.HighDemandNotOffered` | "OSO no longer offers High Demand for this heater" |

## Features (`Features/WaterHeaters`)

### `SetWaterHeaterHighDemandCommand`

| Field | Type | Notes |
|---|---|---|
| `ActingUserId` | `Guid` | The Admin, from `ClaimTypes.NameIdentifier`, for the log |
| `LocationId` | `Guid` | From the route (`Route<Guid>`; not a GUID gives 400) |
| `On` | `bool` | `true` from `PUT`, `false` from `DELETE` |

Returns `Result<WaterHeaterHighDemandResponse>`.

### `WaterHeaterHighDemandResponse`

| Field | Type | Notes |
|---|---|---|
| `LocationId` | `Guid` | |
| `HighDemand` | `bool` | What was asked for: `true` on, `false` off |
| `State` | `WaterHeaterStateResponse?` | The heater's state read back after the switch, the same shape as `GET /api/locations/{id}/water-heater`. `null` when that read failed |

`State.SpecialMode` gives the outcome: `Mode` is `Supercharge` with `Start` and `End` (UTC) when High Demand is on,
and `None` (or `Holiday` or `BlueBattery`) otherwise.

## Rules

```text
PUT / DELETE …/high-demand
  ──► not Admin: 401 / 403, nothing else happens            (FastEndpoints, FR-003)
  ──► location missing: 404 Location.NotFound
  ──► no heater: 404 WaterHeater.NotConnected
  ──► key unreadable: 502 WaterHeater.KeyUnreadable          (log 1808)
  ──► SetHighDemandAsync(on) once
        failure ──► its error, 502 or 503                    (log 1808)
        success ──► GetStateAsync once
                      ok     ──► 200 { highDemand, state }          (log 1807, stateRead = true)
                      failed ──► 200 { highDemand, state: null }    (log 1807, stateRead = false)
```
