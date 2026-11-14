# Contract: Norgespris rate endpoints

All three: JWT Bearer, `Roles(Admin)`. `401` without a token, `403` for a signed-in non-Admin. Errors use the project's
usual FastEndpoints error body (`errors` keyed by the error code/field). This is also the contract the frontend page is
built against.

## `GET /api/admin/norgespris`

All rates, ordered by `validFrom` ascending.

**200**

```json
[
  { "id": "…", "validFrom": "2020-01-01", "ratePerKwh": 0.4000, "taxPerKwh": 0.0000, "totalPerKwh": 0.4000, "status": "InEffect" },
  { "id": "…", "validFrom": "2027-01-01", "ratePerKwh": 0.5000, "taxPerKwh": 0.0000, "totalPerKwh": 0.5000, "status": "Upcoming" }
]
```

`status` is `Superseded`, `InEffect` (exactly one) or `Upcoming`, computed from today's date in Norway. Only `Upcoming`
rows can be deleted.

## `POST /api/admin/norgespris`

```json
{ "validFrom": "2027-01-01", "ratePerKwh": 0.5000, "taxPerKwh": 0, "confirmPastDate": false }
```

| Status | When |
|---|---|
| `201` + the created rate (same shape as a list item), `Location: /api/admin/norgespris` | saved |
| `400` | `validFrom` missing or not a date; `ratePerKwh`/`taxPerKwh` missing, negative, above 100, or with more than 4 decimals |
| `409` `NorgesPris.DateExists` | another rate already starts on that date |
| `409` `NorgesPris.NotNewest` | `validFrom` is today or earlier and not later than every existing start date |
| `409` `NorgesPris.ConfirmationRequired` | `validFrom` is today or earlier and `confirmPastDate` is not `true` |

`confirmPastDate` is optional and defaults to `false`. A `validFrom` of today or earlier is accepted only when it is the
newest rate **and** `confirmPastDate` is `true`; checks run in the order `DateExists`, `NotNewest`,
`ConfirmationRequired`. The frontend must show "costs shown for past hours will be recalculated" and send `true` only
after the Admin accepts. A future `validFrom` ignores the flag.

## `DELETE /api/admin/norgespris/{id}`

| Status | When |
|---|---|
| `204` | deleted (it had not started) |
| `404` `NorgesPris.NotFound` | no such rate |
| `409` `NorgesPris.AlreadyStarted` | the rate starts today or earlier (this includes the one in effect and the seeded one) |

## Unchanged endpoints whose numbers now depend on date

`GET /api/electricity-cost/current`, `/hourly`, `/daily` and `/prices` keep their shapes. (`GET /api/consumption` has no
price in it and is unaffected.)
Only the flat rate they use now follows the table above.

## Frontend notes (for the separate repository)

- Table columns: start date, rate, tax, total; highlight `InEffect`; mark `Upcoming`; delete action only on `Upcoming`.
- Add dialog: start date, rate, tax. If the chosen date is today or earlier, show the recalculation warning and require
  a confirm, then send `confirmPastDate: true`. Show the server's `409` messages in the dialog and keep it open.
- Admin-only route and menu entry next to `/admin/users`; absent for regular users.
