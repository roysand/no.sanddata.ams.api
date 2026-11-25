# Quickstart: Checking Hot Water Used per Hour, Day and Month

How to prove the feature works. For the shapes, see [contracts/endpoints.md](contracts/endpoints.md), and for the
rule, [research.md](research.md) R4–R6.

## 1. Unit and HTTP tests

```bash
dotnet test tests/Features.Tests --filter "FullyQualifiedName~WaterHeater"
```

They cover:

**Client:** `GetSampleAsync`
- power and litres read together;
- `Status` failing → power kept, litres `null`, `LitresError` set;
- `ActualLoad` failing → the sample fails;
- a missing or negative `availableMix40Volume` → litres `null`.

**Sampler:**
- the sample row stores the litres;
- a litres failure still inserts the power;
- the litres log is rate-limited (start, hourly, recovered) and apart from the power log.

**Period:** `HotWaterPeriods`
- day, week (an ISO week that crosses a month and a year), month and year bounds in UTC;
- the 23- and 25-hour days (29 March 2026 and 25 October 2026);
- parts after now left out.

**Handler,** with substituted repositories:
- owner, viewer and Admin get the answer; no access and a missing location give the same 404;
- an inactive location answers only to Admins;
- hours rolled up into Oslo days and months;
- rounding;
- `null` for parts without sampled time;
- the total of the rounded parts, and a `null` total.

**HTTP** (`EndpointHost`):
- the JSON shape;
- the defaults (`day`, today in Oslo);
- 400 for a bad `period` or `date`;
- 401 without a token;
- the 404 mapping.

The SQL has no database test harness, as in specs 012 and 017. §3 checks it.

## 2. Format and build

```bash
dotnet format --verify-no-changes
dotnet build
```

No new warnings.

## 3. The SQL on a local database

Apply the migration and insert samples for a location by hand (or let the sampler run against the real heater):

```bash
dotnet ef database update --project src/Infrastructure/Infrastructure.csproj --startup-project src/api/api.csproj
```

**The hourly balance** (research R10). Seed these samples (UTC), one a minute at 0 kW unless said otherwise:

| Samples | Litres | What it shows |
|---|---|---|
| 09:59 | 300 | The start of the 10:00 hour |
| 10:00–10:29 | falling from 300 to 273 | 27 L drawn |
| 10:30–10:59 at 2 kW | rising from 273 to 288 | 1 kWh heated = 25 L, while the litres rose |
| 11:00–11:59 | falling from 288 to 286 | Standby cooling |

```bash
curl -s -H "Authorization: Bearer $TOKEN" \
  "$API/api/locations/$LOC/water-heater/hot-water?period=day&date=$DAY" | jq '.items[] | select(.litres != null)'
```

The 10:00 hour shows 300 − 288 + 1 ÷ 0.04 = 37 litres and 60 sampled minutes. The 11:00 hour shows 0: 2 L is under
`MinHourLitres`. The same arithmetic is unit-tested in `HotWaterBalanceTests`, with Roy's hours of 2026-11-25.

**The other figures are unchanged.** For the same location and day, the `/water-heater/power` and
`/electricity-cost/hourly` answers are the same before and after the migration.

## 4. Calibration against the OSO app

Do this once story 1 has been deployed and stored litres for 3–5 days (research R6). Write the results under
"Calibration results" in research.md.

1. **How often OSO's figure changes.** For a time you know someone showered, list the samples:

   ```sql
   SELECT "SampledAtUtc", "PowerKw", "AvailableLitres40"
   FROM public."WaterHeaterPowerSample"
   WHERE "LocationId" = :loc AND "SampledAtUtc" BETWEEN :from AND :to
   ORDER BY "SampledAtUtc";
   ```

   - Done on 2026-11-25: they change in steps every 2–10 minutes, which led to the hourly balance (research R10).
2. **`MinHourLitres`.** For three nights, 01:00–05:00 Oslo time, with no water drawn, list each hour's litres at
   its start and end and its kWh (the query in step 1, or the power view). The quiet hours' balance before the floor
   shows the idle loss. Keep `MinHourLitres` (3) if the loss stays under it, and raise it if it doesn't (SC-003).
3. **`KwhPerLitre`.**
   - At three moments, read `GET /api/locations/{id}/water-heater` and note `storedEnergyKwh ÷ availableLitres40`.
   - For each of the days, compare `totalLitres` with the OSO app's "Total v40 used" (Day view).
   - SC-001 is met if at least 4 of 5 days are within 15 %. If most days miss in the same direction, adjust
     `KwhPerLitre` toward OSO's ratio and check again.
4. **The power correction.** Take a shower while the heater is heating (`isHeating` true on the state endpoint). Note
   its length and the shower's flow (about 8–10 L a minute of 40 °C water).
   - The hour's `litres` should be within 20 % of length × flow (SC-002).
   - Recount the hour without the `heated` term (start − end only), from the samples listed as in step 1. That
     figure should be at least 10 % lower.

## 5. Production

After deploying:

- **The litres are stored (SC-005).** The share of a day's samples with `AvailableLitres40` should be at least 95 %:

  ```sql
  SELECT count("AvailableLitres40")::decimal / count(*)
  FROM public."WaterHeaterPowerSample"
  WHERE "LocationId" = :loc AND "SampledAtUtc" >= now() - interval '1 day';
  ```

- **No litres-failure log lines (EventIds 1834–1836)** in a normal day.
- **Speed (SC-004).** Time `?period=day` and `?period=year` a few times each from the server:

  ```bash
  curl -o /dev/null -s -w '%{time_total}\n' -H "Authorization: Bearer $TOKEN" "$API/…?period=year"
  ```

  A day should take under 0.3 s and a year under 1 s. If a year is slower, see research R9.
- **The day's chart matches the OSO app.** Compare today's items with the OSO app's Day view: the same hours have
  bars, at about the same heights.
