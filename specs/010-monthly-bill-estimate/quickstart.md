# Quickstart: validating the monthly bill

Prerequisites: PostgreSQL running, `src/api/local.settings.json`, an Admin account, a Norgespris holiday home with
readings, the spot prices and exchange rates of the months checked, and the rates of spec 008 (grid tariff with
capacity steps, taxes, VAT, Norgespris rate, cap). Contract: [contracts/endpoints.md](contracts/endpoints.md). Rules:
[data-model.md](data-model.md), [research.md](research.md). Check on a scratch copy of the database first.

## 1. Build, test, migrate

```bash
dotnet build
dotnet format --verify-no-changes
dotnet test
dotnet ef database update --project src/Infrastructure/Infrastructure.csproj --startup-project src/api/api.csproj
```

Expected: tests pass (including the synthetic July 2026 test, SC-001); the migration adds the nullable
`SupplierMonthlyFee` column to `Location`.

## 2. Markup under Norgespris (US1)

Request `GET /api/electricity-cost/hourly` for an hour of the Norgespris location with markup 0.03192. Expected:
`actual.parts.markup` = 0.03192 and `actual.ratePerKwh` = 1.25 × (0.40 + 0.03192 + grid + 0.0813). The saving
(`/saving`) for a day equals Σ kWh × (spot − 0.40) × 1.25 for hours below the cap.

## 3. Set the fee (US4)

`GET /api/admin/location-pricing` shows the location with `supplierMonthlyFee: null`, `isComplete: false`.
`PUT /api/admin/location-pricing/{id}` with `{ "gridTariffId": "...", "supplierMarkupPerKwh": 0.03192,
"supplierMonthlyFee": 27.20 }` → `isComplete: true`. `-1` → 400. As a regular user → 403.

## 4. Finished month (US2)

`GET /api/electricity-cost/bill?locationId={id}&month=2026-10`. Expected: `status: "Final"`, `projection: null`,
every line's `amount = amountExclVat + vat`, each part = sum of its lines, total = Strøm + Nettleie. Compare the
Nettleie lines with the October 2026 Elvia bill in spec 008 ("Reference data for the tests").

## 5. Current month (US3)

`GET /api/electricity-cost/bill?locationId={id}` (no month). Expected: `status: "Estimate"`, `projection.hours`
= hours from the current Oslo hour to the month end, `basedOnDays` ≈ 7, `capacity.provisional: true`, each line
`amount = soFar + projected`. Next month → 400 `Bill.MonthNotStarted`. Another user's location → 404.

## 6. July 2026 against the invoice (SC-001)

Confirm first that July 2026 readings, spot prices, exchange rates and the rates valid in July are in the database,
and that markup 0.03192 and fee 27.20 are set. Then `GET /api/electricity-cost/bill?locationId={id}&month=2026-07`
and compare with the invoice table in the spec:

| Check | Expected | Tolerance |
|---|---|---|
| `total.amount` | 673.55 | ± 0.10 |
| Fee, markup, grid day/night, capacity, tax, Enova lines | invoice line | ± 0.02 each |
| `SpotEnergy`, `NorgesPrisCredit`, Strøm and Nettleie subtotals | 373.74, −253.58, 417.33, 256.22 | ± 1.00 each |
| `SpotEnergy + NorgesPrisCredit` | 120.16 | ± 0.02 |
| Quantities `GridEnergyDay` / `GridEnergyNight` | 95.58 / 144.73 kWh | exact to 0.01 |

A gap larger than the tolerance in the kWh quantities points at missing readings (`hours.withReadings` <
`hours.inMonth`); in the spot lines only, at a different spot source or exchange rate.
