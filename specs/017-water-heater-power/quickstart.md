# Quickstart: Checking the Water Heater's Power per 5 Minutes

How to prove the feature works. For the shapes, see [contracts/endpoints.md](contracts/endpoints.md).

## 0. Before the change: save the hourly figures

The hourly SQL is refactored (research R1), so first save what it answers today. Run this against a location with
heater samples (Hjemme), signed in as its owner:

```bash
curl -s -H "Authorization: Bearer $TOKEN" \
  "$API/api/electricity-cost/hourly?locationId=$HJEMME&from=2026-11-18&to=2026-11-20" > hourly-before.json
```

## 1. Unit and HTTP tests

```bash
dotnet test tests/Features.Tests --filter "FullyQualifiedName~WaterHeater"
```

They cover:
- **`PowerWindow`**:
  - defaults: none given, only `from`, only `to`;
  - UTC conversion of an offset time;
  - refusals: `from` not before `to`, more than 48 hours, `from` in the future;
  - exactly 48 hours is allowed.
- **Handler**, with a substituted sample repository and location repository:
  - owner, viewer and Admin get the window;
  - no access and a missing location give the same 404;
  - an inactive location answers only to an Admin;
  - kWh and seconds map to `powerKw` and `sampledSeconds`;
  - a step that rounds to 0 seconds is dropped;
  - no rows give an empty list.
- **HTTP** (`EndpointHost`):
  - the JSON shape;
  - 401 without a token;
  - 400 from the validator;
  - 404 mapping;
  - the query passed on with `IsAdmin`.

The SQL has no database test harness in this repo (as in spec 012). Steps 2–3 check it.

## 2. Locally, against the database

1. Run the API against the local database (`compose.db.yaml`), with the heater sampling for a while.
2. **Default window**: `GET /api/locations/{Hjemme}/water-heater/power`.
   - `to` is now and `from` is 3 hours earlier.
   - Every `start` ends in :00, :05, … :55 with seconds 00.
   - Full steps have `sampledSeconds` 300 (or close to it). The last step has fewer, up to now.
3. **Heating**: turn on the heater (or pick a window when it heated). The steps in that time show about the heater's
   rated power, and the steps around it show standby. Steady heating should be within 2 % (SC-001).
4. **Yesterday**: ask for the same 3 hours yesterday with `from` and `to`. The steps follow yesterday's samples.
5. **Gaps**: stop the API for 10 minutes, start it again, and read the window. The 5-minute steps in the gap have no
   item, and the steps at its edges have fewer `sampledSeconds`.
6. **Refusals**: `from` after `to`, a 49-hour window, and `from` tomorrow each give 400 with a message. A window with
   no samples (a month before the heater was connected) gives `200` and `"items": []`.
7. **Access**: a viewer gets the window. A user without access, and a made-up location id, both get the same 404.
   No token gives 401.

## 3. The hourly figures are unchanged, and the steps add up

1. Read the same hourly range as in step 0 into `hourly-after.json` and compare:
   ```bash
   diff <(jq -S . hourly-before.json) <(jq -S . hourly-after.json)
   ```
   Hours that were complete in step 0 must be identical (FR-006). Only an hour that was still going on can differ.
2. **SC-002**: for one complete hour, read its 5-minute steps and add `powerKw × sampledSeconds / 3600`. The sum
   equals that hour's uncapped heater kWh within 0.001 kWh. In the hourly response, use an hour where the heater
   was not capped at the meter.

## 4. Production

1. Deploy and repeat steps 2.2, 2.4 and 3.2 against Hjemme.
2. **SC-003**: time 20 default-window requests:
   ```bash
   for i in $(seq 20); do
     curl -s -o /dev/null -w "%{time_total}\n" -H "Authorization: Bearer $TOKEN" \
       "$API/api/locations/$HJEMME/water-heater/power"
   done | sort -n | tail -2
   ```
   The 19th value (p95) should be under 0.3 s.
3. **SC-004**: for yesterday (two windows of 24 hours), count the steps with `sampledSeconds` ≥ 240. They should be
   at least 95 % of the 288 steps.

### Results in production (2026-11-20, T022)

Run against Hjemme on `ams-api.sanddata.eu` at about 21:40 UTC, read-only, signed in as the owner.

- **Default window (2.2)**: `from`/`to` were now−3h…now. There were 37 items, every `start` was on whole 5 minutes,
  and the last step was cut at now (286 s). Heating steps showed about 2.03 kW and standby steps 0.012 kW.
- **SC-003**: 20 default-window requests, all `200`. The p95 (19th value) was **0.178 s** and the slowest 0.187 s,
  under 0.3 s. ✅
- **SC-004**: "yesterday" (2026-11-19 Oslo) can't be judged: Hjemme's samples start at 22:05 UTC that day, so only 11
  of its 288 steps exist. Instead, every complete step since sampling began was counted (2026-11-19 22:05 to
  2026-11-20 21:35 UTC, 282 steps). All 282 steps have an item, and 281 have ≥ 240 sampled seconds
  (**99.6 %**). The only short step is the first one (51 s), when sampling began. ✅
- **SC-002 (3.2)**: on 2026-11-20 00:00–21:00 UTC (21 complete hours, none capped), Σ `powerKw × sampledSeconds / 3600`
  matched the hourly response's `waterHeater.kwh` to within **0.00033 kWh** at most, under 0.001 kWh. The difference
  comes from `powerKw` being rounded to 3 decimals. ✅
