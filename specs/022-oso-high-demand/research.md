# Research: Turn the Water Heater's High Demand On and Off

Spec: [spec.md](spec.md). This file also holds the backlog note `specs/_backlog/oso-high-demand.md` (written
2026-11-26, never committed), which this feature took up.

## R1. The OSO calls

**Decision**: Use the two undocumented calls from OSO's own Homey app (github.com/osohotwateriot/no.osoincharge,
`app.ts`), on the same base address and key header as the read client (spec 011):

| What | Call | Body |
|---|---|---|
| On | `POST 1/Device/{deviceId}/HighDemand/` | none (`Content-Length: 0`) |
| Off | `DELETE 1/Device/{deviceId}/HighDemand/` | none |

The trailing slash is kept exactly as the Homey app and Roy's hand test used it. Any 2xx is success, and the body
is ignored.

**Roy's hand test, 2026-11-26, about 05:21 UTC** (his OSO key, his own terminal). High Demand was already on, turned
on in the OSO app with an end time of 21:55.

| Step | Answer | `Status` afterwards |
|---|---|---|
| `DELETE` | 200 | `isInSuperchargeMode: false`, `profileStatus.meaning: "NormalMode"` (profile id 1) |
| `POST` | 200 | `isInSuperchargeMode: true`, `meaning: "SuperchargeMode"` (profile id 3) |

- **It lasts 24 hours.** After the `POST`, the start was 2026-11-26T05:21:13 and the end 2026-11-27T05:21:13.
- **The times are UTC without `Z`.** The start matches the telemetry timestamp, which has a `Z`. The client already
  reads them as UTC (spec 011 Assumptions).
- **The heater lists the feature.** `Status` has `"features": ["SuperChargeMode"]`.
- **Probably heats to 72 °C.** The telemetry's `currentSetPoint` was 72 while on and 60 in normal mode, one reading
  each.
- **The OSO app didn't follow the off call.** After the `DELETE`, OSO's `Status` said normal mode, but the OSO app
  still showed High Demand (Roy). This is FR-009's open question.

**Rationale**: OSO's public Swagger has no other way. The Homey app is OSO's own code, so the calls are OSO's, just not
documented.

**Alternatives considered**:
- *`Profile` (documented).* It sets the heating profile, not High Demand, and spec 011 never mapped its values.
- *Waiting for OSO to document the calls.* No sign that they will. FR-005's own error catches a removed call.

## R2. Where the write call lives

**Decision**: One new method on `IWaterHeaterClient`:

```text
Task<Result> SetHighDemandAsync(WaterHeaterCredentials credentials, bool on, CancellationToken ct)
```

`OsoWaterHeaterClient` sends `POST` or `DELETE` through a small `SendAsync` beside the existing `GetAsync`, with the
same key header, the same "never log the URL" rule and the same exception handling. The interface's summary changes
from "read only on purpose" to "read only, except High Demand (spec 022)".

The status of the answer maps as for reads, except for one:

| OSO answers | Error |
|---|---|
| 2xx | none |
| 401, 403 | `WaterHeater.Rejected` |
| **404** | **`WaterHeater.HighDemandNotOffered`** (new) |
| 408, 429, 5xx, timeout, network | `WaterHeater.Unavailable` |
| anything else | `WaterHeater.InvalidResponse` |

**Rationale**:
- A 404 on a read means a wrong device id, which is `Rejected`. On this call the device was just read fine, so a 404
  much more likely means OSO removed the undocumented call (FR-005).
- The client already logs a failed call with its name (`RequestFailed`, 1820, `call = "HighDemand"`), so no new
  client log event is needed.

**Alternatives considered**: *A separate `IWaterHeaterControl` interface.* It would make the exception to the
read-only rule more visible, but it would need a second typed `HttpClient` with the same setup. One method, named for
what it does, plus the changed summary is enough.

## R3. No retry

**Decision**: The call is sent once. The OSO `HttpClient` has no resilience or retry handler (spec 011: only a 10 s
timeout and `RemoveAllLoggers`), and the new code adds none (FR-006).

**Rationale**: a retried `POST` after a timeout could restart the 24 hours, and a retried `DELETE` is pointless if the
first one landed. The Admin can press again.

## R4. The answer: the switch, then the state read back

**Decision**: After a successful switch, the handler reads the heater's state once with the existing
`GetStateAsync` and maps it with the existing `WaterHeaterMapper.ToState`. The answer is:

```text
WaterHeaterHighDemandResponse(LocationId, HighDemand: bool, State: WaterHeaterStateResponse?)
```

`HighDemand` is what was asked for. `State` is the same object the state endpoint returns, or `null` when the
read-back failed (FR-004). The frontend can put `State` straight into its cached heater state.

**Rationale**:
- OSO's own status is the truth, not our request (spec edge case "OSO reports the old mode for a moment").
- Reusing the state shape means no new mapping and no second idea of what "the mode" is.
- The state endpoint reads OSO on every request (spec 011 has no cache), so the next poll shows the change too.

**Alternatives considered**:
- *Answer with only the mode and its times.* Smaller, but the frontend would have to merge it into its state.
- *Answer 204 with nothing.* Then the frontend reads the state itself, which is one more round trip for the same
  data.

## R5. The endpoints and who may call them

**Decision**:
- **On:** `PUT /api/admin/locations/{locationId}/water-heater/high-demand`.
- **Off:** `DELETE /api/admin/locations/{locationId}/water-heater/high-demand`.
- **Both:** JWT only and `Roles(RoleNames.Admin)`, so FastEndpoints answers 401 without a token and 403 for any other
  role before the handler runs. OSO is never called then (FR-003).
- **One command:** `SetWaterHeaterHighDemandCommand(ActingUserId, LocationId, On)` with one handler. The endpoints
  are `EndpointWithoutRequest` and read the route with `Route<Guid>("locationId")`, like `RemoveWaterHeaterEndpoint`.
  A value that isn't a GUID gives a 400, and there's no body, so there's no validator.
- **The handler's order:**
  1. The location must exist (`404 Location.NotFound`), as in `RemoveWaterHeaterCommandHandler`. An inactive
     location may still be switched, as an Admin may still read it.
  2. A heater must be connected (`404 WaterHeater.NotConnected`).
  3. The key must be readable (`502 WaterHeater.KeyUnreadable`).
  4. Switch, then read back.

**Rationale**:
- The `/api/admin/locations/{id}/water-heater` prefix is where the Admin's heater calls already live (connect,
  settings, remove).
- `PUT` and `DELETE` on one resource read naturally as "make it on" and "make it off". Both are idempotent at OSO.

**Alternatives considered**: *`POST …/high-demand` with `{ "on": true }`.* One endpoint, but a body for a single bit,
and a `false` that reads oddly next to OSO's own `DELETE`.

## R6. Errors to HTTP

**Decision**: Reuse `WaterHeaterErrorStatus.Of(error, onConnect: false)`. The new `WaterHeater.HighDemandNotOffered` is
an `Error.Problem`, so it gets an explicit row there: **502**. It's an upstream failure, like `InvalidResponse`.

| Status | Code | When |
|---|---|---|
| 401 | | No token |
| 403 | | Signed in, not an Admin |
| 404 | `Location.NotFound` | The location doesn't exist |
| 404 | `WaterHeater.NotConnected` | No heater connected |
| 502 | `WaterHeater.Rejected` | OSO rejected the stored key or device |
| 502 | `WaterHeater.HighDemandNotOffered` | OSO answered 404 to the switch |
| 502 | `WaterHeater.InvalidResponse`, `WaterHeater.KeyUnreadable` | As for the state |
| 503 | `WaterHeater.Unavailable` | OSO down, slow (10 s) or rate-limited |

## R7. Logging

**Decision**: Two new events in `Features/WaterHeaters/Logging/LogMessages.cs`, in the WaterHeater range:

| EventId | Name | Level | Fields |
|---|---|---|---|
| 1807 | `WaterHeaterHighDemandSwitched` | Information | `ActingUserId`, `LocationId`, `On`, `StateRead` |
| 1808 | `WaterHeaterHighDemandFailed` | Warning | `ActingUserId`, `LocationId`, `On`, `Reason` |

`Reason` uses the existing `LogMessages.Reason(error)` codes. The client's own failures keep logging 1820–1822 with
`call = "HighDemand"`. The key and the device id are never logged (FR-007, spec 011 FR-016).

**Rationale**: who switched what and when is the audit Roy needs if the heater behaves oddly. A stored history isn't
asked for (FR-010).

## R8. The real-heater test (FR-009)

**Decision**: [quickstart.md §3](quickstart.md#3-against-the-real-heater) is the test. Roy runs it after the deploy,
with the Heater tab or `curl`:

1. Turn High Demand on through the API. Within a few minutes the set point is 72 and the heater heats (power > 0).
2. While the tank is still below 72 °C, turn it off through the API. Within a few minutes the set point is back to
   60, and within the hour the heater stops heating unless the tank is below 60 °C. Our power samples (spec 011) show
   when it stopped.
3. Note what the OSO app shows after each step, and whether it catches up after being restarted.

If step 2 fails, the frontend hides the switch (spec Assumptions) and the outcome is written down here and in the
spec.

## R9. Spec 011's read-only rule

**Decision**: Spec 011's FR-015 stays as it is: it describes spec 011's version. This spec's FR-008 lifts it for High
Demand only. The code comments that say "read only" (the `IWaterHeaterClient` and `OsoWaterHeaterClient` summaries)
name the one exception and point to spec 022.
