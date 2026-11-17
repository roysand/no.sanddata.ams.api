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
- Mention on the screen that the monthly charges (supplier monthly fee, grid fixed fee) are not included until phase 2.

## Open points

- Where the saving goes: dashboard, history, or both.
- Whether the parts are shown to every user or only on a detail view.

## From spec 008 (decided 2026-11-16)

- The tax field of a Norgespris rate is no longer used by the price calculation (the electricity tax and the Enova fee are added separately). The Norgespris Admin page built in frontend PR 13 still shows and sends a Tax field: remove it or label it as not used in this UI feature.
- The `parts` object on the hourly and current-hour cost responses and the `GET /api/electricity-cost/saving` response are described in `specs/008-full-electricity-price/contracts/endpoints.md`.
