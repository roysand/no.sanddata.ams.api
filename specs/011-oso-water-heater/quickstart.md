# Quickstart: Read the OSO Water Heater

How to prove spec 011 works, from tests to Roy's real heater. Endpoints: [contracts/endpoints.md](contracts/endpoints.md).

## Prerequisites

- The OSO subscription key and the heater's device id (from the OSO app or website). Use a **new** key: the first one
  was shown in a chat transcript on 2026-11-19.
- An encryption key: `openssl rand -base64 32`.
- Locally, in `src/api/local.settings.json`:

  ```json
  { "WaterHeater": { "KeyEncryptionKey": "<base64 from openssl>", "SampleIntervalSeconds": 60 } }
  ```

- An Admin token and a token for a user linked to Hjemme (`POST /api/auth/login`).

Keep the key and device id in shell variables (`read -s OSO_KEY`, `read OSO_DEVICE`) rather than typing them on the
command line, so they stay out of shell history.

## 1. Automated tests

```bash
dotnet build
dotnet test tests/Features.Tests --filter "FullyQualifiedName~WaterHeater"
dotnet test tests/Features.Tests --filter "FullyQualifiedName~RequestLoggingRedaction"
dotnet format --verify-no-changes
```

Expected: all pass. They cover parsing the 2026-11-19 OSO answers, the error mapping, the encryption round trip and
tamper check, connect/replace/remove, access (`404` for unlinked users), the sampler's gaps and failure logging, and
that the key and device id appear in no log entry and no response (SC-006).

## 2. Connect the heater (US1)

```bash
curl -s -X PUT "$API/api/admin/locations/$HJEMME/water-heater" \
  -H "Authorization: Bearer $ADMIN" -H "Content-Type: application/json" \
  --data "$(jq -n --arg k "$OSO_KEY" --arg d "$OSO_DEVICE" '{subscriptionKey:$k, deviceId:$d}')"
```

Expected: `200`, a `deviceIdHint` like `4b8814…49499d`, no key in the body. Then:

- Repeat with a wrong key: `400`, `WaterHeater.Rejected`, and `GET …/water-heater` still shows the old connection.
- Repeat as a regular user: `403`.
- In Postgres, `ProtectedSubscriptionKey` starts with `v1:` and does not contain the key.

## 3. Read the state (US2, SC-002, SC-003)

```bash
time curl -s "$API/api/locations/$HJEMME/water-heater" -H "Authorization: Bearer $USER" | jq
```

Expected: `200` within 3 s; litres, showers, temperatures and heating on/off match the OSO app at the same moment;
`ageSeconds` is a few minutes at most; `fourthCelsius` is null. As a user not linked to Hjemme: `404`.

**Check the supercharge time zone** (spec Assumptions): the next time a supercharge starts, compare `specialMode.start`
with the start time the OSO app shows. If they differ by 1 hour (2 in summer), OSO sends Oslo time and the parser in
`OsoWaterHeaterClient` must change.

## 4. Sampling (US3, SC-004, SC-005)

After 10 minutes connected:

```sql
SELECT "SampledAtUtc", "PowerKw" FROM "WaterHeaterPowerSample"
WHERE "LocationId" = '<hjemme id>' ORDER BY "SampledAtUtc" DESC LIMIT 12;
```

Expected: about 10 rows, one per minute, standby power ~0.01 kW when not heating.

Outage: reconnect with a wrong key is refused, so to test failure set `WaterHeater:KeyEncryptionKey` to a different
value locally and restart. Expected: one `WaterHeaterSampleFailing` warning (reason `KeyUnreadable`), no new rows,
every other endpoint still works; restore the key and restart: rows resume within 2 minutes.

After a day: at least 1,368 rows for the day (SC-004).

## 5. Logs (SC-006)

```bash
docker logs ams-api 2>&1 | grep -c -e "$OSO_KEY" -e "$OSO_DEVICE"
```

Expected: `0`.

## 6. Production (Hetzner)

1. On the server, add `WATER_HEATER_KEY_ENCRYPTION_KEY=<base64>` to `/opt/ams-api/.env`.
2. Copy the updated `compose.prod.yaml` to `/opt/ams-api/` (the pipeline does not copy it).
3. Deploy; the migration `AddWaterHeater` runs at startup.
4. Repeat steps 2–5 against production with Roy's heater at Hjemme.

Losing the encryption key only means connecting the heater again; samples are not encrypted and are kept.
