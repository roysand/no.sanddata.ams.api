# Backlog: Show the full price, its parts and the Norgespris saving in the UI

Status: idea, not specified yet. Created 2026-11-16 together with `specs/008-full-electricity-price`. Roy decided that spec 008 only
calculates the numbers in the API, and that the UI is updated in a separate feature afterwards. That feature lives in the
frontend repository `no.sanddata.ams.frontend` and needs spec 008 deployed first.

## What the UI feature should cover

- Prices and costs now include VAT, grid charge, taxes and supplier markup, so every label that says price or cost must
  say it includes VAT.
- Decide whether to show the parts of the price (energy, markup, grid, taxes, VAT) and where: a breakdown on the
  current-hour card, the hourly list or a detail view.
- Show what Norgespris has saved (or cost) compared with spot terms: today, the last 7 days, the month so far and the
  year so far. A negative value is an extra cost. State how many hours were counted when some hours have no spot price.
- Mention on the screen that the supplier monthly fee is not included. The grid capacity charge (kapasitetsledd) is a
  monthly figure from its own endpoint and is not part of the hourly or daily costs.
- Show the capacity charge of the month: the step, the amount, the three daily peaks behind it, and how far the average
  is below the next step (so the household can spread its consumption). The current month is provisional.
- Show when a Norgespris hour is split by the monthly cap (5,000 kWh, 1,000 for a holiday home), and that hours above
  the cap are on spot terms.
- Admin screens: the location flags (electricity-tax exempt, VAT exempt, holiday home), the Norgespris cap values (two
  new price-rate kinds) and the capacity step tables of a grid tariff.

## Open points

- Where the saving goes: dashboard, history, or both.
- Whether the parts are shown to every user or only on a detail view.

## From spec 008 (decided 2026-11-16)

- The tax field of a Norgespris rate is no longer used by the price calculation (the electricity tax and the Enova fee are added separately). The Norgespris Admin page built in frontend PR 13 still shows and sends a Tax field: remove it or label it as not used in this UI feature.
- The `parts` object on the hourly and current-hour cost responses and the `GET /api/electricity-cost/saving` response are described in `specs/008-full-electricity-price/contracts/endpoints.md`.

## From spec 008 stories 6 and 7 (2026-11-17)

- Hourly and current-hour responses have a `cap` object (`kwhWithinCap`, `kwhAboveCap`, `aboveCapParts`), null when
  nothing is above the cap; then `ratePerKwh` is `cost / kWh`.
- `GET /api/admin/location-pricing` and its `PUT` carry `electricityTaxExempt`, `vatExempt`, `isHolidayHome` (optional
  in the PUT; omitted keeps the value).
- `GET /api/electricity-cost/capacity?locationId=&month=YYYY-MM` and the Admin
  `/api/admin/grid-tariffs/{id}/capacity-steps` endpoints. All shapes are in the contracts addendum.
