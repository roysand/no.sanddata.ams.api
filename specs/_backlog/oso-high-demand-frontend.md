# Backlog: High Demand switch on the Heater tab (frontend)

Status: the API side is built and deployed (spec `specs/022-oso-high-demand`, PR #79, deployed on 2026-11-26). The
formal test against the real heater was not run: Roy assumes the switch works and tests it in daily use (see "What
isn't known yet"). Remove this note once the frontend's spec has taken it up.

This note is the handover for a High Demand switch on the Heater tab in `no.sanddata.ams.frontend`. High Demand heats
the tank extra for a while, as the OSO app's "High Demand" button does. The Heater tab already shows the mode as
"Supercharge until …" from the heater's state (spec 011), so what's new is the switch.

## For the frontend

Full shape, fields and errors:
[`specs/022-oso-high-demand/contracts/endpoints.md`](../022-oso-high-demand/contracts/endpoints.md).

- **The requests:** no body, JWT only.
  - `PUT /api/admin/locations/{locationId}/water-heater/high-demand` turns it on;
  - `DELETE` on the same URL turns it off.
- **Admins only.** Show the switch only to a user with the Admin role. Anyone else gets 403, so don't show them a
  switch that can't work. Everyone keeps seeing the mode, as today.
- **Which state the switch shows:** High Demand is on when the heater's state has `specialMode.mode ===
  "Supercharge"`. Show its `specialMode.end` (UTC, so show it in Oslo time) as "on until …".
- **The answer:** `{ locationId, highDemand, state }`.
  - `state` is the heater's state read from OSO just after the switch, the same object
    `GET /api/locations/{id}/water-heater` returns. Put it straight into the cached heater state (the query behind the
    Heater tab), so the tab updates at once without waiting for the next poll.
  - `state` is `null` when the switch was made but the read-back failed. Then refetch the heater's state, or wait for
    the next poll. It is not an error.
  - OSO may report the old mode for a moment. Show what `state` says, and the next poll catches up.
- **How long:** turned on through the API, High Demand lasts **24 hours**, unless it's turned off first. There is no
  duration to choose. Say so next to the switch, for example "on for 24 hours". Turning it on again while it's on may
  or may not restart the 24 hours; that hasn't been checked.
- **Turning it off when it's off** is not an error.
- **Errors:**
  - 403: not an Admin. Shouldn't happen if the switch is hidden.
  - 404 `WaterHeater.NotConnected`: no heater. The Heater tab isn't shown then anyway.
  - 502 `WaterHeater.HighDemandNotOffered`: **OSO no longer offers High Demand.** The calls are undocumented, so OSO
    may remove them. Hide or disable the switch and say why.
  - 502 `WaterHeater.Rejected` or `KeyUnreadable`: the heater's OSO key must be reconnected by an Admin, as for the
    state.
  - 503 `WaterHeater.Unavailable`: OSO is down or slow. Whether the heater changed is unknown, so refetch the state
    before letting the user try again.
- **No retries.** The API never retries a switch, and the app shouldn't either. A retried "on" could restart the
  24 hours. Let the user press again.
- **While a switch is in flight,** disable the switch, since a switch can take a few seconds. Expect up to 10 seconds
  if OSO is slow.

## What isn't known yet

- **The OSO app may not follow.** In Roy's hand test on 2026-11-26, after the API turned High Demand off, OSO's own
  status (and so our Heater tab) said normal mode, but the OSO app still showed High Demand. So the two apps may
  disagree for a while. Our tab shows what OSO's status reports.
- **Whether the heater follows the off call** (set point back from 72 to 60, heating stops) wasn't checked in a formal
  test. Roy tests it in daily use. If it turns out not to work, the switch is hidden again and this note is updated.

## Not covered by the API

- Choosing a duration shorter than 24 hours.
- The price of the coming hours next to the switch.
- Any other heater control (profiles, holiday mode, turning the heater on and off).
