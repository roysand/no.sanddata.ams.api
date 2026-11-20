# Quickstart: Checking the Zaptec Charger Feature

How to prove the feature works. For the request and response shapes, see [contracts/endpoints.md](contracts/endpoints.md).

## 0. Before implementation: try Zaptec by hand

This answers the "verify by hand" points in [research.md](research.md) R2.

Put Roy's Zaptec login in shell variables, not in a file in the repo:

```bash
read -r ZU; read -rs ZP   # Zaptec e-mail, then password (not echoed)
TOKEN=$(curl -s https://api.zaptec.com/oauth/token -A "AmsApi/1.0" \
  --data-urlencode grant_type=password --data-urlencode "username=$ZU" \
  --data-urlencode "password=$ZP" --data-urlencode scope=openid | jq -r .access_token)
curl -s -A "AmsApi/1.0" -H "Authorization: Bearer $TOKEN" \
  "https://api.zaptec.com/api/chargers?PageSize=100" | jq '.data[] | {id,name,installationId,installationName}'
CHARGER=<id from above>
curl -s -A "AmsApi/1.0" -H "Authorization: Bearer $TOKEN" \
  "https://api.zaptec.com/api/sessions/archived?ChargerId=$CHARGER&From=2026-10-01T00:00:00Z&To=2026-11-21T00:00:00Z&PageSize=200" \
  | jq '{hasMore, n: (.sessions|length), first: .sessions[0] | {id,startDateTime,endDateTime,energy,voided,replacedBySessionId, details: (.energyDetails|length), sample: .energyDetails[0:3]}}'
```

Record the results in research.md R2:

| Check | Expected |
|---|---|
| The token call | `200`, with `expires_in` |
| Both data calls | `200`. If they give `401` or `403`, a homeowner login can't use the API, and the UserGroup route is needed first. Stop and tell Roy. |
| `energyDetails` | How many points there are per hour of charging, and whether `energy` grows (cumulative) |
| Session kWh | The sessions and their kWh match the Zaptec app's history |

## 1. Unit tests

```bash
dotnet test tests/Features.Tests --filter "FullyQualifiedName~EvCharger"
```

They cover:
- **Splitter:** points per hour, per 15 min and none; a step across an hour boundary; a decreasing point; the sum
  always equals the session's kWh; daylight saving days.
- **`EvChargerShare.Sum`:** null when there are no parts.
- **Handlers:** with a substituted client and repositories, `403` and `404` cases, the connect check, and "include"
  refused with no charger.
- **Cost handlers:** `totalKwh` with the charger included and not included; every cost field is identical in both
  cases (SC-004).

## 2. Locally, end to end

1. **Add the login.** Put `"Zaptec": { "Username": "…", "Password": "…" }` in `src/api/local.settings.json`, which
   is git-ignored. Then apply the migration with `dotnet ef database update …` (see `CLAUDE.md`) and run the API.
2. **Connect as Admin.**
   - `GET /api/admin/ev-chargers` lists the charger.
   - `PUT /api/admin/locations/{Hjemme}/ev-charger` with that `chargerId` gives `200`.
3. **Wait for the first read** (or set `EvCharger:ReadIntervalMinutes` to 1).
   `GET /api/locations/{Hjemme}/ev-charger/sessions?from=<start of last month>` should match the Zaptec app:
   - the same sessions, at ±0.01 kWh each;
   - the month's total at ±0.1 kWh (SC-002).
4. **Read a charging day with the setting off.** Read `GET /api/electricity-cost/hourly?locationId=…&from=…&to=…` for
   a day with charging. Each charging hour has `evCharger.included: false` and `totalKwh == consumptionKwh`. Save the
   JSON.
5. **Turn the setting on** with `PUT /api/locations/{Hjemme}/ev-charger/include` and `{ "include": true }`, then read
   the same day again.
   - `totalKwh` has risen by exactly `evCharger.kwh`.
   - Every other field matches step 4, compared with `jq 'del(.items[].totalKwh, .items[].evCharger)'` (SC-004).
6. **Check the daily view and the bill.**
   - `GET /api/electricity-cost/daily`: `evCharger.kwh` is the sum of the hourly parts.
   - `GET /api/electricity-cost/bill?month=<this month>`: `total`, the lines and the capacity are the same as with the
     setting off; `totalKwh` includes the charger.
7. **Check access and the not-configured case.**
   - A viewer gets `403` on the include endpoint.
   - Removing the login from `local.settings.json` and restarting: the API runs, the admin list answers `503
     EvCharger.NotConfigured`, and one log line says the sign-in is not set up.
8. **Check the logs (SC-006).** Search the console log for the Zaptec e-mail, the password and `Bearer`. None of them
   should appear.

## 3. Production

1. Add `ZAPTEC_USERNAME` and `ZAPTEC_PASSWORD` to the server's `.env` (see `DockerDeployment.md`), deploy, and repeat
   steps 2–6 against Hjemme.
2. To check the outage case (SC-005), set a wrong password in `.env` and restart.
   - The log should show one `EvChargerReadFailing` entry and no new sessions.
   - Then restore the password and restart. A charge from the gap appears within 15 minutes.
