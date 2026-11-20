# Backlog: The water heater's power in the frontend

Status: the API side is built (spec `specs/017-water-heater-power`, written 2026-11-20). What is left is the frontend
spec in `no.sanddata.ams.frontend`: water heating drawn under the meter's power line in the "Last 3 hours" card.
Remove this note once that spec is written.

## For the frontend

- **Full shape:** [`specs/017-water-heater-power/contracts/endpoints.md`](../017-water-heater-power/contracts/endpoints.md).
- **The request:** `GET /api/locations/{id}/water-heater/power?from=&to=`, the same 3-hour window as the meter's
  5-minute power. Ask twice: today's 3 hours, and the same 3 hours yesterday.
  - Send `from` and `to` on whole 5 minutes. Left out, `to` is now and `from` is 3 hours before it.
  - The window is at most 48 hours, and `from` must not be in the future (`400` otherwise).
  - `from` and `to` in the answer are the window used, in UTC.
- **The items:** one per 5 minutes, with `start`, `powerKw` and `sampledSeconds`. `start` is on whole 5 minutes, the
  same in UTC and Oslo time, so it lines up with the meter's steps.
- **A missing step is a gap, never 0.** No samples (no heater, before it was connected, an outage) give no item, and a
  window without samples gives `"items": []`. Show nothing then.
- **How much to trust a step:** `sampledSeconds` (1–300) says how much of the 5 minutes was sampled. `powerKw` is the
  average over the sampled time only. The step going on now covers only up to now.
- **Not capped:** the heater's power here is not capped at the meter's, unlike the hourly cost figures (spec 012).
- **Access:** owners, viewers and Admins. Anyone else gets `404 Location.NotFound`. No connected heater is needed: a
  location whose heater was removed still answers for the past.
