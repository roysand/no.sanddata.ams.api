# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a .NET 10 ASP.NET Core Web API that ingests electrical grid power-usage measurements from field sensors and persists them for later use. It follows Clean Architecture with vertical slices, using JWT (user/UI access) and API Key (sensor/system ingestion) authentication. A UI consuming this API's data will be built later, as a separate effort.

**Key Stack:**
- Framework: ASP.NET Core 10
- REST: FastEndpoints
- CQRS: Custom implementation (no external CQRS framework dependencies)
- Database: Entity Framework Core (SQL Server or PostgreSQL — provider TBD, EF Core keeps the switch cheap)
- Authentication: JWT Bearer + API Key
- Password Security: BCrypt.Net-Next
- Validation: FluentValidation (integrated with FastEndpoints pipeline)
- Mapping: Manual mappers (static classes per feature)
- Documentation: Scalar (OpenAPI/Swagger)

## Coding Philosophy

- **Strict Clean Architecture layering.** Domain has zero dependencies; each outer layer (Application → Infrastructure → Features → API) depends only inward. Never let EF Core, HTTP, or other infrastructure concerns leak into Domain or Application.
- **Vertical slices over shared coupling.** Each feature under `Features/<Name>/` owns its Commands, Queries, Handlers, Endpoints, Validators, and Mappers. Prefer duplicating a small amount of code across features over introducing a shared dependency between them.
- **Explicit over implicit control flow.** Business/domain failures are values (`Result<T>` + `Error`), never exceptions. Reserve exceptions for truly unrecoverable conditions.
- **Validate once, at the boundary.** FastEndpoints + FluentValidation validate the request before the handler runs. Handlers trust their input and never re-validate.
- **CQRS separation is real, not cosmetic.** Commands mutate and return `Result<T>`; Queries read and return `Result<T>`. A query never mutates state.
- **Structured, low-allocation logging.** Use compiled `LoggerMessage` delegates with structured properties and stable, documented reason codes — never string-concatenated log messages, and never log secrets or raw PII.
- **Explicit manual mapping.** Static mapper classes per feature convert between DTOs, commands, and entities. No reflection-based automappers.
- **No reflection-based dispatch.** `Cqrs.SourceGenerator` (`src/Generators/Cqrs.SourceGenerator/`) scans for `ICommandHandler<,>`/`IQueryHandler<,>` implementations at compile time and emits `Features.Generated.GeneratedDispatcher` — a type-switch over concrete command/query types, no `MakeGenericType`/reflection `Invoke` lookups. Adding a handler regenerates the dispatcher and its DI registration automatically on the next build; nothing to wire by hand.
- **Central, explicit dependencies.** Package versions live only in `Directory.Packages.props`, never hardcoded per-project.
- **Minimal-footprint changes.** Don't add abstractions, config flags, or error handling for scenarios the system doesn't need yet.

## Solution Structure

### Core Projects

| Project | Purpose |
|---------|---------|
| `src/api/Api.csproj` | REST API entry point and endpoints (FastEndpoints) |
| `src/Domain/Domain.csproj` | Core business entities, value objects, and domain logic |
| `src/Application/Application.csproj` | Use cases, commands, queries, business rules, interfaces, CQRS abstractions |
| `src/Infrastructure/Infrastructure.csproj` | Database, repositories, authentication, external services, DI registration |
| `src/Features/Features.csproj` | Vertical slices (Auth, Users, ...) with handlers and endpoints |
| `src/AI/AI/AI.csproj` | AI-related functionality |
| `src/Generators/Cqrs.SourceGenerator/Cqrs.SourceGenerator.csproj` | Roslyn incremental generator — emits the CQRS `IDispatcher` implementation and its DI registration, referenced as an analyzer by `Features.csproj` |

### Clean Architecture Layer Rules

- **Domain** (innermost): No dependencies on other layers. Pure business logic.
- **Application**: Depends only on Domain. Defines CQRS interfaces, business rules, error types.
- **Infrastructure**: Depends on Application & Domain. Implements repositories, database, authentication.
- **Features**: Depends on Application, Infrastructure, Domain. Implements specific use cases.
- **API** (outermost): Depends on all layers. HTTP entry point and configuration.

### Vertical Slice Layout

```
Features/YourFeature/
  Commands/
  Queries/
  Handlers/
  Endpoints/
  Mappers/
  Validators/
  YourFeature.csproj
```

**Request Flow**: HTTP request → FastEndpoint → FluentValidation Pipeline → Handler (if valid) → Domain logic → EF Repository → Database

**Custom CQRS Pattern**: `Application/CQRS/` defines the abstractions — `ICommand<TResult>`, `ICommandHandler<TCommand, TResult>`, `IQuery<TResult>`, `IQueryHandler<TQuery, TResult>`, `IDispatcher`. The concrete `IDispatcher` implementation and its DI registration are produced at compile time by `Cqrs.SourceGenerator` and land in `Features.Generated` (generated, not checked in) — see [DevelopmentGuide.md](DevelopmentGuide.md#custom-dispatcher).

## Getting Started

```bash
# 1. Clone and restore packages
git clone <repo-url>
cd no.sanddata.ams.api
dotnet restore

# 2. Configure local settings
# Create src/api/local.settings.json — see Configuration below

# 3. Apply database migrations
dotnet ef database update \
  --project src/Infrastructure/Infrastructure.csproj \
  --startup-project src/api/api.csproj

# 4. Build and run
dotnet build
dotnet run --project src/api/api.csproj

# 5. Access API documentation
# http://localhost:5231/scalar/v1 (HTTP) or https://localhost:7130/scalar/v1 (HTTPS)
```

**Adding a new feature?** See [DevelopmentGuide.md](DevelopmentGuide.md) for the full step-by-step checklist with code templates. Handlers are **not** auto-discovered — every handler must be manually registered in `Infrastructure/AddInfrastructureToDI.cs`.

## Common Development Commands

```bash
# Build / run
dotnet build
dotnet run --project src/api/api.csproj
dotnet build --configuration Release

# Formatting
dotnet format --verify-no-changes   # check
dotnet format                       # apply

# Packages (always via Directory.Packages.props, never per-project)
dotnet restore
dotnet add package PackageName --project [project-path]

# Migrations — see DatabaseMigrations.md for the full command reference
dotnet ef migrations add MigrationName \
  --project src/Infrastructure/Infrastructure.csproj \
  --startup-project src/api/api.csproj \
  --output-dir Database/Migrations
dotnet ef database update \
  --project src/Infrastructure/Infrastructure.csproj \
  --startup-project src/api/api.csproj
```

## Configuration

| File | Purpose |
|------|---------|
| `appsettings.json` | Default configuration (development defaults) |
| `local.settings.json` | Local overrides (git-ignored, NOT in source control) |
| `Directory.Packages.props` | Central NuGet package version management |
| `Directory.Build.props` | Shared project properties (nullable refs, implicit usings) |
| `.editorconfig` | Code formatting and style rules |

Load order (last wins): `appsettings.json` → `appsettings.{Environment}.json` → `local.settings.json` → environment variables.

**`local.settings.json`** (git-ignored):
```json
{
  "ApplicationSettings": {
    "DbConnectionString": "Server=localhost;Database=AmsDb;Trusted_Connection=true;TrustServerCertificate=true;"
  },
  "JwtSettings": {
    "SecretKey": "your-secret-key-must-be-at-least-32-characters-long",
    "Issuer": "AmsApi",
    "Audience": "AmsApiClients",
    "AccessTokenExpirationHours": 6
  }
}
```

## Validation & Error Handling

FastEndpoints validates the request via `Validator<TRequest>` **before** the handler runs; handlers never validate again. Handlers return `Result<T>` for domain/business errors instead of throwing:

```csharp
Error.NotFound("User.NotFound", "User not found")
Error.Conflict("User.EmailExists", "Email already in use")
Error.Unauthorized("Auth.InvalidCredentials", "Invalid email or password")
Error.BadRequest("Validation.InvalidInput", "Input validation failed")
Error.InternalServerError("System.Exception", "An unexpected error occurred")
```

In the endpoint: `if (!result.IsSuccess) { AddError(result.Error.Code, result.Error.Description); ThrowIfAnyErrors(); }`

Full code samples: [DevelopmentGuide.md](DevelopmentGuide.md#validation--error-handling).

## Authentication

Two schemes:
1. **JWT Bearer** — user login, role-based access (`POST /api/auth/login`, `Authorization: Bearer <token>`).
2. **API Key** — external system / sensor ingestion (`X-API-Key: <key>`, stored in DB, can expire).

```csharp
AuthSchemes(JwtBearerDefaults.AuthenticationScheme);        // JWT only
AuthSchemes(JwtBearerDefaults.AuthenticationScheme, "ApiKey"); // JWT or API Key
Roles("Admin", "Manager");                                   // role-gated
AllowAnonymous();                                             // open
```

Full login flow, securing-endpoint examples, and reading claims in handlers: [AuthenticationGuide.md](AuthenticationGuide.md).

## Code Standards

Enforced by `.editorconfig`:
- Nullable reference types & implicit usings enabled
- PascalCase for public members, camelCase for private
- 4-space indentation, no tabs
- Soft line-length limit: 120 chars
- Package versions only in `Directory.Packages.props`

## Logging

Structured logging via compiled `LoggerMessage` delegates in per-feature `LogMessages` classes — never string-concatenated messages, never log secrets/PII. Failures use short, stable reason codes (e.g. `UserNotFound`, `InvalidToken`).

EventId ranges (keep in sync as features are added):

| Range | Area |
|-------|------|
| 1000-1099 | Users |
| 1100-1199 | Auth |
| 1200-1299 | Test |
| 2000-2099 | Infra/Logging |

Full guide (why, request/response logging config, reason-code table): [DevelopmentGuide.md](DevelopmentGuide.md#logging-with-logmessages).

## Docker & Deployment

```bash
docker build -t ams-api:latest -f Dockerfile .
docker compose -f compose.yaml up
docker tag ams-api:latest myregistry/ams-api:latest
docker push myregistry/ams-api:latest
```

## References

- **FastEndpoints**: https://fast-endpoints.com/
- **Entity Framework Core**: https://learn.microsoft.com/en-us/ef/core/
- **ASP.NET Core Authentication**: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/
- **JWT Best Practices**: https://tools.ietf.org/html/rfc8725
- **CQRS Pattern**: https://learn.microsoft.com/en-us/azure/architecture/patterns/cqrs
- **Clean Architecture**: https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html

## Related Documentation

- [DevelopmentGuide.md](DevelopmentGuide.md) — feature-creation checklist, CQRS/EF Core internals, full logging guide
- [AuthenticationGuide.md](AuthenticationGuide.md) — auth system details, security practices, testing
- [DatabaseMigrations.md](DatabaseMigrations.md) — EF Core migration commands and best practices
- [UserCrudEndpoints.md](UserCrudEndpoints.md) — user management API endpoints
- [AzureDeployment.md](AzureDeployment.md) — Azure CLI commands to provision the API's Azure infrastructure
