# Implementation Plan: User Creates Own Location

**Spec**: [spec.md](./spec.md)

## Approach (smallest change, constitution VIII)

- `CreateLocationCommand` gets an optional `Guid? LinkToUserId = null`.
- `CreateLocationCommandHandler` also takes `IUserLocationRepository<UserLocation>`; when `LinkToUserId` is
  set it inserts the `UserLocation` before the single `SaveChangesAsync` (both repositories share the
  request-scoped `ApplicationDbContext`, so location, key and link commit together).
- `LocationMapper.ToOwnCommand(userId, request)` builds the command with the link.
- New `CreateOwnLocationEndpoint` (`POST /api/locations`, JWT, no role). It reuses `CreateLocationRequest`, so the
  existing `CreateLocationValidator` applies to both endpoints.
- No new log message: `LocationCreated` already records location id and acting user. No schema change, so no migration.

## Constitution check

I Layering: Pass (Features only). II Vertical slice: Pass (all inside `Features/Locations`). III Result values:
Pass. IV Validate once: Pass (validator reused). V CQRS: Pass (command only). VI Logging: Pass (no secrets). VII
Manual mapping: Pass. VIII Minimal: Pass (no new abstractions).

## Tests

`CreateLocationCommandHandlerTests`: link inserted in the same save, no link without `LinkToUserId`, no link on
duplicate serial, `ToOwnCommand` links caller and trims.
