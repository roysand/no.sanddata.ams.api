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

**The pairing rule.** Seed one hour with these samples, one a minute at 0 kW unless said otherwise:

| Samples | Litres | Expected used |
|---|---|---|
| 10:00–10:05 | 300 every minute | 0 (sampled, nothing used) |
| 10:06–10:09 | 300, 291, 282, 273 | 27 |
| 10:10–10:13 at 3 kW | 273, 266, 259, 252 | 21 + 3 × (3 / 60 / 0.035) ≈ 25.3 |
| 10:14–10:20 at 3 kW | rising by 1 a minute | 0 |
| 10:21–10:30 | falling by 1 a minute | 0 (below `MinFallLitres`) |
| none 10:31–10:40, then 10:41 | 250 | 0, and 10:31–10:41 is not sampled |

```bash
curl -s -H "Authorization: Bearer $TOKEN" \
  "$API/api/locations/$LOC/water-heater/hot-water?period=day&date=$DAY" | jq '.items[] | select(.litres != null)'
```

The hour shows about 52 litres and 30 sampled minutes. The 10:31–10:41 gap counts for neither.

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

   - **The litres fall almost every minute while the tap runs:** keep the pairing rule.
   - **They fall in steps every few minutes:** switch to run pairing (research R6, point 1).
2. **`MinFallLitres`.** For three nights, 01:00–05:00 Oslo time, with no water drawn, read `?period=day`. Do this once
   with `MinFallLitres` = 1 and once with 2. Pick the smallest value that keeps every quiet hour at 2 L or less
   (SC-003).
3. **`KwhPerLitre`.**
   - At three moments, read `GET /api/locations/{id}/water-heater` and note `storedEnergyKwh ÷ availableLitres40`.
   - For each of the days, compare `totalLitres` with the OSO app's "Total v40 used" (Day view).
   - SC-001 is met if at least 4 of 5 days are within 15 %. If most days miss in the same direction, adjust
     `KwhPerLitre` toward OSO's ratio and check again.
4. **The power correction.** Take a shower while the heater is heating (`isHeating` true on the state endpoint). Note
   its length and the shower's flow (about 8–10 L a minute of 40 °C water).
   - The hour's `litres` should be within 20 % of length × flow (SC-002).
   - Recount the same pairs without the `heated` term, by hand from the samples listed as in step 1. That figure
     should be at least 10 % lower.

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
