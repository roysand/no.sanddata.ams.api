# Backlog: Connect the OSO water heater

Status: idea, not specified. Written 2026-11-19 at Roy's request, to pick up later. Nothing is built.

## What Roy has

An OSO water heater (OSO Energy, Norway) that can be reached through OSO's API. Which model, which location it is on
(Hjemme or the holiday home), its device id and whether a subscription key already exists are not written down yet.

## The API: OSO inCharge API 1.0

Read from OSO's public Swagger on 2026-11-19; not yet tried with a real key and device.

- Swagger UI: <https://api.osoenergy.no/water-heater-api-swagger/index.html>
- OpenAPI 3.0 document: <https://api.osoenergy.no/water-heater-api-swagger/swagger/1.0/swagger.json>
- Base URL: `https://api.osoenergy.no/water-heater-api`
- Authentication: header `Ocp-Apim-Subscription-Key: <subscription key>` (Azure API Management). The key is created
  on the owner's account on the OSO Energy website (as the Home Assistant integration describes it), or by asking OSO.
- Every call is per device: `/1/Device/{deviceId}/…`. There is **no endpoint that lists the devices**, so the device id
  must be found in the OSO app or website and entered by hand.
- Errors: 400, 401, 403 and 500 with `{ "error": "…" }`; some calls also answer 404.

### Reading

| Call | Returns |
|---|---|
| `GET /1/Device/{deviceId}/Status` | `DeviceStatusPublic`: `availableMix40Volume` and `maxMix40Capacity` (litres of 40 °C water now and at most), `availableShowers`, `availableBaths`, `connectionState` (0/1), `lastActivityTime`, `profileStatus` (holiday, supercharge and "blue battery" mode with start and end times, `meaning` as text), `features`, `optimizationOption` / `optimizationSubOption`, and `lastTelemetry` (time, heater on/off, control `mode` 0–9, `currentSetPoint` °C) |
| `GET /1/Device/{deviceId}/CurrentTemperature` | `DeviceTemperatures`: time and `temperatureTop`, `temperatureMid`, `temperatureLow`, `temperatureOne` (°C) |
| `GET /1/Device/{deviceId}/ActualLoad` | `actualLoad`: the heater's power right now, kW |
| `GET /1/Device/{deviceId}/AvailableMixV40Volume` | `availableMixV40Volume`: litres of 40 °C water available (nullable) |
| `GET /1/Device/{deviceId}/TappingCapacityKwh` | `tappingCapacitykWh`: the energy stored as usable hot water, kWh |

### Control

| Call | Does |
|---|---|
| `PUT /1/Device/{deviceId}/Profile` | Sets the 24 hourly temperature setpoints, body `{ "hours": [60, 60, …, 60] }` (°C per hour; the example uses 50–65). Which time zone hour 0 is in is not stated (probably Oslo) |
| `POST /1/Device/{deviceId}/TurnOn?fullUtilizationParam=true\|false` | Turns heating on "if available" |
| `POST /1/Device/{deviceId}/TurnOff?fullUtilization=true\|false` | Turns heating off "if available" (note the parameter name differs from TurnOn) |
| `PUT /1/Device/{deviceId}/OptimizationMode` | Body `{ "optimizationOptions": int, "optimizationSubOptions": int? }`; OSO's own optimisation (the values are not explained) |
| `POST /1/Device/{deviceId}/HolidayMode/{startUtc}/{endUtc}` | Holiday mode between two UTC times (`yyyy-MM-ddTHH:mm:ssZ`) |
| `DELETE /1/Device/{deviceId}/HolidayMode` | Ends holiday mode |

### What the API does not give

- **No history and no energy counter**: only the power right now (`ActualLoad`). To show the heater's kWh and cost per
  day, our API must sample `ActualLoad` (for example every minute, as the meter readings) and store it.
- **No meaning of the numbers**: `ControlMode` 0–9, `optimizationOption`, `optimizationSubOption`, what
  `fullUtilization` does, and the difference between the four temperatures are not documented. Ask OSO, or read the
  open-source Home Assistant integration (`osoenergy`) and the `pyosoenergyapi` package.
- **No rate limits** are stated.

Other sources: [Home Assistant OSO Energy integration](https://www.home-assistant.io/integrations/osoenergy/),
[OSO Energy](https://osoenergy.no/en/?p=802), [OSO smart management](https://osoenergy.no/en/funksjoner/).

## Why it fits this app

A water heater is often the largest flexible load in a home (typically 2–3 kW), and the app already knows what is
needed to move it:

1. **Heat in the cheapest hours.** The frontend's cheapest-time card (`cheapest.ts`) already finds the cheapest hours
   at the price the location pays. Once tomorrow's prices are published, those hours could get a high setpoint and the
   expensive hours a low one, sent with `PUT …/Profile`.
2. **Stay below the next capacity step.** The capacity charge (kapasitetsledd) comes from the highest hour on each of
   the three days with the most use. When the current hour is heading for a new peak (live power is already read every
   minute), `TurnOff` the heater and `TurnOn` it again when the hour is safe. This is the one part of the grid bill the
   household can lower, and it saves money on Norgespris too.
3. **Show it.** Temperature, litres of 40 °C water and showers left, whether it is heating now and at what power, and,
   once sampled, its share of the day's kWh and cost.

On Norgespris the energy price is fixed, so (1) saves little at that location; (2) still saves on the grid bill.

## Open questions before a spec

- Model, location, device id, and whether Roy has a subscription key.
- Read only first (show state and power), or control from the start (profile, pausing during peaks)?
- Where the key and device id are stored: per location in the API, the key encrypted, Admin only; never in the
  frontend (`VITE_*` values ship to the browser).
- Safety: hot water must still reach a high enough temperature regularly against legionella. The app must never keep
  the heater off or the setpoints low for long; a pause for a peak must end by itself, and a profile must keep a
  minimum.
- Does our control fight OSO's own optimisation? It may need `OptimizationMode` turned off while the app controls the
  heater, and on again when it stops.
- What happens when OSO's service is down: the heater must keep following its own profile.
- Rate limits, and whether sampling `ActualLoad` every minute is acceptable to OSO.

## Suggested first step

Find the device id in the OSO app, get the subscription key, and try the read calls by hand (for example in the
Swagger UI or with curl). Note the real answers here, and which `ControlMode` and `optimizationOption` values the
heater reports. Then run `/speckit-specify` in this repo for a read-only first version.
