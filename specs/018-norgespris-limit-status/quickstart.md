# Quickstart: Checking the Norgespris Limit Status and the Spot Energy Price

How to prove the feature works. For the shapes, see [contracts/endpoints.md](contracts/endpoints.md); for the rules,
[research.md](research.md).

## 1. Unit and HTTP tests

```bash
dotnet test tests/Features.Tests --filter "FullyQualifiedName~NorgesPrisStatus|FullyQualifiedName~DayPrices|FullyQualifiedName~EvChargerCost|FullyQualifiedName~MonthlyBill"
```

They cover:
- **Status handler**, with substituted repositories:
  - only active Norgespris locations of the user's list; none gives `items: []`;
  - holiday-home and home limits; `kwhSoFar` from the month start to now, the running hour included;
  - the car added only with the setting on;
  - `projectedKwh` equal to the bill's `totalKwh + projection.kwh` for the same data; null without readings;
  - over the limit at exactly the limit, and by how much;
  - the Norgespris price including VAT, and without VAT for an exempt location; a rate starting tomorrow not used;
  - `resetsAt` and `month` at the turn of a month and across the October clock change.
- **Car in the cap (FR-004)**: with the setting on, an hour after the car's kWh passed the limit is priced on spot
  terms in the hourly costs, the prices, the current hour and the bill's projection; with it off, the costs are as
  before.
- **Prices**: `spotEnergyPricePerKwh` = price × rate × (1 + VAT); no VAT when exempt; null without a price; negative
  stays negative; the other two fields unchanged.
- **HTTP**: 401 without a token; 200 with the shape above.

Then the whole suite and the checks the constitution asks for:

```bash
dotnet test
dotnet format --verify-no-changes
dotnet build
```

## 2. Against the local database

Sign in as Roy (`POST /api/auth/login`) and set `TOKEN`, `API` (`http://localhost:5231`) and the cabin's id `CABIN`.

```bash
curl -s -H "Authorization: Bearer $TOKEN" "$API/api/electricity-cost/norgespris-status" | jq
curl -s -H "Authorization: Bearer $TOKEN" "$API/api/electricity-cost/bill?locationId=$CABIN" \
  | jq '{totalKwh, projected: .projection.kwh}'
```

Expected:
- The cabin with `limitKwh` 1000 and Hjemme with 5000 (if both are on Norgespris locally).
- For each location, `projectedKwh` = the bill's `totalKwh + projected` within 0,01 kWh (SC-001).
- `norgesPrisPricePerKwh` = today's rate on `GET /api/admin/norgespris` × 1,25.
- `resetsAt` = `2026-11-30T23:00:00Z` in November.

```bash
curl -s -H "Authorization: Bearer $TOKEN" \
  "$API/api/electricity-cost/prices?locationId=$CABIN&date=2026-11-23" | jq '.items[:3]'
```

Expected: every hour with a published price has `spotEnergyPricePerKwh` (SC-002), the same for the cabin under or
over its limit.

## 3. Access

- As a viewer of the cabin only: only the cabin is listed.
- Without a token: 401.

## 4. In production (SC-003, SC-004)

After the deploy, time the status for Roy (3 locations) a few times:

```bash
for i in $(seq 20); do curl -s -o /dev/null -w '%{time_total}\n' \
  -H "Authorization: Bearer $TOKEN" "$API/api/electricity-cost/norgespris-status"; done | sort -n | tail -2
```

The 95th percentile (the second highest of 20) should be under 0,3 s. For the cabin over its limit, `overLimit` is
true with `overLimitKwh`, and tomorrow's prices have `spotEnergyPricePerKwh` for every published hour.
