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

`saving` is spot-terms cost minus Norgespris-terms cost summed over the counted hours and can be negative (an extra cost). An hour with consumption but no spot price or exchange rate is left out, so `hoursCounted <= hoursWithConsumption`. The Norgespris monthly cap is not applied (phase 2).

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
