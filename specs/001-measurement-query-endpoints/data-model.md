# Phase 1 Data Model: Measurement Query Endpoints

## No new persisted entities

This feature is read-only over data that already exists (`Measurement`, `Meter`, `Location`,
the `User`↔`Location` association). No new EF Core entity, table, or migration is required.

Existing entities relevant to these queries (for reference, unchanged):

- **Measurement** (`Domain/Common/Entities/Measurement.cs`): `Id`, `LocationId`, `MeterId`,
  `Timestamp` (UTC), `PowerWatts`. Indexed today on `(LocationId, Timestamp)` and
  `(MeterId, Timestamp)` — both query paths this feature needs are already covered, no new
  index/migration required.
- **Location** (`Domain/Common/Entities/Location.cs`): `Id`, `Name`, `Address`, `Zone`,
  `IsActive`, `Users` (many-to-many), `Meters` (one-to-many). The `Users` collection is the
  authorization boundary for every query in this feature.
- **Meter** (`Domain/Common/Entities/Meter.cs`): `Id`, `LocationId`, `DeviceId`, `MeterId`
  (HAN-reported serial, nullable), `MeterType` (nullable), `Comment`, `IsActive`.

## New repository methods

Following the existing pattern of adding specific methods to a per-entity repository interface
(e.g. `IApiKeyEfRepository.FindActiveByKeyAsync`), rather than growing the generic repository:

**`ILocationEfRepository<T>`** (`Application/Common/Interfaces/Repositories/ILocationEfRepository.cs`):

- `Task<bool> IsUserAssociatedAsync(Guid userId, Guid locationId, CancellationToken ct)` — cheap
  existence/ownership check used by every measurement query before touching `Measurement` at all.
- `Task<IReadOnlyList<Location>> GetForUserAsync(Guid userId, CancellationToken ct)` — eager-loads
  `Meters`; backs the "list my locations" query.

**`IMeasurementEfRepository<T>`** (`Application/Common/Interfaces/Repositories/IMeasurementEfRepository.cs`):

- `Task<(IReadOnlyList<Measurement> Items, int TotalCount)> GetPagedAsync(Guid locationId, Guid? meterId, DateTime from, DateTime to, int page, int pageSize, CancellationToken ct)` —
  ordered by `Timestamp` ascending.
- `Task<Measurement?> GetLatestAsync(Guid locationId, Guid? meterId, CancellationToken ct)` —
  ordered by `Timestamp` descending, first-or-default.

## New read-model DTOs (Features layer, not Domain)

- **`LocationSummaryResponse`**: `Id`, `Name`, `Address`, `Zone`, `Meters: MeterSummaryResponse[]`.
- **`MeterSummaryResponse`**: `Id`, `DeviceId`, `MeterId?`, `MeterType?`, `Comment?`, `IsActive`.
- **`MeasurementResponse`**: `Timestamp`, `MeterId`, `PowerWatts`.
- **`PagedMeasurementsResponse`**: `Items: MeasurementResponse[]`, `Page`, `PageSize`,
  `TotalCount`.

## New error codes

| Code | HTTP Status | Used when |
|---|---|---|
| `Location.NotFound` | 404 | `locationId` doesn't exist, or isn't associated with the caller (same response for both — see research.md §3) |
| `Meter.NotFound` | 404 | `meterId` supplied but not registered under the given `locationId` |
| `Validation.InvalidRange` | 400 | `to` is earlier than `from` |
