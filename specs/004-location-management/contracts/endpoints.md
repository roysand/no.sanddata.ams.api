# API Contracts: Location Management and Hashed Sensor Keys

Auth: **admin** = JWT with the `Admin` role (401 without a token, 403 without the role). Request bodies are JSON;
route-only calls carry no body.

## New admin endpoints

### `GET /api/admin/locations`  (admin)
All locations, including ones the caller is not linked to.

**200**:
```json
[
  {
    "id": "22222222-2222-2222-2222-222222222222",
    "name": "Home",
    "address": "Test address",
    "serialNumber": "SN123",
    "zone": "NO1",
    "isActive": true,
    "hasNorgesPriceAgreement": false,
    "apiKey": {
      "description": "Test ingestion key",
      "hint": "…test",
      "isActive": true,
      "expiresAt": "2027-08-28T14:06:42Z",
      "status": "Active"
    },
    "meters": [
      { "id": "…", "deviceId": "58:CF:79:9C:93:AE", "meterId": "7359992896383454", "meterType": "6525",
        "comment": "Main building", "isActive": true }
    ]
  }
]
```
`apiKey.status` is `Active`, `Expired` or `Deactivated`. The key itself is **never** in this response.

### `POST /api/admin/locations`  (admin)
Creates the location and its sensor key.

Body: `{ "name", "address", "serialNumber", "zone", "hasNorgesPriceAgreement", "isActive" }`
(`isActive` defaults to `true`).

**201**: `{ "location": <as in the list above>, "apiKey": "<full key, shown only in this response>" }`

Errors: **400** invalid or missing field (zone not NO1-NO5, empty/too long text); **409** `Location.SerialNumberExists`;
**401/403**.

### `PUT /api/admin/locations/{id}`  (admin)
Body: `{ "name", "address", "serialNumber", "zone", "hasNorgesPriceAgreement", "isActive" }`.
The sensor key is not touched.

**200**: the updated location (list shape). **404** `Location.NotFound`; **409** `Location.SerialNumberExists`; **400**.

### `POST /api/admin/locations/{id}/api-key/rotate`  (admin)
No body. Generates a new key, invalidates the old one at once, expires in two years, and reactivates the key if it
was deactivated.

**200**: `{ "apiKey": "<full new key, shown only in this response>", "hint": "…ab12", "expiresAt": "…" }`
**404** `Location.NotFound`.

### `PUT /api/admin/locations/{id}/api-key`  (admin)
Body: `{ "isActive": false }`. Activates or deactivates the existing key (it does not change the key).

**200**: the key info (`description`, `hint`, `isActive`, `expiresAt`, `status`). **404** `Location.NotFound`.

## Changed existing behavior

| Endpoint | Change |
|---|---|
| `GET /api/locations` | Inactive locations are no longer listed for regular users |
| Every data endpoint that checks location membership (measurements, consumption, cost, meters) | An inactive location is "not found" for regular users |
| `POST /api/measurements` (sensor) | Same `X-API-Key` header; rejected (401) if the key is wrong, expired, deactivated, **or the location is inactive** |
| `POST /api/meters` | Admins may register readers at **any** location (active or not); linked users only at their own active locations, as before |

## Error codes

| Code | HTTP | When |
|---|---|---|
| `Location.NotFound` | 404 | Unknown location id (admin endpoints); hidden/inactive/unlinked for users |
| `Location.SerialNumberExists` | 409 | Another location already has that serial number |
| `Validation.InvalidZone` | 400 | Zone is not one of NO1-NO5 |
| `Meter.DeviceIdExists` | 409 | Existing: device id already registered at the location |

## Secrets

The full key appears only in the 201 of `POST /api/admin/locations` and the 200 of `.../api-key/rotate`. It is not
stored, logged, or returned anywhere else; those two response bodies are also never written by the request logger.
