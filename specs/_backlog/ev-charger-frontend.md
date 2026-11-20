# Backlog: The EV charger in the frontend

Status: the API side is built (spec `specs/016-zaptec-ev-charger`, written 2026-11-20). What is left is the frontend
spec in `no.sanddata.ams.frontend`: the checkbox, the car next to the house in the charts, and the charging sessions.
Remove this note once that spec is written.

## For the frontend

- **Full shapes:** [`specs/016-zaptec-ev-charger/contracts/endpoints.md`](../016-zaptec-ev-charger/contracts/endpoints.md).
- **Has a charger, and the checkbox:**
  - `GET /api/locations` now gives `hasEvCharger` and `includeEvCharger` per location. Show the checkbox "Count the
    car charger with the house" when `hasEvCharger` is true.
  - Change it with `PUT /api/locations/{id}/ev-charger/include` and `{ "include": true|false }`. Only an owner or an
    Admin may do that; a viewer gets `403`, so show the checkbox read-only to viewers (`role` is "Viewer").
  - It is one setting for the whole location, not per user.
- **The charts:** the hourly, daily, current-hour and bill responses gain `totalKwh` and an `evCharger` part.
  - Chart `totalKwh`. It is the meter's kWh plus the car's when the checkbox is on, and equals `consumptionKwh` when
    it is off.
  - `evCharger.kwh` is the car's share, to draw "car" next to "house". With the checkbox off it is still there,
    with `included: false`, so the car can be shown beside the house without being added.
  - `evCharger` is `null` when there is no charging data. Show nothing, never 0.
- **Costs do not change:**
  - The charger is not priced. `consumptionKwh`, every cost, the Norgespris figures and the whole bill stay the
    house's own.
  - Do not compute a price per kWh as cost ÷ `totalKwh`.
- **Days and the bill:**
  - `evCharger.hoursWithoutMeterReading` counts charging hours that had no house reading. They are in the day's and
    month's car kWh, but have no item in the hourly list.
  - The bill also gives `firstHour` and `lastHour` of the charging so far.
- **Sessions:** `GET /api/locations/{id}/ev-charger/sessions?from=&to=` lists completed sessions, newest first, with
  `start`, `end`, `kwh` and `estimated`. With no range it covers the last 30 days; the range is at most 366 days.
- **A charge in progress** is not shown until it has ended: Zaptec gives completed sessions only. The data is read
  every 15 minutes, so a finished charge appears within about 15 minutes. Say so near the car figures.
- **Admin page:**
  - `GET /api/admin/ev-chargers` lists the chargers the server's Zaptec login sees.
  - `PUT /api/admin/locations/{id}/ev-charger` with `{ "chargerId": "…" }` connects one, `GET` on the same path shows
    the connection, and `DELETE` removes it.
  - `503 EvCharger.NotConfigured` means the Zaptec login is not set in the server's `.env`.
