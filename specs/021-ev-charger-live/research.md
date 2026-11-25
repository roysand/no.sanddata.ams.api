# Research: The Car Charger's State Right Now

**Spec**: [spec.md](spec.md) | **Date**: 2026-11-24

The spec's clarifications settled the no-charger answer, the offline rule, the 1-minute failure hold, power only while
charging, and the 3-second wait. This file settles the rest. Each item is a decision, why, and what else was weighed.

## R1. The Zaptec call and what is kept from it

**Decision**: `GET api/chargers/{chargerId}/state` with the shared Zaptec token (spec 016, `ZaptecTokenCache`). The
answer is a JSON array of `{ ChargerId, StateId, Timestamp, ValueAsString }`. The client keeps **only** these ids and
drops the rest before anything leaves Infrastructure (FR-006):

| Id  | Kept as                                   | Parse                                         |
| --- | ----------------------------------------- | --------------------------------------------- |
| -2  | `Online` (1 = true) and its timestamp     | integer                                       |
| -1  | `LastCheckInUtc`: the entry's timestamp   | timestamp only; the value isn't used          |
| 513 | `PowerW` and its timestamp                | decimal, invariant culture (`4414.704`)       |
| 553 | `SessionKwh` and its timestamp            | decimal, invariant culture                    |
| 710 | `OperationMode` and its timestamp         | integer                                       |
| 721 | `SessionSetUtc`: the entry's timestamp    | timestamp only; the session id isn't passed on |

- **Field names** are matched case-insensitively, as in spec 016's client.
- **`Timestamp`** has no `Z`, so it is read as UTC (`DateTimeStyles.AssumeUniversal | AdjustToUniversal`).
- **A value that is missing or can't be read** is null. It is not a failure, because the edge case "a value Zaptec
  doesn't send" asks for "unknown" with the figures that are there.
- **An id that appears twice**: the entry with the latest timestamp is kept.
- **An answer that isn't a JSON array** is `EvCharger.InvalidResponse`, as in spec 016.

**Rationale**: Roy's read on 2026-11-24 showed these ids on a Go during a charge. Cutting the list down in the client
means device ids, SIM numbers, signed meter data and the PIN never reach a handler, a cache or a log.

**Alternatives considered**: Passing the raw list to the handler was rejected because it would carry the values FR-006
forbids further in than needed. Zaptec's `GET api/chargers/{id}` (charger details) has no session kWh.

**Check by hand before implementation** (quickstart §0): the exact field names and timestamp format, what 553 holds
right after a new car is plugged in, and what 710, 513 and 553 hold with no car.

**Verified by hand** (Roy, 2026-11-24, during a charge on his Zaptec Go, filtered to the six ids):

| Id  | Timestamp (UTC, no `Z`)   | Value      | Read as                                        |
| --- | ------------------------- | ---------- | ---------------------------------------------- |
| -2  | `2026-11-23T07:33:37.387` | `1`        | online; changed yesterday and kept since        |
| -1  | `2026-11-24T20:28:48.134` | `null`     | the check-in: only its timestamp is used        |
| 513 | `2026-11-24T19:52:50.927` | `4450.216` | 4.45 kW, unchanged for 36 minutes               |
| 553 | `2026-11-24T20:27:21.103` | `21.483`   | 21.483 kWh; newer than 721, so not reset        |
| 710 | `2026-11-24T15:37:47.26`  | `3`        | charging since 15:37:47                         |
| 721 | `2026-11-24T14:52:37.97`  | (hidden)   | plugged in at 14:52:37                          |

- **Field names:** `ChargerId`, `StateId`, `Timestamp` and `ValueAsString`, in PascalCase, as the client expects.
- **Timestamps:** 2–3 decimals, and any number is read.
- **The check-in's value is `null`:** the test samples now use that.
- **The figures agree:** about 4.4 kW from 15:37 to 20:27 is about 21.4 kWh.

**Not read by hand:** "just after plugging in" and "no car" (Roy, 2026-11-24: go ahead, and fix it if a bug shows up).
The code doesn't depend on them:
- With no car, power and kWh aren't given, whatever 513 and 553 still hold.
- Just after plugging in, the kWh is 0 whether Zaptec resets 553 at once or keeps the old value until the next
  check-in. Kept, it is older than 721, and rule 4 gives 0.

If the Car tab shows last charge's kWh on a new charge, or a figure with no car, read the state by hand then.

## R2. From Zaptec's values to the answer

**Decision**: A pure function in Application, `ChargerLiveState.From(values, readAtUtc, nowUtc)`, gives the answer
([data-model.md](data-model.md#rules)). It runs on **every request**, with the cached values and the current time:

1. **Offline** when `Online` is false, or `LastCheckInUtc` is missing or more than 10 minutes before now. A missing
   `Online` with a recent check-in counts as online, since a fresh check-in shows the charger is there. The state
   is `offline`, with no power, no kWh and no times except `lastReportedAt` and `readAt`.
2. **State** from 710: 1 → `noCar`, 2 → `connected`, 3 → `charging`, 5 → `finished`. Anything else, or missing, is
   `unknown`.
3. **Power** only when `charging`: `PowerW / 1000`, rounded to 2 decimals. Otherwise null.
4. **kWh** when `connected`, `charging`, `finished` or `unknown`: `SessionKwh`, rounded to 3 decimals. If 553's
   timestamp is earlier than 721's, the value still belongs to the previous session, so it is `0` (edge case "a new
   charge"). With `noCar` it is null.
5. **Plugged in at** is 721's timestamp when there is a car (`connected`, `charging`, `finished`). Otherwise null.
6. **Charging since** is 710's timestamp when `charging`. Otherwise null.

**Rationale**: Working out the state on each request, rather than once when Zaptec is read, means the 10-minute offline
rule uses the time of the request, even when the values are up to 3 minutes old. A pure function can be tested
case by case without HTTP.

**Alternatives considered**:
- **Zaptec's session details for "charging since"** (spec assumption): `sessions/archived` only lists finished
  sessions. Reading the running session would be a second call per read, for little gain.
- **Pauses**: 710's timestamp changes when a pause ends, so "charging since" is the time charging last resumed. That
  is true to what the charger is doing, so it is kept and documented in the contract.

## R3. Sharing one answer per charger

**Decision**: A singleton `ChargerStateCache` in `Features/EvChargers/Services`, written by hand in the style of
`ZaptecTokenCache`:

- **Keyed by** the Zaptec charger id. Each entry holds the read's `Result` (values or error), when it was read, and
  when it expires: 3 minutes after a good read, 1 minute after a failed one.
- **Single flight**: while a read is running, everyone who asks for that charger waits for the same task. Zaptec is
  called once, even when the Car tab and the Now page ask at the same moment (SC-002).
- **Not cancelled by the caller**: the shared read doesn't use the request's `CancellationToken`. Each caller waits
  with its own token (`Task.WaitAsync(ct)`), so one browser closing a tab doesn't cancel the read for the others. The
  read ends after 3 seconds anyway (R4).
- **Time** comes from `TimeProvider`, so tests can move the clock.
- **The client** is resolved from a new scope for each read (`IServiceScopeFactory`), as `EvChargerReadService`
  does. The singleton never holds a typed `HttpClient`.
- **Lifetime**: in memory, lost on restart. Nothing is written to the database (FR-004).

**Rationale**: Production is one container (Hetzner), so an in-memory cache is shared by every request. About 60
lines of our own code give single flight and different times for success and failure, with no new package.

**Alternatives considered**: `IMemoryCache` (in the shared framework) doesn't do single flight, so it would still need
the lock. `HybridCache` would be a new package, and its cache stampede protection doesn't give a separate time for
failures. A background poller was rejected by US2-2: nobody looking means no calls.

## R4. 3 seconds, and no retry after a rate limit

**Decision**: The new `IEvChargerClient.GetStateAsync(chargerId, ct)` reads with:

- **A 3-second limit** over the whole read, including a sign-in if the token has to be fetched. This uses a linked
  `CancellationTokenSource` with `StateTimeout = TimeSpan.FromSeconds(3)`, a constant on the client. The client takes
  no `TimeProvider`, since a manual test clock doesn't drive timers anyway, so the timeout test waits the real 3 s.
  When it fires, the client logs `Unavailable` with the call name `state` and returns `EvCharger.Unavailable`.
  Today `SendAsync` lets a cancellation through when its token is cancelled, so the new method catches
  `OperationCanceledException` itself when only the timeout fired.
- **No wait-and-retry after a 429**: `GetJsonAsync` gets a `retryBusy` argument. The state read passes `false`, so a
  429 becomes `EvCharger.Unavailable` straight away. Spec 016's reads keep `true`.
- **One sign-in again after a 401**, as today, inside the same 3 seconds.

**Rationale**: This keeps SC-004 (never later than about 3 seconds) and FR-003. The 1-minute hold (R3) does the
trying again. Spec 016's reads keep their own 15-second `HttpClient.Timeout` and their 429 retry, so they aren't
affected.

**Alternatives considered**: A second typed `HttpClient` with a 3-second timeout would need a second registration and
would still need the 429 change. Changing the shared `HttpClient.Timeout` would cut spec 016's reads short.

**Shared token**: A 401 on the state read clears the shared token (`Invalidate`), as a 401 on a session read does
today. The next spec 016 read signs in again. That is the same behaviour as now, so it doesn't break FR-003's
"doesn't affect the session reads".

## R5. What the endpoint answers

**Decision**: `GET /api/locations/{locationId}/ev-charger/state` ([contracts/endpoints.md](contracts/endpoints.md)).

| Case                                                    | Answer                                                   |
| ------------------------------------------------------- | -------------------------------------------------------- |
| No access, or no such location                          | `404 Location.NotFound`, as the session list             |
| No charger connected                                    | `200`, `state: "noCharger"`, all else null (clarified)   |
| Zaptec login not set up                                 | `503 EvCharger.NotConfigured`; nothing is cached         |
| Zaptec failed, rate-limited or timed out (held 1 min)   | `503 EvCharger.Unavailable` or `SignInRejected`, `502 EvCharger.InvalidResponse` |
| Otherwise                                               | `200` with the state (R2)                                |

**Rationale**: Failures follow `EvChargerErrorStatus.Of` (spec 016) and the water heater state (spec 011), which
answers `503` when OSO can't be read. The frontend already handles those. "No charger" is not a failure: it is the
normal answer for most locations, and the Now page asks for every location (clarification 1).

**Alternatives considered**: A `200` with `state: "unavailable"` was rejected because it would hide a failure in a
success, unlike every other external read in the API.

## R6. Who may ask

**Decision**: As `GetChargingSessionsQueryHandler`: an Admin, or a user linked to the location (owner or viewer). If
the location doesn't exist, the answer is `404 Location.NotFound` for everyone. As there, an inactive location isn't
refused. JWT only.

**Rationale**: FR-005 says "as the charging session list". Using the same rule means a user who can see the sessions
can always see the state.

## R7. Logging

**Decision**:
- **Client**: reuses 1920–1924 with the call name `state` (`RequestFailed`, `Unavailable`, `InvalidResponse`,
  `SignInRejected`, `RateLimited`). It never logs the charger id, a URL, a body or a token.
- **Feature**: two new events in `Features/EvChargers/Logging/LogMessages.cs`, in a new block 1930–1939 for the live
  state:
  - **1930** `EvChargerStateRead`, Information: `{LocationId}` and `{ElapsedMs}`, once per Zaptec read, not once
    per request. This is what the SC-002 and SC-003 checks count. `ElapsedMs` is how long the Zaptec read took,
    sign-in included, for SC-004's "under 1 s when Zaptec has to be read".
  - **1931** `EvChargerStateFailed`, Warning: `{LocationId}` and `{Reason}`, once per failed read, so at most once a
    minute.
- The EventId table in `CLAUDE.md` / `DevelopmentGuide.md` already gives EvCharger 1900–1999, so only the block
  comment in `LogMessages` changes.

**Rationale**: Logging per Zaptec read rather than per request gives a count of Zaptec calls in Application Insights
without a log line on every request.

## R8. Never stored, never counted

**Decision**: The handler has no repository that writes, and nothing calls `SaveChanges`. `ChargerLiveState` isn't
used by ElectricityCost, the bill or Norgespris. A handler test checks that `IChargingSessionRepository` is never
called (FR-004).

**Rationale**: Once a charge ends, spec 016 stores it. Counting the live figures as well would count the charge twice.
