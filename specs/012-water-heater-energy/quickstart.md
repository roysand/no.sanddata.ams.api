# Quickstart: Check the Water Heater's kWh and Cost

## Prerequisites

- .NET 10 SDK; the API running locally against a copy of the production database, or the deployed API.
- Hjemme has heater samples from 2026-11-19 onwards (spec 011). An Admin, owner or viewer token for Hjemme.

## 1. Automated checks

```sh
dotnet build && dotnet test && dotnet format --verify-no-changes
```

Expected: everything passes, with no new warnings. The new tests cover:
- `WaterHeaterShareTests`:
  - share of cost, including a Norgespris-split hour
  - capping
  - `null` cost without a price, and an hour with 0 kWh
  - day and month sums, with `null` when any hour lacks a cost
  - empty input giving `null`
- `CostHandlerTests`:
  - hourly and daily responses with heater rows (fields filled)
  - without heater rows (`waterHeater: null`)
  - existing fields unchanged
- `MonthlyBillHandlerTests`:
  - an Estimate month: the heater part covers `soFar` only, `estimate: false`, and the total is unchanged
  - a month without samples: `null`

## 2. The SQL against real samples (manual, FR-001, SC-002)

On the production copy, pick an hour when the heater was clearly heating, then an hour that has a gap.

1. List the raw samples of the hour plus the 2 minutes before it (`SELECT "SampledAtUtc", "PowerKw" FROM
   "WaterHeaterPowerSample" WHERE "LocationId" = … ORDER BY 1`).
2. Work out by hand: each sample × min(gap to the next sample, 120 s), split at the hour boundary.
3. Call `GET /api/electricity-cost/hourly?locationId=…&from=…&to=…`. Check that `waterHeater.kwh` matches the hand sum
   to 0,001 kWh, and that `sampledMinutes` is 60 for the full hour and lower for the hour with the gap.
4. Check the hours on each side of a boundary where one hold crosses it: the two parts add up to the whole hold.

## 3. Days, the bill and speed (manual, SC-001, SC-004, SC-005)

1. `GET /daily` for 2026-11-20: `waterHeater.kwh` and `cost` equal the sums of that day's hourly parts.
2. `GET /bill?month=2026-11`: `waterHeater.cost` equals the sum of the month's hourly heater costs, and
   `strom`, `nettleie` and `total` are identical to those on `main`.
3. Time `GET /daily` for a whole year (as the frontend's year view asks), on `main` and on this branch. This branch
   must take no more than 1,5 × as long. If it doesn't, fall back to research R2's stored rollup.

## 4. No heater

For a location without a heater, the hourly, daily and bill responses have `waterHeater: null`, and every other field
equals `main`.
