# Contract: price endpoints

All new endpoints: JWT Bearer. The Pricing endpoints are Admin only (`401` without a token, `403` for a signed-in non-Admin). The saving endpoint is for any signed-in user linked to the location. Error bodies carry the message and the status, not the error code, like the other endpoints. Amounts are NOK per kWh unless said otherwise.

## Changed: existing cost endpoints

`GET /api/electricity-cost/current`, `/hourly`, `/daily` and `/prices` keep their shapes. **`ratePerKwh` and `cost` are now the total including VAT** (before: energy only, excluding VAT). Nothing else about the old fields changes.

Additive: each `actual` and `comparison` object in `/hourly` and `/current` gets `parts`:

```json
"actual": {
  "ratePerKwh": 1.5016, "cost": 3.0032,
  "parts": { "energy": 0.8000, "markup": 0.0200, "grid": 0.3000, "taxes": 0.0813, "vat": 0.3003, "total": 1.5016, "gridSet": true }
}
```

`grid` is `null` and `gridSet` is `false` when the location has no tariff (or the tariff has no rate for the hour); the total is then without a grid part. The parts add up exactly to `total`. `/daily` and `/prices` stay totals only. As before, an hour that cannot be priced has `actual` or `comparison` equal to `null`.

## New: `GET /api/electricity-cost/saving?locationId={id}`

What Norgespris saved compared with spot terms, per period (Oslo calendar days). For a spot location it is what the agreement would have saved. Needs the user to be linked to the location (`404` otherwise).

**200**

```json
{
  "periods": [
    { "period": "Today", "from": "2026-11-16", "to": "2026-11-16", "saving": 1.23, "hoursCounted": 14, "hoursWithConsumption": 14 },
    { "period": "Last7Days", "from": "2026-11-10", "to": "2026-11-16", "saving": -4.50, "hoursCounted": 160, "hoursWithConsumption": 162 },
    { "period": "MonthToDate", "from": "2026-11-01", "to": "2026-11-16", "saving": 20.10, "hoursCounted": 380, "hoursWithConsumption": 380 },
    { "period": "YearToDate", "from": "2026-01-01", "to": "2026-11-16", "saving": 310.00, "hoursCounted": 2100, "hoursWithConsumption": 2110 }
  ]
}
```

`saving` is spot-terms cost minus Norgespris-terms cost summed over the counted hours and can be negative (an extra cost). An hour with consumption but no spot price or exchange rate is left out, so `hoursCounted <= hoursWithConsumption`. The Norgespris monthly cap is applied (addendum, story 6): kWh above the cap add nothing to the saving.

## New: location pricing (Admin)

### `GET /api/admin/location-pricing`

```json
[ { "locationId": "...", "name": "Home", "gridTariffId": null, "gridTariffName": null, "supplierMarkupPerKwh": 0.0000, "isComplete": false } ]
```

`isComplete` is true when a grid tariff is set.

### `PUT /api/admin/location-pricing/{locationId}`

`{ "gridTariffId": "..." | null, "supplierMarkupPerKwh": 0.0200 }` returns `200` with the list item. `400` for a markup that is negative, above 100, or has more than 4 decimals; `404` for an unknown location or tariff. Changing it recalculates past hours.

## New: national price rates (Admin)

`kind` is a string, `ElectricityTax`, `EnovaFee` or `Vat` (case-insensitive on input, returned as written here; enums are not serialized as text by default in this API, so the request and response records use strings and the validator parses them). `value` is NOK per kWh for the fees and a fraction for VAT (`0.25`).

| Call | Body | Answers |
|---|---|---|
| `GET /api/admin/price-rates?kind={kind}` | none (kind optional) | `200` list, oldest first: `{ id, kind, validFrom, value, status }` with status `Superseded`, `InEffect` or `Upcoming` |
| `POST /api/admin/price-rates` | `{ kind, validFrom, value, confirmPastDate }` | `201` the row; `400` invalid; `409` `DateExists`, `NotNewest`, `ConfirmationRequired` |
| `DELETE /api/admin/price-rates/{id}` | none | `204`; `404` unknown; `409` already started |

## New: grid tariffs (Admin)

Rates are NOK per kWh excluding taxes and VAT (convert a published rate that includes them before entering it).

| Call | Body | Answers |
|---|---|---|
| `GET /api/admin/grid-tariffs` | none | `200` `[ { id, name, rates: [ { id, validFrom, dayRatePerKwh, nightRatePerKwh, status } ] } ]` |
| `POST /api/admin/grid-tariffs` | `{ name }` | `201` the tariff; `400` empty or over 100 characters; `409` name taken |
| `POST /api/admin/grid-tariffs/{id}/rates` | `{ validFrom, dayRatePerKwh, nightRatePerKwh, confirmPastDate }` | `201` the rate; `400`; `404` unknown tariff; `409` `DateExists`, `NotNewest`, `ConfirmationRequired` |
| `DELETE /api/admin/grid-tariffs/{id}/rates/{rateId}` | none | `204`; `404`; `409` already started |

`confirmPastDate` is optional (default false) and only matters for a start date of today or earlier. The check order is `DateExists`, `NotNewest`, `ConfirmationRequired`, as for the Norgespris rates.

## Addendum 2026-11-17 (stories 6 and 7)

### Changed: `cap` on a priced hour (`/hourly`, `/current`)

On Norgespris terms, when some kWh of the hour are above the monthly cap, the `actual` (Norgespris location) or the
`comparison` (spot location) gets an additive `cap` object. `parts` are the terms of the kWh within the cap, or the
spot terms when the whole hour is above it.

```json
"actual": {
  "ratePerKwh": 1.2240, "cost": 2.4480,
  "parts": { "energy": 0.4000, "markup": 0.0000, "grid": 0.2899, "taxes": 0.0813, "vat": 0.1928, "total": 0.9640, "gridSet": true },
  "cap": { "kwhWithinCap": 1.2000, "kwhAboveCap": 0.8000,
           "aboveCapParts": { "energy": 0.9000, "markup": 0.0200, "grid": 0.2899, "taxes": 0.0813, "vat": 0.3228, "total": 1.6140, "gridSet": true } }
}
```

`cost` = `kwhWithinCap` x `parts.total` + `kwhAboveCap` x `aboveCapParts.total`; `ratePerKwh` = `cost` / kWh. `cap`
is `null` when nothing is above the cap (the API writes null fields). For an exempt location `taxes` is the Enova fee only (tax exempt) and `vat` is
0 (VAT exempt). `/prices` shows the rate of the next kWh given the month consumption before the hour.

### Changed: location pricing (Admin)

`GET /api/admin/location-pricing` items gain `electricityTaxExempt`, `vatExempt`, `isHolidayHome`.

`PUT /api/admin/location-pricing/{locationId}`:

```json
{ "gridTariffId": "...", "supplierMarkupPerKwh": 0.0200,
  "electricityTaxExempt": false, "vatExempt": false, "isHolidayHome": true }
```

The three flags are optional; an omitted flag keeps its current value, so an existing call with only the first two
fields changes nothing else.

### Changed: national price rates (Admin)

`kind` also accepts `NorgesPrisCapHome` and `NorgesPrisCapHolidayHome`, `value` in kWh per month (`> 0`, `<= 99999`).
Same rules and answers as the other kinds.

### New: capacity steps (Admin, phase 2)

Amounts are NOK per month excluding VAT (Elvia publishes them including VAT: divide by 1.25).

| Call | Body | Answers |
|---|---|---|
| `GET /api/admin/grid-tariffs/{id}/capacity-steps` | none | `200` `[ { validFrom, status, steps: [ { lowerKw, upperKw, monthlyAmount } ] } ]`, oldest first |
| `POST /api/admin/grid-tariffs/{id}/capacity-steps` | `{ validFrom, steps: [ { lowerKw, upperKw, monthlyAmount } ], confirmPastDate }` | `201` the table; `400` steps not starting at 0, not contiguous, not increasing, or top step not last; `404` unknown tariff; `409` `DateExists`, `NotNewest`, `ConfirmationRequired` |
| `DELETE /api/admin/grid-tariffs/{id}/capacity-steps/{validFrom}` | none | `204`; `404`; `409` already started |

Example body (Elvia from 2026-07-01, excluding VAT): `{ "validFrom": "2026-07-01", "steps": [ { "lowerKw": 0,
"upperKw": 2, "monthlyAmount": 120 }, { "lowerKw": 2, "upperKw": 5, "monthlyAmount": 200 }, { "lowerKw": 5, "upperKw":
10, "monthlyAmount": 336 }, ..., { "lowerKw": 100, "upperKw": null, "monthlyAmount": 5440 } ] }`.

### New: `GET /api/electricity-cost/capacity?locationId={id}&month=YYYY-MM` (phase 2)

Signed-in user linked to the location (`404` otherwise). `month` optional, default the current Oslo month; `400` for a
malformed month.

**200**

```json
{
  "month": "2026-10", "provisional": false, "set": true,
  "peaks": [ { "date": "2026-10-14", "hourStartUtc": "2026-10-14T16:00:00Z", "kw": 6.2 },
             { "date": "2026-10-03", "hourStartUtc": "2026-10-03T05:00:00Z", "kw": 5.1 },
             { "date": "2026-10-27", "hourStartUtc": "2026-10-27T17:00:00Z", "kw": 4.9 } ],
  "averageKw": 5.4, "step": { "lowerKw": 5, "upperKw": 10 },
  "amountExclVat": 336.00, "vat": 84.00, "amount": 420.00, "kwToNextStep": 4.6
}
```

`set` false (with the other figures null) when the location has no tariff, the tariff has no step table for the month,
or there is no consumption. `provisional` is true for the current month. Fewer than three days with data: `peaks` has
fewer items and the average is over them.
