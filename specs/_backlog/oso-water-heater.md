# Backlog: Connect the OSO water heater

Status: specified as `specs/011-oso-water-heater/spec.md` (read-only first version), branch
`feature/011-oso-water-heater`. Written 2026-11-19 at Roy's request; read calls tried by hand the same evening.
Nothing is built. Remove this note when spec 011 ships; the API findings below then live in its research.md.

## What Roy has

An OSO water heater (OSO Energy, Norway) at **Hjemme**, reachable through OSO's API. Roy has a subscription key and
the device id (a 128-character hex string, so it is too long for a short text column). Neither is written here; the
model is still not noted.

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

## Tried by hand, 2026-11-19 18:08 UTC

All five read calls answered `200` with the real key and device id. Answers (device id left out):

```json
// GET Status
{"lastTelemetry":{"timestamp":"2026-11-19T18:06:48.703Z","control":{"heater":0,"mode":0,"currentSetPoint":72}},
 "optimizationOption":1,"optimizationSubOption":2,"smartCompanyId":null,
 "availableMix40Volume":344,"maxMix40Capacity":360,"connectionState":1,
 "lastActivityTime":"2026-11-19T18:06:48.703Z","availableShowers":3,"availableBaths":1,
 "profileStatus":{"isInHolidayMode":false,"holidyModeStartTime":null,"holidyModeEndTime":null,
   "isInSuperchargeMode":true,"superchargeModeStartTime":"2026-11-18T19:40:14.5919821",
   "superchargeModeEndTime":"2026-11-19T19:40:14.5919821","isInBlueBatteryMode":false,"id":3,
   "meaning":"SuperchargeMode"},
 "features":["SuperChargeMode"]}
// GET CurrentTemperature
{"timeStamp":"2026-11-19T18:06:48.703Z","temperatureOne":0,"temperatureLow":70.5,"temperatureMid":72.3,"temperatureTop":72.8}
// GET ActualLoad
{"actualLoad":0.011774}
// GET AvailableMixV40Volume
{"availableMixV40Volume":343}
// GET TappingCapacityKwh
{"tappingCapacitykWh":13.97741}
```

What this tells us:

- **Values our heater reports:** `mode` 0, `heater` 0 (off), `optimizationOption` 1, `optimizationSubOption` 2,
  profile `id` 3 = `SuperchargeMode`. Their meaning is still undocumented.
- **Telemetry is about 2 minutes old** when read (`timestamp` 18:06:48, read 18:08:31), and `Status` and
  `CurrentTemperature` share the same timestamp. Sampling more often than every minute or two gains nothing.
- **`actualLoad` is not zero when the heater is off** (0.0118 kW ≈ 12 W, probably electronics). Treat it as kW.
- **Spelling and time quirks:** `holidyModeStartTime` / `holidyModeEndTime` (sic); supercharge times have **no `Z`**
  and seven decimals, so their time zone is unknown (UTC or Oslo); `timeStamp` vs `timestamp` casing differs between
  calls. Deserialise case-insensitively and keep the misspelt names.
- `temperatureOne` is 0: probably a sensor this model does not have; do not show it as 0 °C.
- `Status.availableMix40Volume` (344) and `AvailableMixV40Volume` (343) differ by a litre: `Status` alone covers the
  read-only view, plus `CurrentTemperature`, `ActualLoad` and `TappingCapacityKwh`.

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

- ~~Location, device id, key~~: Hjemme; Roy has both. ~~Read only first?~~ Yes, read only first.
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
