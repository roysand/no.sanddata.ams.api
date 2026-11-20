# Data Model: The Water Heater's Power over the Last Hours

Nothing new is stored, there is no migration, and no existing table or column changes.

## Read: `WaterHeaterPowerSample` (spec 011, unchanged)

| Field | Type | Note |
|---|---|---|
| `LocationId` | uuid | The samples belong to the location and outlive the heater's connection |
| `SampledAtUtc` | timestamptz | About once a minute |
| `PowerKw` | numeric(9,6) | The heater's power at that moment |

The index `(LocationId, SampledAtUtc)` serves the window read.

## Derived: `WaterHeaterPowerStep` (new record, Application/WaterHeaters)

One row per 5-minute step that has any sampled time. The repository returns it, and nothing stores it.

| Field | Type | Rule |
|---|---|---|
| `StepStartUtc` | DateTime (UTC) | On whole 5 minutes of the UTC clock (`date_bin('5 minutes', t, epoch)`) |
| `Kwh` | decimal | Σ power × held time in the step, ≥ 0 |
| `SampledSeconds` | decimal | Σ held time in the step, 0 < s ≤ 300 |

The hold rule is the same as spec 012:
- Each sample holds until the next sample, for at most 2 minutes, and never past now or `to`.
- The 2 minutes before `from` are read, so the first step's start is covered.
- A hold that crosses a step boundary is split between the two steps.

## Response item (Features/WaterHeaters, mapped by `WaterHeaterMapper`)

| Field | Type | From the step |
|---|---|---|
| `start` | DateTime (UTC) | `StepStartUtc` |
| `powerKw` | decimal, 3 decimals | `Kwh × 3600 / SampledSeconds` |
| `sampledSeconds` | int, 1–300 | `SampledSeconds` rounded; a step that rounds to 0 is left out |

## Window (request, `PowerWindow`)

| Input | Left out | Refused when |
|---|---|---|
| `to` | now | — (holds stop at now anyway) |
| `from` | `to` − 3 hours | not before `to`; more than 48 hours before `to`; after now |
