# Tasks: User Creates Own Location

- [X] T001 Add `LinkToUserId` to `src/Features/Locations/Commands/CreateLocationCommand.cs`
- [X] T002 Link in the same save in `src/Features/Locations/Handlers/CreateLocationCommandHandler.cs`
- [X] T003 Add `LocationMapper.ToOwnCommand` in `src/Features/Locations/Mappers/LocationMapper.cs`
- [X] T004 Add `src/Features/Locations/Endpoints/CreateOwnLocationEndpoint.cs` (`POST /api/locations`)
- [X] T005 Tests in `tests/Features.Tests/Locations/CreateLocationCommandHandlerTests.cs` (209 tests pass)
- [ ] T006 Manual check against a running API: create as non-admin, then `GET /api/locations` lists it
- [ ] T007 Deploy to the environment the frontend calls
