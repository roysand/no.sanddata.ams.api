---

description: "Task list for the Full Electricity Price per kWh"
---

# Tasks: Full Electricity Price per kWh

**Input**: Design documents from `/specs/008-full-electricity-price/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/endpoints.md, quickstart.md

**Tests**: Required. xUnit + NSubstitute in `tests/Features.Tests/`, as for features 004 to 007. `dotnet build` and `dotnet test` must pass, and `dotnet format --include <touched files>` must be clean for the files this feature touches (a repo-wide `dotnet format --verify-no-changes` already fails on old files).

**Organization**: Grouped by user story from spec.md (US1 full price per hour, US2 the parts of the price, US3 Admin maintains the rates and settings, US4 locations without a tariff, US5 the Norgespris saving). **Delivery order: US1, US2, US3, US4, US5.** US1 and US2 change the numbers every cost endpoint returns, so they come first and are proven by hand-calculated tests; US3 makes the data maintainable without touching the database; US4 and US5 follow. No web app change is in this feature (see `specs/_backlog/full-price-and-norgespris-saving-in-ui.md`).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1-US5 from spec.md

## Path Conventions

Paths are relative to the repository root. Handlers are discovered by `Cqrs.SourceGenerator` at compile time; do not register them by hand. Follow the spec 007 code as the pattern for dated rates: `src/Features/NorgesPris/`, `src/Application/ElectricityCost/NorgesPrisSchedule.cs`, `tests/Features.Tests/NorgesPris/` (including `FixedTimeProvider`, and `EndpointHost` with `HostCaller` in `tests/Features.Tests/Http/`). Files are written with CRLF line endings by `dotnet format`; run it on the touched files at the end. When writing files with a shell heredoc, avoid apostrophes in the text.

---

## Phase 1: Setup

- [x] T001 Record the figures the old calculation gives, for the before and after comparison in the pull request: on a scratch copy of the local database (never the real `amsdb`; see how spec 007 did it) run the unchanged code and save the JSON of `GET /api/electricity-cost/hourly` and `/daily` for one spot location and one Norgespris location. These numbers are expected to change by design (VAT, taxes, grid), so this is documentation, not a regression check. (Done 2026-11-16: unchanged main code on a scratch copy of the local database; spot hour 2026-10-02T19:00Z was 1.5799 NOK/kWh energy only, comparison 0.40.)

---

## Phase 2: Foundational (blocks all stories)

**Purpose**: the entities, the tables with their seed rows, and the repository. No behaviour changes yet.

- [x] T002 Create `src/Domain/Common/PriceRateKind.cs` (`enum PriceRateKind { ElectricityTax, EnovaFee, Vat }`) and `src/Domain/Common/Entities/PriceRate.cs`: derives from `Entity`; `PriceRateKind Kind`, `DateOnly ValidFrom`, `decimal Value`, `Guid AddedByUserId`, all `private set`; constructor `(Guid id, PriceRateKind kind, DateOnly validFrom, decimal value, Guid addedByUserId)` that also sets `CreatedAt`/`UpdatedAt`; a parameterless constructor for EF Core, as `NorgesPrisRate` does.
- [x] T003 [P] Create `src/Domain/Common/Entities/GridTariff.cs` (`string Name`, constructor `(Guid id, string name)`, parameterless constructor) and `src/Domain/Common/Entities/GridTariffRate.cs` (`Guid GridTariffId`, `DateOnly ValidFrom`, `decimal DayRatePerKwh`, `decimal NightRatePerKwh`, `Guid AddedByUserId`; constructor `(Guid id, Guid gridTariffId, DateOnly validFrom, decimal dayRatePerKwh, decimal nightRatePerKwh, Guid addedByUserId)`).
- [x] T004 Change `src/Domain/Common/Entities/Location.cs`: add `Guid? GridTariffId { get; private set; }` and `decimal SupplierMarkupPerKwh { get; private set; }` and `void SetPricing(Guid? gridTariffId, decimal supplierMarkupPerKwh)`. The existing constructor and `Update` keep their signatures (the markup defaults to 0, the tariff to null).
- [x] T005 [P] Create `src/Infrastructure/Database/Configuration/PriceRateConfiguration.cs` (kind stored as text, max length 20; `ValidFrom` required; `Value` `HasPrecision(9, 4)`; `AddedByUserId` required without a foreign key; unique index on `Kind` and `ValidFrom`; `HasData` with fixed ids `b1000000-0000-0000-0000-000000000001` to `...03`: `Vat` 0.2500 from 2020-01-01, `ElectricityTax` 0.0713 from 2026-01-01, `EnovaFee` 0.0100 from 2026-01-01, `AddedByUserId` `Guid.Empty`, `CreatedAt`/`UpdatedAt` `2026-11-16T00:00:00Z`), `GridTariffConfiguration.cs` (name required, max 100, unique index) and `GridTariffRateConfiguration.cs` (foreign key to `GridTariff` with `DeleteBehavior.Restrict`; `HasPrecision(9, 4)` on both rates; unique index on `GridTariffId` and `ValidFrom`), following `NorgesPrisRateConfiguration.cs`.
- [x] T006 Change `src/Infrastructure/Database/Configuration/LocationConfiguration.cs`: `SupplierMarkupPerKwh` required, `HasPrecision(9, 4)`, `HasDefaultValue(0m)`; `HasOne<GridTariff>().WithMany().HasForeignKey(l => l.GridTariffId).IsRequired(false).OnDelete(DeleteBehavior.Restrict)`.
- [x] T007 Add `DbSet<PriceRate> PriceRate`, `DbSet<GridTariff> GridTariff` and `DbSet<GridTariffRate> GridTariffRate` to `src/Infrastructure/Database/ApplicationDbContext.cs` and to `src/Application/Abstractions/Data/IApplicationDbContext.cs`.
- [x] T008 Create the migration per `DatabaseMigrations.md`: `dotnet ef migrations add AddFullElectricityPrice --project src/Infrastructure/Infrastructure.csproj --startup-project src/api/api.csproj --output-dir Database/Migrations`. Review the generated code: three new tables in schema `public`, the unique indexes, the restrict foreign keys, the two `Location` columns (markup default 0, tariff nullable), exactly three seed rows, and nothing else changed. Do not hand-edit the snapshot.
- [x] T009 [P] Create `src/Application/Common/Interfaces/Repositories/IPriceRateRepository.cs`, declared on its own like `INorgesPrisRateRepository` (so no immediately-saving `Delete` is exposed): reads for the calculator `Task<IReadOnlyList<PriceRate>> GetPriceRatesAsync(...)` and `Task<IReadOnlyList<GridTariffRate>> GetGridTariffRatesAsync(...)` (both ordered by start date, no tracking); reads for Admin `GetGridTariffsAsync`, `GetGridTariffByIdAsync`, `GridTariffNameExistsAsync(string name, ...)`, `GetPriceRateByIdAsync`, `GetGridTariffRateByIdAsync`; writes `TryInsertPriceRateAsync(PriceRate, ...)` and `TryInsertGridTariffRateAsync(GridTariffRate, ...)` (save, and return false when the unique start date was taken, also when a concurrent request won, as `NorgesPrisRateEfRepository.TryInsertAsync` does), `InsertGridTariff(GridTariff)`, `RemovePriceRate`, `RemoveGridTariffRate`, `SaveChangesAsync`.
- [x] T010 Create `src/Infrastructure/Database/Repositories/PriceRateEfRepository.cs` implementing T009 over `ApplicationDbContext`, following `NorgesPrisRateEfRepository.cs` (including detaching the entity and re-checking the date after a `DbUpdateException`). Do not derive from `GenericEfRepository`.
- [x] T011 [P] Add `Task<IReadOnlyList<ExchangeRate>> GetByDateRangeAsync(string currencyPair, DateOnly from, DateOnly to, CancellationToken ct)` to `src/Application/Common/Interfaces/Repositories/IExchangeRateRepository.cs` and implement it in `src/Infrastructure/Database/Repositories/ExchangeRateEfRepository.cs` (no tracking, ordered by date).
- [x] T012 Register `IPriceRateRepository` (`AddScoped`) in `src/Infrastructure/AddInfrastructureToDI.cs`.
- [x] T013 [P] Claim the log range: add the row `| 1700-1799 | Pricing |` to the EventId tables in `CLAUDE.md` and `DevelopmentGuide.md`.
- [x] T014 [P] Extend `tests/Features.Tests/Database/ModelDeleteBehaviorTests.cs`: the foreign keys from `GridTariffRate` and from `Location` to `GridTariff` are `DeleteBehavior.Restrict`, so a tariff in use can never be deleted by cascade; `Location.GridTariffId` is optional.

**Checkpoint**: the tables, seeds and repository exist; nothing in the running behaviour has changed yet.

---

## Phase 3: User Story 1 - Costs match what the bill charges per kWh (Priority: P1) 🎯 MVP

**Goal**: every price is `(1 + VAT) x (energy + markup + grid + taxes)`, with the grid rate chosen by Oslo hour, weekday and holiday, the dated values chosen by Oslo date, loaded once per request, and the Spot versus Norgespris comparison built on the same inputs.

**Independent Test**: unit tests with hand-calculated hours (weekday day, weekday night, Saturday, Sunday, public holiday, clock-change days, spot and Norgespris); the calculator loads the rate rows once however many hours it prices.

- [x] T015 [P] [US1] Create `src/Application/ElectricityCost/NorwegianHolidays.cs`: `public static bool IsHoliday(DateOnly date)` for 1 January, 1 May, 17 May, 25 December, 26 December and, from Easter Sunday computed with the anonymous Gregorian algorithm, Maundy Thursday (E-3), Good Friday (E-2), Easter Monday (E+1), Ascension Day (E+39) and Whit Monday (E+50) (research D5).
- [x] T016 [P] [US1] Create `src/Application/ElectricityCost/GridPeriod.cs`: `enum GridPeriod { Day, Night }` and `static GridPeriod For(DateTime hourStartUtc)`: convert with `Europe/Oslo` (`NorgesPrisSchedule.OsloDateOf` only gives the date, so `GridPeriod` does its own conversion with the same time zone to get the local weekday and hour); Day when the local weekday is Monday to Friday, the date is not a holiday, and the local hour is 6 to 21; otherwise Night (research D4).
- [x] T017 [P] [US1] Create `src/Application/ElectricityCost/PriceCalculation.cs`: `record PriceBreakdown(decimal Energy, decimal Markup, decimal? Grid, decimal Taxes, decimal Vat, decimal Total, bool GridSet)` and a static `PriceBreakdown Calculate(decimal energy, decimal markup, decimal? grid, decimal electricityTax, decimal enovaFee, decimal vatFraction)` using the formulas in data-model.md ("Pricing one hour"); no rounding.
- [x] T018 [US1] Create `src/Application/ElectricityCost/PriceRateBook.cs`: holds the `PriceRate` rows and the `GridTariffRate` rows (ordered by start date) and answers `decimal? ValueFor(PriceRateKind kind, DateTime hourStartUtc)` and `GridTariffRate? GridRateFor(Guid gridTariffId, DateTime hourStartUtc)` with the latest start date on or before the Oslo date of the hour, using `NorgesPrisSchedule.OsloDateOf` (research D3). Pure, no repository.
- [x] T019 [P] [US1] Write `tests/Features.Tests/Pricing/NorwegianHolidaysTests.cs` (the fixed days; Easter Sunday itself is not listed but the Easter-based days are: 2026-04-02, 2026-04-03, 2026-04-06, 2026-05-14, 2026-05-25; 2027-03-25, 2027-03-26, 2027-03-29; an ordinary weekday and 2 January are not holidays) and `GridPeriodTests.cs` (hours 05:00 night, 06:00 day, 21:00 day, 22:00 night in Oslo time in both winter and summer time; Saturday, Sunday and a holiday on a weekday are night all day; the 23-hour day `2027-03-28` and the 25-hour day `2026-10-25` give every real hour exactly one period; use UTC instants and check the Oslo conversion, for example `2026-12-31T22:00:00Z` is 23:00 Oslo).
- [x] T020 [P] [US1] Write `tests/Features.Tests/Pricing/PriceCalculationTests.cs` with hand-calculated cases: the example in data-model.md (energy 0.80, markup 0.02, grid 0.30, tax 0.0713 plus Enova 0.0100, VAT 0.25 gives net 1.2013, VAT 0.300325, total 1.501625); a Norgespris case (energy 0.40, markup 0, grid 0.30); grid `null` leaves the grid part out and `GridSet` false; VAT 0 gives total equal to net; for each case `Energy + Markup + (Grid ?? 0) + Taxes + Vat` equals `Total` exactly.
- [x] T021 [P] [US1] Write `tests/Features.Tests/Pricing/PriceRateBookTests.cs`: the value valid before and after a start date; the midnight hour in Oslo time (`2026-12-31T22:00:00Z` versus `23:00:00Z` for a rate starting 2027-01-01); a rate starting today applies from the first hour of today; an hour before the earliest row gives `null`; a tariff with no row for the hour gives `null`; each kind and each tariff is looked up separately.
- [x] T022 [US1] Change `src/Application/ElectricityCost/CostCalculator.cs` (research D1, D3, D8, D9, D10, D16): keep the existing behaviour and add `IPriceRateRepository` (T022, T023 and T024 are done together; the build is red until T024 updates the tests); load the `PriceRateBook` once per instance in a lazily loaded field next to the Norgespris rates; add `RateCost.Breakdown` (`PriceBreakdown`) so `RatePerKwh` is the total and `Cost` is consumption times it; energy is the `RatePerKwh` of the Norgespris row valid for the hour on Norgespris terms (the old `TaxPerKwh` is not used, research D16) and spot x EURNOK on spot terms; markup is `location.SupplierMarkupPerKwh` on spot terms and 0 on Norgespris terms; the grid rate comes from `GridPeriod.For` and the location tariff (`null` when the location has no tariff or the tariff has no row for the hour); VAT, electricity tax and Enova fee come from the book, and an hour missing any of them, or the spot price or exchange rate, gives a `null` `RateCost`. Add `Task PreloadAsync(Location location, DateTime fromUtc, DateTime toUtc, CancellationToken ct)` that loads the zone spot prices with `GetByRegionAndHourRangeAsync` and the exchange rates with `GetByDateRangeAsync` (from 7 days before the range) into dictionaries used by the per-hour lookups, falling back to the single-row queries when nothing was preloaded.
- [x] T023 [US1] Call `PreloadAsync` once before pricing a range: in `src/Features/ElectricityCost/Services/HourlyCostProvider.cs` (for `fromUtc` to `toUtc`) and in `src/Features/ElectricityCost/Handlers/GetDayPricesQueryHandler.cs` (for the day start to the day end). `GetCurrentHourCostQueryHandler` prices one hour and needs no preload.
- [x] T024 [US1] Update the existing tests that build the calculator: add the new constructor argument in `tests/Features.Tests/ElectricityCost/CostCalculatorTests.cs`, `CostHandlerTests.cs` and `GetDayPricesQueryHandlerTests.cs`, using a helper `tests/Features.Tests/Pricing/PriceRateStubs.cs` that returns a repository with VAT 0, electricity tax 0, Enova fee 0 and no grid tariff, so the existing assertions keep their numbers (the old formula is the new one with those inputs). Keep every existing test passing unchanged, with one exception: the tests that rely on the old `TaxPerKwh` of a Norgespris row (`CostCalculatorTests` with `NorgesPrisRates.Seeded(0.05m)` expecting 0.45, and `tests/Features.Tests/NorgesPris/CostCalculatorNorgesPrisTests.cs` with rates 0.40 plus tax 0.05 and 0.50 plus tax 0.02) must change to the new rule, where only the rate counts (0.40 and 0.50), and keep a test that a row with a non-zero tax gives the same price as the same row with tax 0.
- [x] T025 [P] [US1] Write `tests/Features.Tests/Pricing/CostCalculatorPriceTests.cs`: a spot location on a weekday 14:00 Oslo hour and a Sunday 14:00 hour differ by exactly (day rate minus night rate) x (1 + VAT) per kWh; a Norgespris location has no markup and the Norgespris rate only (a row with `TaxPerKwh` 0.05 gives the same price as the same row with 0); the comparison has the other side energy, the markup only on the spot side, and the same grid, taxes and VAT as the actual; a clock-change day prices every real hour; an hour before the earliest tax row gives `null`; a missing spot price or exchange rate gives `null`; pricing 24 x 30 hours calls `GetPriceRatesAsync` and `GetGridTariffRatesAsync` once each (`Received(1)`); after `PreloadAsync` the per-hour single-row price and exchange-rate lookups are not called (`DidNotReceive`).
- [x] T026 [US1] Run `dotnet build` and `dotnet test`; fix what breaks. Existing handler tests must pass with unchanged assertions.

**Checkpoint**: the numbers are right. Deployable on its own, with VAT and taxes included and the grid part left out until a tariff is set (US4).

---

## Phase 4: User Story 2 - The parts of the price are available (Priority: P1)

**Goal**: each hourly and current-hour price comes with its parts, exactly adding up to the total; `ratePerKwh` and `cost` stay as the totals.

**Independent Test**: request one hour and the parts add up to `total`, and `ratePerKwh` equals `parts.total`.

- [x] T027 [US2] Add the response record `PriceParts(decimal Energy, decimal Markup, decimal? Grid, decimal Taxes, decimal Vat, decimal Total, bool GridSet)` in `src/Features/ElectricityCost/Queries/GetHourlyCostQuery.cs` and add `PriceParts Parts` to `HourRateCostResponse` and to `CurrentRateCostResponse` in `GetCurrentHourCostQuery.cs` (additive; the existing fields keep their names and positions before it).
- [x] T028 [US2] Change `src/Features/ElectricityCost/Mappers/CostMapper.cs` to fill `Parts` from `RateCost.Breakdown` in `ToHourly` and `ToCurrent`; `ToDailyResponse` and the day prices are unchanged.
- [x] T029 [P] [US2] Write `tests/Features.Tests/ElectricityCost/CostMapperPartsTests.cs`: the parts equal the breakdown; the parts add up to the total; `ratePerKwh` equals `parts.total` and `cost` equals consumption times it; the daily response is the sum of the hourly costs; `Grid` is `null` and `GridSet` false when no tariff is set. Add one pipeline test in `tests/Features.Tests/Http/` (follow `NorgesPrisHttpTests.cs`) that the JSON of an hourly item contains `parts` with the expected property names.
- [x] T030 [US2] Run `dotnet build` and `dotnet test`.

**Checkpoint**: the parts are available to any client.

---

## Phase 5: User Story 3 - An Admin maintains the rates and settings (Priority: P1)

**Goal**: an Admin keeps the dated values, the grid tariffs and the per-location settings through Admin-only endpoints, with the same safety rules as spec 007.

**Independent Test**: as Admin, add a tax rate starting next month and see hours before and after use different values; refuse duplicates and edits of started rows; set a location tariff and markup; a non-Admin is refused.

- [x] T031 [P] [US3] Create `src/Features/Pricing/Logging/LogMessages.cs` (EventIds 1700 to 1799, internal static, `LoggerMessage.Define`, as `Features/NorgesPris/Logging/LogMessages.cs`): `RatesListed` (1700), `PriceRateAdded` (1701), `PriceRateDeleted` (1702), `GridTariffCreated` (1703), `GridTariffRateAdded` (1704), `GridTariffRateDeleted` (1705), `LocationPricingChanged` (1706: location id, tariff id, markup, acting user), `Refused` (1707: reason code, acting user). Reason codes: `DateExists`, `NotNewest`, `ConfirmationRequired`, `AlreadyStarted`, `NotFound`, `TariffNameExists`, `TariffNotFound`, `LocationNotFound`.
- [x] T032 [P] [US3] Create the records in `src/Features/Pricing/Commands/PricingCommands.cs` (`AddPriceRateCommand(PriceRateKind Kind, DateOnly ValidFrom, decimal Value, bool ConfirmPastDate, Guid ActingUserId)` with the kind already parsed from the request string, `DeletePriceRateCommand(Guid Id, Guid ActingUserId)`, `CreateGridTariffCommand(string Name, Guid ActingUserId)`, `AddGridTariffRateCommand(Guid GridTariffId, DateOnly ValidFrom, decimal DayRatePerKwh, decimal NightRatePerKwh, bool ConfirmPastDate, Guid ActingUserId)`, `DeleteGridTariffRateCommand(Guid GridTariffId, Guid RateId, Guid ActingUserId)`, `SetLocationPricingCommand(Guid LocationId, Guid? GridTariffId, decimal SupplierMarkupPerKwh, Guid ActingUserId)`, each an `ICommand<Result<...>>` with a small response record) and `src/Features/Pricing/Queries/PricingQueries.cs` (`ListPriceRatesQuery(PriceRateKind? Kind, Guid ActingUserId)`; every response record exposes the kind as a string such as `ElectricityTax` because the API has no JSON string-enum converter, research D17; `ListGridTariffsQuery(Guid ActingUserId)`, and the response records from contracts/endpoints.md: `PriceRateResponse`, `GridTariffResponse` with `GridTariffRateResponse`, `LocationPricingResponse`).
- [x] T033 [US3] Create `src/Features/Pricing/Handlers/DatedRatePolicy.cs`: the shared rule, used by T036 and T037 and nowhere else (research D12): given the existing start dates, the new start date, `ConfirmPastDate` and today (the Oslo date from `TimeProvider` through `NorgesPrisSchedule.OsloDateOf`), return `null` when allowed or the `Error.Conflict` for `Pricing.DateExists`, `Pricing.NotNewest` (start date today or earlier and not later than every existing one) or `Pricing.ConfirmationRequired`, checked in that order; plus `bool HasStarted(DateOnly validFrom, DateOnly today)` for delete.
- [x] T034 [US3] Create `src/Features/Pricing/Mappers/PricingMapper.cs`: static methods computing the status (`InEffect` is the row with the latest start date on or before today within its kind or tariff, `Upcoming` after today, otherwise `Superseded`) and mapping entities to the response records.
- [x] T035 [P] [US3] Create validators in `src/Features/Pricing/Validators/`: `AddPriceRateValidator` (the request `Kind` is a string that parses to a defined `PriceRateKind`, case-insensitive; date not default; value `>= 0`, `<= 100` and at most 4 decimals, and `<= 1` for `Vat`), `CreateGridTariffValidator` (name required, at most 100 characters), `AddGridTariffRateValidator` (date not default; both rates `>= 0`, `<= 100`, at most 4 decimals), `SetLocationPricingValidator` (markup `>= 0`, `<= 100`, at most 4 decimals). Request records are declared next to the endpoints (T039 to T041).
- [x] T036 [US3] Create the handlers `ListPriceRatesQueryHandler`, `AddPriceRateCommandHandler` and `DeletePriceRateCommandHandler` in `src/Features/Pricing/Handlers/` following `AddNorgesPrisRateCommandHandler` and `DeleteNorgesPrisRateCommandHandler`: list orders by start date and can filter by kind; add applies `DatedRatePolicy` against the existing rows of the same kind, inserts with `TryInsertPriceRateAsync` (a `false` result is `DateExists`) and logs; delete answers `NotFound` or `AlreadyStarted`, otherwise removes and saves once. Inject `TimeProvider`.
- [x] T037 [US3] Create the handlers `ListGridTariffsQueryHandler`, `CreateGridTariffCommandHandler` (name taken gives `Error.Conflict("Pricing.TariffNameExists", ...)`), `AddGridTariffRateCommandHandler` (unknown tariff gives `Error.NotFound("Pricing.TariffNotFound", ...)`; then `DatedRatePolicy` against the rows of that tariff) and `DeleteGridTariffRateCommandHandler` in `src/Features/Pricing/Handlers/`.
- [x] T038 [US3] Create `src/Features/Pricing/Handlers/SetLocationPricingCommandHandler.cs`: load the location with `ILocationRepository<Location>.GetByIdAsync`; unknown gives `Error.NotFound("Pricing.LocationNotFound", ...)`; a given tariff id that does not exist gives `Pricing.TariffNotFound`; otherwise `location.SetPricing(...)`, save, log `LocationPricingChanged`, return the list item for that location.
- [x] T039 [US3] Create the endpoints for the national rates in `src/Features/Pricing/Endpoints/` (Admin only, `AuthSchemes(JwtBearerDefaults.AuthenticationScheme)`, `Roles(RoleNames.Admin)`, Tags "Pricing", Summary with the status codes from contracts/endpoints.md): `ListPriceRatesEndpoint` (`GET /api/admin/price-rates`, optional `kind` query value as a string, an unknown value is `400`), `AddPriceRateEndpoint` (`POST`, request `AddPriceRateRequest(string Kind, DateOnly ValidFrom, decimal Value, bool ConfirmPastDate = false)`, answers `201`), `DeletePriceRateEndpoint` (`DELETE /api/admin/price-rates/{id}`, `204`). Map `ErrorType.NotFound` to 404 and `ErrorType.Conflict` to 409, as the Norgespris endpoints do.
- [x] T040 [US3] Create the endpoints for the grid tariffs in `src/Features/Pricing/Endpoints/`: `ListGridTariffsEndpoint` (`GET /api/admin/grid-tariffs`), `CreateGridTariffEndpoint` (`POST`, `{ name }`, `201`), `AddGridTariffRateEndpoint` (`POST /api/admin/grid-tariffs/{id}/rates`, request with the route id, dates, rates and `ConfirmPastDate`, `201`), `DeleteGridTariffRateEndpoint` (`DELETE /api/admin/grid-tariffs/{id}/rates/{rateId}`, `204`). The Summary of the add-rate endpoint says that the rates are entered excluding taxes and VAT, so a published rate that includes them must be converted first (FR-005).
- [x] T041 [US3] Create `src/Features/Pricing/Endpoints/SetLocationPricingEndpoint.cs` (`PUT /api/admin/location-pricing/{locationId}`, request `SetLocationPricingRequest(Guid? GridTariffId, decimal SupplierMarkupPerKwh)`, `200` with the list item; the Summary says that changing it recalculates past hours, as the location edit does for the Norgespris flag).
- [x] T042 [P] [US3] Create `tests/Features.Tests/Pricing/FakePriceRateRepository.cs`: an in-memory `IPriceRateRepository` with the same rules as the real one (ordered reads, unique start date per kind and per tariff, `TryInsert*` returning false for a taken date and for a settable `LoseTheRace`), modelled on `FakeNorgesPrisRateRepository`.
- [x] T043 [P] [US3] Write `tests/Features.Tests/Pricing/PriceRateHandlerTests.cs` (use `FixedTimeProvider` from `tests/Features.Tests/NorgesPris/`): list statuses and filter by kind; add a future rate; a duplicate date for the same kind is refused but the same date for another kind is accepted; a start date of today or earlier needs to be the newest of its kind and `ConfirmPastDate`, in the order `DateExists`, `NotNewest`, `ConfirmationRequired`; a lost race is `DateExists`; delete an upcoming row, refuse a started one (today included), unknown id is `NotFound`; and `DatedRatePolicy` cases directly.
- [x] T044 [P] [US3] Write `tests/Features.Tests/Pricing/GridTariffHandlerTests.cs`: create a tariff, a taken name (case-insensitive) is refused; add a rate to an unknown tariff is `TariffNotFound`; the dated rules per tariff (the same date on two tariffs is fine); delete rules; the list shows tariffs with their rates and statuses.
- [x] T045 [P] [US3] Write `tests/Features.Tests/Pricing/SetLocationPricingHandlerTests.cs`: sets tariff and markup; clearing the tariff with `null`; unknown location is `LocationNotFound`; unknown tariff is `TariffNotFound`; the change is logged and saved once.
- [x] T046 [P] [US3] Write validator tests `tests/Features.Tests/Pricing/PricingValidatorTests.cs` (negative, over 100, five decimals, VAT above 1, empty name, 101-character name, default date) and `tests/Features.Tests/Http/PricingHttpTests.cs` following `NorgesPrisHttpTests.cs`: every new Admin endpoint gives `401` without a token and `403` for a signed-in non-Admin; an Admin gets the success status; a body is bound into the command (including `confirmPastDate` defaulting to false, `kind` bound from a string and returned as a string, and an unknown kind giving `400`); an invalid body is `400` before any handler runs; a handler conflict maps to `409` and a not-found to `404`.
- [x] T047 [US3] Run `dotnet build` and `dotnet test`.

**Checkpoint**: an Admin can maintain every input of the price without a redeploy.

---

## Phase 6: User Story 4 - Locations without a tariff do not break (Priority: P2)

**Goal**: a location with no grid tariff still prices, with the grid part marked not set, and the Admin can see which locations are incomplete.

**Independent Test**: a location with no tariff shows `parts.grid` null and `gridSet` false and appears with `isComplete: false` in the Admin list; once a tariff is set it is complete and the grid part appears.

- [x] T048 [US4] Create `src/Features/Pricing/Queries/ListLocationPricingQuery.cs` handled by `src/Features/Pricing/Handlers/ListLocationPricingQueryHandler.cs` (every location, including ones the Admin is not linked to, with tariff name, markup and `isComplete` true when a tariff is set; use `ILocationRepository<Location>.GetAllWithKeyAsync`, which already returns every location, and take the tariff names from `IPriceRateRepository.GetGridTariffsAsync`; no new repository method is needed) and `src/Features/Pricing/Endpoints/ListLocationPricingEndpoint.cs` (`GET /api/admin/location-pricing`, Admin only).
- [x] T049 [P] [US4] Write `tests/Features.Tests/Pricing/ListLocationPricingHandlerTests.cs` (incomplete without a tariff, complete with one, markup shown, all locations returned) and extend `PricingHttpTests.cs` (`401`/`403`/`200`).
- [x] T050 [P] [US4] Extend `tests/Features.Tests/Pricing/CostCalculatorPriceTests.cs`: a location without a tariff prices with `Grid` null and `GridSet` false and the total is the sum without a grid part, never zero for the whole price; a tariff with no rate for the hour behaves the same; after the tariff is set the grid part appears.
- [x] T051 [US4] Run `dotnet build` and `dotnet test`.

**Checkpoint**: the first deployment is safe for locations that have not been configured yet.

---

## Phase 7: User Story 5 - The system can tell what Norgespris saves (Priority: P2)

**Goal**: one endpoint gives the saving for today, the last 7 days, the month so far and the year so far, computed from the same hourly prices.

**Independent Test**: for a location with hours on both sides of the Norgespris rate the figure equals the hand-calculated sum of the hourly differences, can be negative, and counts hours without a spot price separately.

- [x] T052 [P] [US5] Create `src/Features/ElectricityCost/Queries/GetNorgesPrisSavingQuery.cs` (`GetNorgesPrisSavingQuery(Guid UserId, Guid LocationId)`; records `SavingPeriodResponse(string Period, DateOnly From, DateOnly To, decimal Saving, int HoursCounted, int HoursWithConsumption)` and `NorgesPrisSavingResponse(IReadOnlyList<SavingPeriodResponse> Periods)`) and add a `SavingQueried` message (next free id in 1500-1599) to `src/Features/ElectricityCost/Logging/LogMessages.cs`.
- [x] T053 [US5] Create `src/Features/ElectricityCost/Handlers/GetNorgesPrisSavingQueryHandler.cs` (research D14): access check as the other cost handlers (not linked is `NotFound`); today in Oslo time from `TimeProvider`; the four periods as Oslo calendar days (Today, Last7Days = today and the six days before, MonthToDate, YearToDate); one `HourlyCostProvider.GetAsync` call for the range from the earlier of the start of the year and the start of the last-7-days period, through the end of today; for each period sum over its hours `spotCost - norgesPrisCost` where the spot-terms cost is `Actual` on a spot location and `Comparison` on a Norgespris location (and the other way round), skipping an hour where either is `null`; count `HoursCounted` and `HoursWithConsumption`. A pure static helper for the per-period sum (in the same file or `Features/ElectricityCost/Services/SavingCalculator.cs`) so it can be tested without a repository.
- [x] T054 [US5] Create `src/Features/ElectricityCost/Endpoints/GetNorgesPrisSavingEndpoint.cs` (`GET /api/electricity-cost/saving`, request `locationId`, JWT, any signed-in user, `404` when not linked) following `GetDailyCostEndpoint.cs`.
- [x] T055 [P] [US5] Write `tests/Features.Tests/ElectricityCost/NorgesPrisSavingTests.cs`: a hand-calculated day with one hour where spot is above and one where it is below the Norgespris rate (the second reduces the saving and the total can be negative); the same figure for a spot location and a Norgespris location; an hour without a spot price is left out and `HoursCounted < HoursWithConsumption`; the periods are Oslo calendar days and the shorter periods add up to the longer ones (for a date range that does not cross a year or month boundary and for one that does); a Norgespris rate change during the period is used per hour; a user not linked gets `NotFound`; and an endpoint test in `tests/Features.Tests/Http/` (`401` without a token, `200` shape with four periods).
- [x] T056 [US5] Run `dotnet build` and `dotnet test`.

**Checkpoint**: the saving numbers are available; a later UI feature shows them.

---

## Phase 8: Polish and cross-cutting

- [x] T057 Run `dotnet build` (no new warnings), `dotnet test` several times in a row (the suite had an order-dependent failure before; it must stay at zero failures), and `dotnet format --verify-no-changes --include <every .cs file this feature added or changed>` (list them with `git diff --name-only` and `git ls-files --others --exclude-standard`, excluding the migrations). Check that the formatter did not make unrelated changes (it marked `DbSet` properties `required` in spec 007; revert that if it happens).
- [x] T058 Verify on a scratch copy of the local database, as in spec 007 (clone with `CREATE DATABASE ... TEMPLATE`, a throwaway admin password in the clone only, run the new code on another port, never touch the real `amsdb`; remove the clone and any worktree afterwards): apply the migration, run quickstart.md sections 1 to 9, and compare one hour with a hand calculation. Record the before and after numbers next to T001. Also take one real hour and one real day from an actual electricity bill and grid bill (the per-kWh lines) and compare them with the app for the same period; the difference should be explained only by the monthly charges of phase 2 (SC-004). (Done 2026-11-16 on a scratch copy, since removed; the real amsdb was not touched. Hand calculations matched to the last decimal for four hours: Friday 21:00 and 22:00 Oslo, spot and Norgespris; grid day and night boundaries, Saturday and Sunday; a tax change on 2026-10-03; clearing the tariff; daily equals the sum of the hours; the year-to-date saving 41.660059747 equals an independent sum of the hourly figures (24 of 36 hours priced); ordinary user 403 on every Admin endpoint; 40 days of hourly costs in 28 ms.)
- [x] T059 Update `specs/008-full-electricity-price/checklists/requirements.md` and this file with what was verified, and update `specs/_backlog/full-price-and-norgespris-saving-in-ui.md` if anything the UI feature needs became clearer (for example the final `parts` and `saving` shapes). (The UI backlog note was updated in the specify step: the Norgespris tax field, the parts and the saving shapes.)
- [x] T060 Commit on `feature/008-full-electricity-price`, push, and open a pull request against `main` that describes the formula, the seed values, the fact that every cost figure rises on deploy (VAT, taxes, and grid once a tariff is set), that the Admin must add a grid tariff and set each location before the grid part appears, and the phase 2 items left out.
- [ ] T061 Before deploying: check that the production database and settings need nothing else (no config keys were added or removed); after deploying, add the grid tariff of each location through Scalar (`POST /api/admin/grid-tariffs`, its rates, then `PUT /api/admin/location-pricing/{locationId}`) and the supplier markup, and check `GET /api/admin/location-pricing` shows every location as complete.

---

## Dependencies & Execution Order

- **Phase 1** first (the baseline needs the unchanged code).
- **Phase 2** blocks everything. Order inside it: T002 to T004 before T005 to T007; T005 to T007 before T008; T009 before T010 and T012; T011 is independent.
- **US1** (T015 to T026) needs Phase 2. T015 to T017 are independent; T018 needs T002 and T003; T022 needs T015 to T018, T009 and T011; T023 and T024 need T022; T019 to T021 and T025 follow their targets.
- **US2** needs US1 (the breakdown comes from T017 and T022).
- **US3** (T031 to T047) needs Phase 2 only for the entities and repository, so it can run in parallel with US1 and US2 once Phase 2 is done; it is ordered after them because their numbers matter most. Inside it: T031 to T035 before T036 to T038; handlers before endpoints; T042 before T043 to T045.
- **US4** needs US1 (calculator behaviour) and US3 (`location-pricing` endpoints share the slice).
- **US5** needs US1 (the prices) and uses `HourlyCostProvider` with the preload from T023.
- **Polish** last. T058 needs a running database; the earlier phases are covered by unit and pipeline tests without one.

### Parallel opportunities

- Phase 2: T003 with T002; T005, T009, T011, T013 and T014 once their inputs exist.
- US1: T015, T016, T017 together, then T019, T020, T021 together, then T025 with T024.
- US3: T031, T032, T035 together; T042 to T046 together once the handlers exist.
- US4 and US5 can be done in either order after US1 and US3.

## Implementation Strategy

1. **MVP = Phase 1 + 2 + US1 + US2.** After these every cost endpoint returns the full price including VAT and taxes, with the parts. Without a grid tariff the grid part is left out, so the figures are still too low until US3 lets an Admin add tariffs; that is the safe default for the first deploy.
2. **US3 next**, because the grid part, markup and every future tax change need it, and it removes any need to touch the database by hand.
3. **US4**, then **US5**, which are small once the calculation and the Admin data exist.
4. Deploy US1 to US3 together if possible, so the Admin can enter the grid tariffs the same day the figures change. Keep the UI feature (`specs/_backlog`) for after this is deployed.
