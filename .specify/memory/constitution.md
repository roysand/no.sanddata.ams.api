<!--
Sync Impact Report
- Version change: 1.0.1 → 1.0.2 (PATCH — Principle V: handlers are registered by the source
  generator, not by hand; the old text was stale)
- Previous: 1.0.0 → 1.0.1 (PATCH — clarified/strengthened existing migrations guidance)
- Modified principles: none (no principle renamed or redefined)
- Added sections: none (existing Development Workflow line strengthened; Technology Stack
  bullet added for visibility)
- Removed sections: none
- Follow-up TODOs: none
-->

# no.sanddata.ams.api Constitution

## Core Principles

### I. Strict Clean Architecture Layering
Domain MUST have zero dependencies on any other layer. Each outer layer depends only inward:
Application → Infrastructure → Features → API. EF Core, HTTP, or any other infrastructure
concern MUST NOT leak into Domain or Application. Business rules and entities live in Domain;
use cases, CQRS abstractions, and error types live in Application; persistence, auth, and
external services live in Infrastructure; vertical-slice use cases live in Features; HTTP
wiring lives in API.
**Rationale**: keeps domain logic testable and portable independent of framework or database
choice, and makes layer violations a build-time (project reference) error rather than a review
nitpick.

### II. Vertical Slices Over Shared Coupling
Each feature under `Features/<Name>/` MUST own its Commands, Queries, Handlers, Endpoints,
Validators, and Mappers. Features MUST NOT reach into another feature's internals. When two
features need similar logic, a small amount of duplication is preferred over introducing a
shared dependency between them.
**Rationale**: features evolve independently; shared coupling between slices is the most common
way small changes ripple into unrelated endpoints.

### III. Explicit Control Flow Over Exceptions
Business and domain failures MUST be represented as values (`Result<T>` + `Error`), never thrown
as exceptions. Exceptions are reserved for truly unrecoverable conditions (e.g. programmer
errors, infrastructure faults). Handlers return `Result<T>`; endpoints translate a failed result
into an error response via `AddError` + `ThrowIfAnyErrors`.
**Rationale**: predictable, cheap control flow for expected failure paths (not-found, conflict,
invalid credentials) without the cost and opacity of exception-driven flow.

### IV. Validate Once, At the Boundary
FastEndpoints + FluentValidation MUST validate every request before a handler runs. Handlers
MUST trust their input and MUST NOT re-validate what the pipeline already checked.
**Rationale**: a single source of truth for input validity avoids duplicated, drifting validation
logic between the endpoint layer and the handler layer.

### V. Real CQRS Separation, Compile-Time Dispatch
Commands mutate state and return `Result<T>`; Queries read state and return `Result<T>` and MUST
NOT mutate anything. All dispatch MUST go through the `Cqrs.SourceGenerator`-emitted dispatcher
(a compile-time type-switch), never through reflection-based lookup (`MakeGenericType`,
reflection `Invoke`) or a MediatR-style runtime pipeline. New handlers are discovered and registered
automatically at compile time by `Cqrs.SourceGenerator`; they MUST NOT be registered by hand.
**Rationale**: read/write separation stays real (not cosmetic), and dispatch errors surface as
compile errors instead of runtime "no handler registered" failures.

### VI. Structured, Low-Allocation Logging
Logging MUST use compiled `LoggerMessage` delegates defined in per-feature (or centralized)
`LogMessages` classes, with structured properties and stable, documented reason codes (e.g.
`UserNotFound`, `InvalidToken`). String-concatenated log messages are prohibited. Secrets and
PII (passwords, tokens, raw personal data) MUST NOT be logged.
**Rationale**: consistent, queryable structured logs and stable reason codes are what make
alerts and dashboards reliable; string-built messages and ad hoc codes silently rot.

### VII. Explicit Manual Mapping, No Automappers
Conversions between DTOs, commands, and domain entities MUST use static per-feature mapper
classes (`Features/<Name>/Mappers/`). Reflection-based automapping libraries MUST NOT be used.
**Rationale**: manual mappers are explicit, debuggable, and keep mapping bugs visible in code
review instead of hidden behind reflection-based conventions.

### VIII. Minimal-Footprint Changes
Do not add abstractions, configuration flags, or error handling for scenarios the system does
not yet need. Package versions live only in `Directory.Packages.props`, never hardcoded per
project. Prefer the smallest change that correctly satisfies the current requirement.
**Rationale**: this is a solo-maintained project; speculative abstraction and premature
configurability are pure maintenance cost with no reader to justify them for.

## Technology Stack & Architecture Constraints

- Framework: ASP.NET Core 10 (.NET 10), REST via FastEndpoints.
- Persistence: Entity Framework Core (SQL Server or PostgreSQL — provider choice is kept cheap
  to change by staying behind EF Core). All schema changes MUST be expressed as EF Core
  migrations (`dotnet ef migrations add`) and applied via `dotnet ef database update`; hand-edited
  schema or direct out-of-band DB changes are prohibited.
- Authentication: JWT Bearer for user/UI access; API Key (`X-API-Key`, DB-backed, expirable) for
  sensor/system ingestion. Endpoints declare their scheme(s) explicitly via `AuthSchemes(...)`.
- Password hashing: BCrypt.Net-Next only.
- Validation: FluentValidation, wired through the FastEndpoints pipeline.
- CQRS dispatch: the project's own `Application/CQRS` abstractions plus the
  `Cqrs.SourceGenerator`-generated dispatcher — no external CQRS/mediator framework dependency.
- EventId ranges for `LoggerMessage` definitions MUST stay within the table maintained in
  `CLAUDE.md` / `DevelopmentGuide.md`, and that table MUST be updated when a new range is
  claimed.

## Development Workflow

- New features follow the vertical-slice checklist in `DevelopmentGuide.md`: Command/Query →
  Handler → Validator → Mapper → Endpoint → DI registration in
  `Infrastructure/AddInfrastructureToDI.cs` → validator assembly registration in `Program.cs`.
- `dotnet format --verify-no-changes` MUST pass before a change is considered done;
  `dotnet build` MUST succeed with no new warnings introduced by the change.
- EF Core schema changes go through migrations only (`dotnet ef migrations add`), following
  `DatabaseMigrations.md` — no hand-edited schema drift. Every entity/mapping change MUST ship
  with its migration in the same commit; a migration MUST NOT be edited after it has been applied
  to any database — write a new migration instead.
- As a solo-maintainer hobby project, there is no multi-approver review gate; the author is the
  reviewer. Self-review against this constitution's principles before merging to `main` replaces
  a formal PR-approval process.

## Governance

This constitution supersedes ad hoc practice for anything it explicitly states. Where it is
silent, `CLAUDE.md`, `DevelopmentGuide.md`, `AuthenticationGuide.md`, and `DatabaseMigrations.md`
remain the authoritative day-to-day references.

**Amendment procedure**: edit this file directly (or via the `speckit-constitution` workflow),
update the Sync Impact Report at the top, and bump the version per the policy below. Because
this is a solo project, no separate approval step is required — the act of committing the
amendment is the ratification.

**Versioning policy** (semantic versioning applied to governance):
- MAJOR: backward-incompatible principle removal or redefinition (e.g. dropping the
  Result-over-exceptions rule, or allowing reflection-based dispatch).
- MINOR: a new principle or materially expanded section is added.
- PATCH: wording clarifications, typo fixes, non-semantic refinements.

**Compliance**: before implementing a feature, check it against the principles above (this is
what `speckit-plan`'s constitution check step is for). Any deliberate deviation MUST be called
out explicitly in the feature's plan with a one-line justification, rather than silently
drifting from the stated architecture.

**Version**: 1.0.2 | **Ratified**: 2026-09-24 | **Last Amended**: 2026-10-03
