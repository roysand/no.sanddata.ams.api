# Contracts: Estimated Bill for the Month

All amounts are NOK, full precision (the client rounds). JSON is camelCase.

## GET /api/electricity-cost/bill (new)

Auth: JWT. The caller must be linked to the location.

| Query | Rule |
|---|---|
| `locationId` | Required GUID |
| `month` | Optional `yyyy-MM`; the current Oslo month when omitted |

Responses:

| Status | When | Error code |
|---|---|---|
| 200 | Bill returned | |
| 400 | `month` not `yyyy-MM` | `Validation.InvalidMonth` |
| 400 | The month has not started | `Bill.MonthNotStarted` |
| 401 | Not signed in | |
| 404 | Unknown location, or the caller is not linked to it | `Location.NotFound` |

200 body (current month of a Norgespris holiday home, shortened):

```json
{
  "month": "2026-11",
  "status": "Estimate",
  "pricingModel": "NorgesPris",
  "isComplete": true,
  "hours": { "inMonth": 720, "passed": 420, "withReadings": 420, "priced": 420 },
  "projection": {
    "fromUtc": "2026-11-18T11:00:00Z",
    "hours": 300,
    "kwh": 95.2,
    "basedOnHours": 168,
    "basedOnDays": 7.0,
    "assumedSpotPricePerKwh": 0.9134
  },
  "capacity": { "provisional": true, "averageKw": 6.1, "step": { "lowerKw": 5, "upperKw": 10 } },
  "strom": {
    "name": "Strom",
    "lines": [
      {
        "code": "SupplierMonthlyFee", "unit": "Month", "quantity": 1, "unitPrice": 34.00, "vatRate": 0.25,
        "set": true, "complete": true,
        "soFar": 34.00, "projected": 0, "amount": 34.00, "amountExclVat": 27.20, "vat": 6.80
      },
      {
        "code": "SpotEnergy", "unit": "kWh", "quantity": 240.31, "unitPrice": 1.5552, "vatRate": 0.25,
        "set": true, "complete": true,
        "soFar": 210.10, "projected": 163.63, "amount": 373.73, "amountExclVat": 298.98, "vat": 74.75
      }
    ],
    "soFar": 0, "projected": 0, "amount": 0, "amountExclVat": 0, "vat": 0
  },
  "nettleie": {
    "name": "Nettleie",
    "lines": [
      {
        "code": "NorgesPrisCredit", "unit": "kWh", "quantity": 240.31, "unitPrice": -1.0552, "vatRate": 0,
        "set": true, "complete": true,
        "soFar": -141.80, "projected": -111.78, "amount": -253.58, "amountExclVat": -253.58, "vat": 0
      }
    ],
    "soFar": 0, "projected": 0, "amount": 0, "amountExclVat": 0, "vat": 0
  },
  "total": { "soFar": 0, "projected": 0, "amount": 673.54, "amountExclVat": 488.11, "vat": 185.43 }
}
```

(Numbers are illustrative; part sums shortened to 0.)

Line codes, in this order: Strøm — `SupplierMonthlyFee`, `SpotEnergy`, `SupplierMarkup`; Nettleie — `GridEnergyDay`,
`GridEnergyNight`, `CapacityCharge`, `ElectricityTax`, `EnovaFee`, `NorgesPrisCredit` (Norgespris locations only).

A line with `set: false` has `unitPrice`, `soFar`, `projected`, `amount`, `amountExclVat` and `vat` null and is left
out of its part and the total. A finished month has `status: "Final"`, `projection: null`, `projected: 0` everywhere
and `capacity.provisional: false`.

Field meanings: [data-model.md](../data-model.md).

## PUT /api/admin/location-pricing/{locationId} (changed)

Request gains an optional field:

| Field | Rule |
|---|---|
| `supplierMonthlyFee` | Optional decimal, NOK per month excluding VAT, 0–10,000. Omitted keeps the current value. |

400 `Validation.InvalidMonthlyFee` when out of range. The description now says the markup is added on all kWh,
including Norgespris.

## GET /api/admin/location-pricing (changed)

Each row gains `supplierMonthlyFee` (null when not set). `isComplete` is true only when a grid tariff **and** the
monthly fee are set.

## Existing cost endpoints (values change, shape unchanged)

`/api/electricity-cost/hourly`, `/daily`, `/current`, `/prices`, `/saving`: for a Norgespris location with a markup,
`parts.markup` on Norgespris terms is now the markup (was 0) and the totals rise by markup × (1 + VAT) per kWh. The
saving no longer contains the markup.
