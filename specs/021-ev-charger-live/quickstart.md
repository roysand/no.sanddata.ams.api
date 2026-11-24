# Quickstart: Checking the Car Charger's State Right Now

**Spec**: [spec.md](spec.md) | **Contract**: [contracts/endpoints.md](contracts/endpoints.md)

## 0. Before implementation: read the state by hand

This answers the "check by hand" points in [research.md](research.md) R1. Use spec 016's quickstart §0 to get
`$TOKEN` and `$CHARGER` with Roy's login in shell variables, never in a file in the repo.

The full answer holds values that must not be saved (serial, SIM numbers, signed meter data, PIN), so **filter it
before looking at it** and before pasting anything into the repo:

```bash
curl -s -A "AmsApi/1.0" -H "Authorization: Bearer $TOKEN" \
  "https://api.zaptec.com/api/chargers/$CHARGER/state" \
  | jq '[.[] | select(.StateId as $i | [-2,-1,513,553,710,721] | index($i))
         | {StateId, Timestamp, ValueAsString: (if .StateId == 721 then "<session id>" else .ValueAsString end)}]'
```

Read it in three situations and note the results in research.md R1:

| Situation                              | Check                                                                           |
| -------------------------------------- | ------------------------------------------------------------------------------- |
| Charging                               | The field names and `Timestamp` format match R1, and 710 = 3                    |
| Just after plugging in a car           | Whether 553 is reset to 0 at once, or keeps the last session's value until it changes (R2 rule 4) |
| No car                                 | 710 = 1, and what 513 and 553 still hold                                        |

The filtered "charging" answer, with timestamps and figures kept, becomes the canned sample in `ZaptecSamples.cs`.

## 1. Unit tests

```bash
dotnet test tests/Features.Tests --filter "FullyQualifiedName~EvCharger"
```

They cover:

- **`ChargerLiveState.From`**:
  - each mode, 1, 2, 3, 5, 4 and missing;
  - offline from the flag, and from a check-in older than 10 minutes while the flag says 1;
  - power only while charging;
  - kWh 0 when 553 is older than 721;
  - rounding.
- **`ChargerStateCache`** (with a manual `TimeProvider`):
  - two calls within 3 minutes give one client call;
  - a call after 3 minutes gives a new one;
  - a failure is held for 1 minute and then read again;
  - two calls at the same time while a read is running give one client call;
  - cancelling one caller doesn't cancel the read for the other.
- **Client** (`ZaptecStubHandler`):
  - it parses the canned sample, and timestamps without `Z` come back as UTC;
  - the unwanted ids are dropped;
  - a 429 gives `Unavailable` at once with one request, not two;
  - a stub delay of 5 s gives `Unavailable` after about 3 s;
  - a 401 signs in again once;
  - not configured gives `NotConfigured` with no request.
- **Handler**:
  - `404` without access;
  - an Admin may read;
  - `noCharger` gives no client call;
  - a failure gives the error;
  - the session repository is never called (FR-004).
- **`NoSecretsInLogsTests`**: the state call logs no token, URL or charger id.

## 2. Locally, end to end

1. Run the API with the Zaptec login in `src/api/local.settings.json` and the charger connected to Hjemme (spec 016
   quickstart §2).
2. While the car charges, call `GET /api/locations/{Hjemme}/ev-charger/state`:
   - the state is `charging`;
   - `powerKw` and `sessionKwh` match the Zaptec app within one check-in (SC-001).
3. Call it again at once, then from a second signed-in user who is a viewer of Hjemme. `readAt` is the same in all
   three answers, and the console shows one `EvChargerStateRead` (1930) (US2-1). The third answer should come in
   under 100 ms (SC-004).
4. Call the same endpoint for a location without a charger. It gives `200` with `state: "noCharger"`, and no
   `EvChargerStateRead` is logged (US3-3).
5. Sign in as a user with no link to Hjemme. The answer is `404 Location.NotFound` (US3-2).
6. Set a wrong Zaptec password and restart, then call the state twice within a minute:
   - both answers are `503 EvCharger.SignInRejected`;
   - one `EvChargerStateFailed` (1931) is logged;
   - the spec 016 read job still runs on its own schedule (FR-003).
7. Search the console log for the Zaptec e-mail, the password, `Bearer` and the charger id. None of them should
   appear (FR-007).

## 3. Production

1. Deploy, then during a charge repeat step 2 against Hjemme with the Car tab or `curl`.
2. **SC-002**: leave the app open on two devices for an hour during a charge, then count the reads in Application
   Insights:

   ```kusto
   traces
   | where timestamp > ago(1h)
   | where tostring(customDimensions.EventName) == "EvChargerStateRead"
   | count
   ```

   The count should be at most 20.
3. **SC-003**: run the same query over a night when nobody has the app open. The count should be 0.
4. **SC-004**: check the two targets separately.
   - **When Zaptec is read**, the p95 of `ElapsedMs` should be under 1000:

     ```kusto
     traces
     | where timestamp > ago(1d)
     | where tostring(customDimensions.EventName) == "EvChargerStateRead"
     | summarize p95 = percentile(toint(customDimensions.ElapsedMs), 95), reads = count()
     ```

   - **From the shared answer**: in `requests`, the p95 duration for
     `GET /api/locations/{locationId}/ev-charger/state` should be under 100 ms. Most requests are answered from the
     shared answer, so their p95 is mostly that case. Compare `reads` above with the request count to confirm.
5. When the charge ends, it appears in `…/ev-charger/sessions` within 15 minutes. The day's kWh in the cost figures
   don't change while the charge is running (FR-004).
