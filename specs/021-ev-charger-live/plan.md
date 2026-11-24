# Implementation Plan: The Car Charger's State Right Now

**Branch**: `feature/021-ev-charger-live` | **Date**: 2026-11-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/021-ev-charger-live/spec.md`

## Summary

A new endpoint, `GET /api/locations/{locationId}/ev-charger/state`, gives the location's Zaptec charger state right
now: online, state, power, the charge's kWh, plugged in at, charging since, last check-in, and when it was read. It
is added to the existing `EvChargers` slice, and nothing is stored.

1. **Read (research R1, R4).**
   - `IEvChargerClient` gains `GetStateAsync`. It calls Zaptec's `GET api/chargers/{id}/state` with spec 016's shared
     token.
   - It keeps only the six state ids the answer needs (FR-006).
   - It gives up after 3 seconds and doesn't wait and retry after a 429.
2. **Share (research R3).**
   - A singleton `ChargerStateCache` keeps one read per charger: 3 minutes after a good read and 1 minute after a
     failed one.
   - Callers who ask while a read is running wait for that read.
   - With nobody asking, Zaptec isn't called.
3. **Work out the answer (research R2).** The pure `ChargerLiveState.From` runs on every request with the current
   time. It applies the offline rule (flag 0, or no check-in for 10 minutes), power only while charging, and kWh
   reset at a new session.
4. **Answer (research R5, R6).**
   - Access is the same as for the session list.
   - A location without a charger gets `200 noCharger` with no Zaptec call.
   - Zaptec failures get `503`/`502`, as in spec 016 and the water heater state.

Before any code: [quickstart.md](quickstart.md) §0 reads the state by hand, filtered. It checks the field names and
timestamp format, and what session kWh (553) holds right after a new car is plugged in.

## Technical Context

**Language/Version**: C# 14 / .NET 10, ASP.NET Core 10

**Primary Dependencies**: FastEndpoints, custom CQRS with `Cqrs.SourceGenerator`, and spec 016's typed
`HttpClient` for Zaptec. **No new packages.**

**Storage**: none. No entity, column or migration. The charger comes from spec 016's `EvCharger` row. The shared
answer lives in memory and is lost on restart.

**Testing**: xUnit + NSubstitute in `tests/Features.Tests/EvChargers`:
- **Pure tests:** `ChargerLiveState`.
- **Cache tests:** `ChargerStateCache`, with a manual `TimeProvider` and a substituted client.
- **Client tests:** `ZaptecStubHandler` with a new state path and a canned sample from quickstart §0.
- **Handler tests:** substituted repositories, client and cache.
- **Log test:** `NoSecretsInLogsTests` extended to the state call.

**Target Platform**: the Linux container on the Hetzner VPS. No new configuration: it uses spec 016's `Zaptec`
login.

**Project Type**: web-service (API). The Car tab and the Now page line are the frontend's spec 017.

**Performance Goals**:
- **Answer time:** under 100 ms from the shared answer, under 1 s at p95 when Zaptec is read, and never more than
  about 3 s (SC-004).
- **Zaptec reads:** at most 20 an hour per charger while Zaptec answers (SC-002), and none when nobody asks (SC-003).

**Constraints**:
- **No retry after a rate limit** for this call (FR-003).
- **Spec 016 unaffected:** its reads keep their 15 s timeout and their 429 retry.
- **No extra values passed on:** none beyond the six ids (FR-006).
- **No secrets logged:** no login, token, URL or charger id in any log (FR-007).
- **Never counted:** the live figures never reach cost, bill or Norgespris (FR-004).

**Scale/Scope**: one household and one charger.
- **Application:** 2 records and a pure `ChargerLiveState`; 1 method on `IEvChargerClient`.
- **Infrastructure:** 1 client method, plus a `retryBusy` argument on `GetJsonAsync`.
- **Features:** 1 query, 1 handler, 1 endpoint, 1 validator, 1 mapper method, the cache, 2 log events and 1 DI line.
- **Tests:** about 5 test classes, new or extended.
- **Docs:** the contract, a short EV charger note in `DevelopmentGuide.md` (it has none yet), and a frontend
  handover note in `specs/_backlog/`.

## Constitution Check

_GATE: Must pass before Phase 0 research. Re-check after Phase 1 design._

| Principle | How the plan meets it | Status |
|---|---|---|
| I. Clean layering | **Application:** `ZaptecChargerState`, `ChargerLiveState` and the `IEvChargerClient` method. **Infrastructure:** the HTTP call and the parsing. **Features:** the cache, the handler and the endpoint. Domain is unchanged. | ✅ |
| II. Vertical slices | Everything is in `Features/EvChargers`. No other slice reads the live state. | ✅ |
| III. Results over exceptions | `GetStateAsync` returns `Result<T>` for HTTP, timeout and parsing failures. The cache holds the `Result`, failure included. The handler returns `Result<T>`. | ✅ |
| IV. Validate at the boundary | The only input is the route's `locationId`. `GetChargerStateValidator` checks that it isn't empty, as every other EvCharger request does, and the handler doesn't check it again. Access is a business rule and is checked in the handler as a `Result`, as for the session list. | ✅ |
| V. CQRS | `GetChargerStateQuery` reads and stores nothing. Filling an in-memory cache of an external read isn't application state, the same as `ZaptecTokenCache`. Its handler is registered by the generator. | ✅ |
| VI. Logging | `LoggerMessage` events 1930 and 1931 in `Features/EvChargers/Logging/LogMessages.cs`, inside EvCharger's 1900–1999. The client reuses 1920–1924 with the call `state`. They log the location id and reason codes only. | ✅ |
| VII. Manual mapping | `EvChargerMapper.ToStateResponse`. | ✅ |
| VIII. Minimal footprint | There is no Service Bus, no background poller, no new configuration key, no new `HttpClient` and no new package. The 3-minute, 1-minute and 10-minute times are constants, not settings. | ✅ |
| Stack: migrations only | No schema change. | ✅ |

**Re-check after Phase 1**: there are no violations, so Complexity Tracking stays empty.

## Project Structure

### Documentation (this feature)

```text
specs/021-ev-charger-live/
├── plan.md              # This file
├── research.md          # Phase 0: R1–R8
├── data-model.md        # Phase 1: types, rules, cache, interface change
├── quickstart.md        # Phase 1: read Zaptec by hand, then check end to end
├── contracts/
│   └── endpoints.md     # Phase 1: the new endpoint
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/Application/
├── Common/Interfaces/External/IEvChargerClient.cs   # + GetStateAsync
└── EvChargers/
    ├── EvChargerModels.cs          # + ZaptecStateValue<T>, ZaptecChargerState
    └── ChargerLiveState.cs         # new: pure rules (research R2)

src/Infrastructure/External/
└── ZaptecEvChargerClient.cs        # + GetStateAsync (3 s, no 429 retry, keep 6 ids); GetJsonAsync(retryBusy)

src/Features/EvChargers/
├── Queries/EvChargerQueries.cs     # + GetChargerStateQuery, ChargerStateResponse
├── Handlers/GetChargerStateQueryHandler.cs   # new
├── Validators/EvChargerValidators.cs # + GetChargerStateValidator (LocationId not empty)
├── Endpoints/EvChargerEndpoints.cs # + GetChargerStateEndpoint
├── Mappers/EvChargerMapper.cs      # + ToStateResponse
├── Services/ChargerStateCache.cs   # new: singleton, single flight, 3 min / 1 min
└── Logging/LogMessages.cs          # + 1930 EvChargerStateRead, 1931 EvChargerStateFailed
src/Features/ServiceCollectionExtensions.cs   # + AddSingleton<ChargerStateCache>

tests/Features.Tests/EvChargers/
├── ChargerLiveStateTests.cs        # new
├── ChargerStateCacheTests.cs       # new
├── GetChargerStateHandlerTests.cs  # new
├── ZaptecEvChargerClientTests.cs   # + state parsing, 429, timeout
├── ZaptecSamples.cs, ZaptecStubHandler.cs    # + state sample and path
└── NoSecretsInLogsTests.cs         # + state call

DevelopmentGuide.md                 # + a short EV charger note: the state endpoint, its shared answer, no 429 retry
specs/_backlog/ev-charger-live-frontend.md    # new: handover for the frontend's spec 017
```

**Structure Decision**: The live state belongs to the charger connected in spec 016, so it goes in the existing
`Features/EvChargers` slice, next to the session list. The cache is in that slice's `Services`, because only this
slice uses it. The Zaptec call goes on the existing client, so the token and the no-logging setup stay in one place.

## Complexity Tracking

No violations to justify.
