# Backlog: Hot water used, for the Heater tab (frontend)

Status: the API side is built (spec `specs/020-water-heater-consumption`). Storing the litres (US1, PR #66) was
merged and deployed on 2026-11-24. The endpoint itself (US2 and US3) is built on the branch
`feature/020-water-heater-consumption-build` and ships in PR 2, before the calibration (T030–T033 in
`specs/020-water-heater-consumption/tasks.md`). The calibration checks the counting rule against the OSO app from about
2026-11-27, in a small follow-up PR. Update this note with PR 2's number when it is merged.

This note is the handover for a chart of the hot water used on the Heater tab in `no.sanddata.ams.frontend` (frontend
spec 009). It should look like the OSO app's "Total v40 used" view. Remove this note once the frontend's spec has
taken it up.

## For the frontend

Full shape and field meanings:
[`specs/020-water-heater-consumption/contracts/endpoints.md`](../020-water-heater-consumption/contracts/endpoints.md)
and [`data-model.md`](../020-water-heater-consumption/data-model.md).

- **The request:** `GET /api/locations/{id}/water-heater/hot-water?period=day|week|month|year&date=yyyy-MM-dd`. JWT
  only. Both parameters may be left out: `period` is then `day` and `date` today in Oslo.
- **The switch and the arrows:** the Day/Week/Month/Year switch maps to `period`. The back and forward arrows step
  `date` by one period: a day, 7 days, a month or a year. Any date in the period works, because the answer is the
  whole period that holds it:
  - a week is the ISO week, Monday to Sunday;
  - a month is the calendar month, and a year the calendar year, in Oslo time.
- **One bar per item:** a day has one item per hour (23 or 25 on the days the clocks change), a week and a month one
  per day, and a year one per month. Only parts that start before now are listed, so today stops at the current
  hour and a period wholly in the future has `items: []`. `from` and `to` are always the whole period, for the axis.
- **The total:** `totalLitres` is the "Total v40 used" figure. It is the sum of the bars' `litres`, so the two always
  agree. It is null when no bar has litres.
- **Time:** `start`, `from` and `to` are UTC instants. Show them in Oslo time: `2026-11-20T23:00:00Z` is 00:00 on
  21 November.
- **Gaps:** `litres: null` means the part has no sampled time. Draw a gap, never 0. `0` means it was sampled and no
  water was used.
- **How much to trust a bar:** `sampledMinutes` says how much of the part was sampled (up to 60 for an hour, 1,440
  for a day, 1,500 on the day with 25 hours). A bar with few sampled minutes is too low; consider marking it. The top-level `sampledMinutes` is
  the sum over the bars.
- **History begins on 2026-11-24**, when US1 was deployed. Earlier periods answer with null litres and a null total,
  as does a location without a heater. That is not an error.
- **The numbers can still move a little:** the calibration's follow-up PR may adjust the counting rule after PR 2 is
  deployed. The response shape stays the same, but check the figures against the OSO app after that PR, not before.
- **Who may see it:** owners and viewers of the location, and Admins. Everyone else gets `404 Location.NotFound`, the
  same as for a location that does not exist. No connected heater is needed: the samples belong to the location.
- **Errors:** 400 for a `period` that isn't one of the four or a `date` that isn't a real `yyyy-MM-dd` date, 401
  without a token, 404 as above.

## Not covered by the API

- Showers or baths as units: the API gives litres only.
- Cost of the hot water: the heater's kWh and cost are spec 012's figures, not this endpoint.
