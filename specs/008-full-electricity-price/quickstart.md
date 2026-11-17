# Quickstart: validating the full electricity price

Prerequisites: PostgreSQL (TimescaleDB) running, `src/api/local.settings.json`, an Admin account, a location with measurements and some spot prices. Contract: [contracts/endpoints.md](contracts/endpoints.md). Data rules: [data-model.md](data-model.md). Check on a scratch copy of the database, as for spec 007, not on the real one.

## 1. Build, test, migrate

```bash
dotnet build
dotnet test
dotnet ef database update --project src/Infrastructure/Infrastructure.csproj --startup-project src/api/api.csproj
```

Expected: tests pass; the migration creates `PriceRate`, `GridTariff`, `GridTariffRate`, adds the two location columns and seeds the three `PriceRate` rows (VAT 0.25 from 2020-01-01, tax 0.0713 and Enova 0.0100 from 2026-01-01).

## 2. Hand-calculated example (SC-001)

Pick an hour of a spot location with a known spot price and exchange rate, then compute by hand:

```
price = 1.25 x (spot x EURNOK + markup + grid + 0.0713 + 0.0100)
```

Set the location markup and grid tariff first (steps 3 and 4). Request `GET /api/electricity-cost/hourly` for that hour and compare `actual.ratePerKwh` and `actual.parts` with the hand value. The parts must add up to `total`.

## 3. Add a grid tariff and point a location at it

As Admin: `POST /api/admin/grid-tariffs` `{ "name": "Test household" }`, then `POST /api/admin/grid-tariffs/{id}/rates` `{ "validFrom": "2026-01-01", "dayRatePerKwh": 0.30, "nightRatePerKwh": 0.20, "confirmPastDate": true }`, then `PUT /api/admin/location-pricing/{locationId}` `{ "gridTariffId": "...", "supplierMarkupPerKwh": 0.02 }`. `GET /api/admin/location-pricing` shows `isComplete: true`.

## 4. Day, night, weekend, holiday (US1)

Request the hourly cost for: a weekday 14:00 Oslo hour, the same weekday 23:00, a Saturday 14:00, a Sunday 14:00, and a public holiday (for example 1 May, or Good Friday) 14:00. Expected: only the weekday 14:00 hour has `parts.grid` equal to the day rate; the others have the night rate. Check the boundary hours 05:00 (night), 06:00 (day), 21:00 (day) and 22:00 (night) in Oslo time, in both summer and winter time.

## 5. Norgespris terms and the comparison (US1, US2)

For a Norgespris location the `actual` has `parts.markup` 0 and `parts.energy` equal to the Norgespris rate plus tax for the hour; the `comparison` has the markup and the spot energy; `grid`, `taxes` and VAT rate are the same on both sides.

## 6. Dated values (US3)

Add an `ElectricityTax` row starting a few days ahead (`POST /api/admin/price-rates`), then compare an hour before and after: only `parts.taxes` and the VAT on it change. Check the refusals: same date `409`, a date older than the newest `409`, a past date without `confirmPastDate` `409`, delete a started row `409`, delete the future row `204`, a non-Admin `403`.

## 7. Missing tariff (US4)

For a location with no grid tariff the hourly cost still shows, with `parts.grid` null and `parts.gridSet` false, and the location is listed with `isComplete: false`.

## 8. The saving (US5)

`GET /api/electricity-cost/saving?locationId=...`. Check one day by hand: the sum over its hours of (spot-terms cost minus Norgespris-terms cost) equals the `Today` or a chosen day value; `Last7Days` equals the sum of `Today` and the six days before it; `hoursCounted <= hoursWithConsumption`.

## 9. Compare with a real bill (SC-004)

Take one real hour and one real day from your electricity bill and your grid bill and compare the per-kWh lines with the app for the same period. The difference should be explained only by the monthly charges that phase 2 adds.

## 10. Nothing else broke

`/daily` totals equal the sum of the hourly `cost` values; `/prices` shows the total including VAT for each hour; a month of hourly costs responds as fast as before (the number of database queries does not grow with the hours).

## Addendum: stories 6 and 7

Expected after `dotnet ef database update`: the migration `AddExemptionsAndNorgesPrisCap` adds the three location
columns (false), widens `PriceRate.Kind` to 40 characters and adds the two cap rows (5000 and 1000 kWh from
2025-10-01). The `GridCapacityStep` table comes with story 7 in its own migration.

### 11. Reproduce the Elvia bill (SC-004, SC-008)

Enter Elvia's tariff as in the spec's reference data (rates 0.2899 / 0.1699 from 2026-07-01, and the step table), mark
the location as a holiday home. For October 2026, sum the hourly `parts` per day and night kWh and compare with the bill
lines (energiledd day 129.60, night 136.29, elavgift 31.88 + 57.19, Enova 4.47 + 8.03): each within 0.01 kr.

### 12. Exemptions (story 6)

Set `electricityTaxExempt: true` on a test location: `parts.taxes` becomes 0.0100 (Enova only). Set `vatExempt: true`:
`parts.vat` is 0. Clear both: the figures return. An omitted flag in the PUT keeps its value.

### 13. Norgespris cap (story 6)

On a scratch database, lower `NorgesPrisCapHolidayHome` with a new row for the current month (`confirmPastDate: true`)
to a value just above the month's consumption so far, then request `/hourly`: the hour that crosses the cap has `cap`
with both kWh parts and `cost` = within x `parts.total` + above x `aboveCapParts.total`; later hours are on spot terms.
Request a range that starts mid-month: the same hours have the same figures. The saving no longer counts kWh above the
cap.

### 14. Capacity charge (story 7)

Add Elvia's step table to the tariff, then `GET /api/electricity-cost/capacity?locationId=...&month=2026-10`: three peaks
on different days, their average, the step, and for 5-10 kW `amount` 420.00. The current month is `provisional: true`.
A location without a tariff gives `set: false`. Check the Admin refusals as for the other dated values.
