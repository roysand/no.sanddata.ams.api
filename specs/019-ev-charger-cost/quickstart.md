# Quickstart: Checking the Car Charging Cost on the Bill

How to prove the feature works. For the shape, see [contracts/endpoints.md](contracts/endpoints.md); for the rules,
[research.md](research.md).

## 1. Unit and HTTP tests

```bash
dotnet test tests/Features.Tests --filter "FullyQualifiedName~EvChargerCost|FullyQualifiedName~MonthlyBill"
```

They cover:
- **Setting off:** the cost is Σ car kWh × the Norgespris rate × 1,25; no VAT when exempt; a rate change mid-month
  prices each hour at its own rate; no rate gives null; the house's model and limit don't change it.
- **Setting on:**
  - within the limit, the cost is Σ car kWh × the hour's `ratePerKwh` from the prices handler;
  - car kWh that pass the limit are priced on spot terms, and an hour crossing the limit is split;
  - a charging hour without a house reading is priced;
  - a missing spot price gives null.
- **`pricedAt`:** `HousePrice` or `NorgesPrisPrice` by the setting.
- **Unchanged bill:** the lines, parts, total and projection are the same with and without the car's cost (SC-003).
- **No charging:** `evCharger` stays null.

Then the whole suite and the constitution's checks:

```bash
dotnet test
dotnet format --verify-no-changes
dotnet build
```

## 2. Against the local database or production

Sign in as Roy and read Hjemme's bill:

```bash
curl -s -H "Authorization: Bearer $TOKEN" "$API/api/electricity-cost/bill?locationId=$HJEMME" | jq '.evCharger'
```

Expected:
- **Setting off:** `pricedAt` is `NorgesPrisPrice`, and `cost` is `kwh` × today's rate on `GET /api/admin/norgespris`
  × 1,25, within 0,01 kr if the rate didn't change in the month (SC-001). Check the previous month too, with
  `&month=yyyy-MM`.
- **Setting on** (switch it on the Car tab): `pricedAt` is `HousePrice`. `cost` is the sum over the car's hours of
  kWh × `ratePerKwh` from `/prices` for those days, for a month within the limit (SC-002).
- **Unchanged bill:** `total` and the lines are the same as before the deploy (SC-003).
- **Timing:** the bill answers as fast as before, within 10 % (SC-004).
