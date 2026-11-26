# Quickstart: High Demand on and off

## 1. Tests

```bash
dotnet test tests/Features.Tests/Features.Tests.csproj --filter "FullyQualifiedName~HighDemand"
```

What they cover:
- **The client** (`OsoWaterHeaterClientTests`, with `OsoStubHandler`):
  - on sends `POST` and off sends `DELETE` to `1/Device/{id}/HighDemand/`, with the key header and no body;
  - 2xx is success;
  - 401 and 403 give `Rejected`, and 404 gives `HighDemandNotOffered`;
  - 5xx, 429 and a timeout give `Unavailable`;
  - exactly one request each time, with no retry.
- **The handler:**
  - the location is missing, or no heater is connected;
  - the key can't be read;
  - a failed switch, which doesn't read the state;
  - a successful switch with the state read back, and one where the read-back fails (`state: null`);
  - the 1807 and 1808 log events.
- **HTTP** (`WaterHeaterHighDemandHttpTests`): 401 without a token, 403 for a user who isn't an Admin, the JSON
  shape, and the errors' status codes.
- **Logs** (`NoSecretsInLogsTests`): no key and no device id in any log line of a switch, failed or not.

## 2. Locally

Run the API with a database where Hjemme has the heater connected (spec 011 quickstart §2). Sign in as an Admin:

```bash
time curl -s -X PUT -H "Authorization: Bearer $TOKEN" "$API/api/admin/locations/$LOC/water-heater/high-demand" \
  | jq '{highDemand, mode: .state.specialMode}'
```

`mode` is `Supercharge` with an end time 24 hours ahead. Then turn it off with `-X DELETE`, after which `mode` is
`None`. As a user who isn't an Admin, both answer 403.

## 3. Against the real heater

FR-009, run by Roy after the deploy (research R8). Use the Heater tab's state, or:

```bash
curl -s -H "Authorization: Bearer $TOKEN" "$API/api/locations/$LOC/water-heater" \
  | jq '{readingTime, isHeating, powerKw, setPointCelsius, mode: .specialMode.mode, end: .specialMode.end}'
```

1. **On:** run `PUT` with `time` before `curl`, as in §2. Note the time it took (SC-003: under 5 s). Read the state
   every 20 seconds and note when it shows `Supercharge` (SC-001: within 1 minute). Within a few minutes,
   `setPointCelsius` is 72 and `isHeating` is true, unless the tank is already at 72 °C. Note what the OSO app shows.
   - **Again while on:** run `PUT` once more and note whether `end` moves, meaning OSO restarts the 24 hours (US1
     scenario 3).
2. **Off while heating:** while the tank is still below 72 °C, run `DELETE`, timed as in step 1. Note when the state
   shows `None` (SC-002: within 1 minute). Within a few minutes, `setPointCelsius` is back to 60. Within the hour, the
   heater stops heating unless the tank is below 60 °C. The Heater tab's power chart (spec 017) shows when it
   stopped. Note what the OSO app shows, and again after restarting the app.
3. **Repeat** steps 1 and 2 until on and off have each been done 3 times (SC-001, SC-002). After the first round
   only the timings are needed; the hour-long check of step 2 is done once.
4. **Record** in research.md (R8) and the spec:
   - the answer times and how long the state took to change, per try;
   - whether `end` moved on the second `PUT`;
   - the set points;
   - when the heating stopped;
   - what the OSO app showed.

   If step 2 fails, the frontend hides the switch.

## 4. Production logs

```kusto
traces
| where timestamp > ago(1d)
| where tostring(customDimensions.EventName) in ("WaterHeaterHighDemandSwitched", "WaterHeaterHighDemandFailed")
| project timestamp, name = tostring(customDimensions.EventName), on = tostring(customDimensions.On),
          reason = tostring(customDimensions.Reason)
```

There is one line per switch, and none holds the key or the device id.
