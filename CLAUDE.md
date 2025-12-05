# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a .NET 10 ASP.NET Core Web API project following Clean Architecture with vertical slices. It implements a user management and test management system with JWT and API Key authentication.

**Key Stack:**
- Framework: ASP.NET Core 10
- REST: FastEndpoints
- CQRS: Custom implementation (no external dependencies)
- Database: Entity Framework Core with SQL Server
- Authentication: JWT Bearer + API Key
- Password Security: BCrypt.Net-Next
- Validation: FluentValidation
- Mapping: Manual mappers (static classes per feature)
- Documentation: Scalar (OpenAPI/Swagger)

## Solution Structure

### Core Projects

| Project | Purpose |
|---------|---------|
| `src/api/Api.csproj` | REST API entry point and endpoints (FastEndpoints) |
| `src/Domain/Domain.csproj` | Core business entities, value objects, and domain logic |
| `src/Application/Application.csproj` | Use cases, commands, queries, business rules, interfaces |
| `src/Infrastructure/Infrastructure.csproj` | Database, repositories, authentication, external services |
| `src/Features/Features.csproj` | Vertical slices (Auth, Users, Test) with handlers and endpoints |
| `src/AI/AI/AI.csproj` | AI-related functionality |

### Architecture Pattern

**Vertical Slices**: Each feature (Auth, Users, Test) contains:
- **Commands/Queries**: Request/Response DTOs implementing `ICommand<TResult>` or `IQuery<TResult>`
- **Handler**: Business logic implementing `ICommandHandler<TCommand, TResult>` or `IQueryHandler<TQuery, TResult>`
- **Endpoint**: FastEndpoints HTTP handler that routes requests to the custom dispatcher
- **Mappers**: Static mapper classes for DTO ↔ Entity conversions
- **Validator**: FluentValidation rules

Example flow: HTTP request → FastEndpoint → Dispatcher → Handler → Domain logic → EF Repository → Database

**Custom CQRS Dispatcher**: Located at `Application/CQRS/Dispatcher.cs`, handles command/query routing via dependency injection. No external CQRS framework—lightweight and explicit.

## Common Development Commands

### Build & Run

```bash
# Build the entire solution
dotnet build

# Run the API server (starts on HTTPS port 5231 by default)
dotnet run --project src/api/api.csproj

# Build in release mode
dotnet build --configuration Release
```

### Testing & Verification

```bash
# Run all tests in solution
dotnet test

# Run specific test project
dotnet test src/Tests/Tests.csproj

# Run with coverage
dotnet test /p:CollectCoverage=true
```

### Database & Migrations

```bash
# Create a new migration after changing entities
dotnet ef migrations add MigrationName \
  --project src/Infrastructure/Infrastructure.csproj \
  --startup-project src/api/api.csproj \
  --output-dir Database/Migrations

# Apply pending migrations
dotnet ef database update \
  --project src/Infrastructure/Infrastructure.csproj \
  --startup-project src/api/api.csproj

# List all migrations
dotnet ef migrations list \
  --project src/Infrastructure/Infrastructure.csproj \
  --startup-project src/api/api.csproj

# Generate SQL script without applying (for production)
dotnet ef migrations script \
  --project src/Infrastructure/Infrastructure.csproj \
  --startup-project src/api/api.csproj \
  --output migration.sql \
  --idempotent
```

### Code Quality

```bash
# Run code analysis
dotnet analyzers [project]

# Check .editorconfig compliance
dotnet format --verify-no-changes

# Apply formatting according to .editorconfig
dotnet format
```

### Package Management

```bash
# Restore packages (needed if Directory.Packages.props changes)
dotnet restore

# Add a new package (updates Directory.Packages.props)
dotnet add package PackageName --project [project-path]

# List outdated packages
dotnet outdated [project]
```

## API Documentation & Testing

**OpenAPI/Swagger URL** (development only):
- http://localhost:5231/scalar/v1 (Scalar - modern interactive docs)
- http://localhost:5231/openapi/v1.json (OpenAPI specification)

**Authentication in API docs:**
- JWT: Click the lock icon, select "Bearer", paste your token
- API Key: Click the lock icon, select "ApiKey", paste your API key value

**Local testing commands:**

```bash
# Login and get JWT token
curl -X POST http://localhost:5231/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"user@example.com","password":"password123"}'

# Use token in subsequent requests
TOKEN="your-jwt-token-here"
curl -X GET http://localhost:5231/api/users/profile \
  -H "Authorization: Bearer $TOKEN"

# Test API Key authentication
curl -X GET http://localhost:5231/api/external/data \
  -H "X-API-Key: your-api-key-here"
```

## Configuration

### Key Configuration Files

| File | Purpose |
|------|---------|
| `appsettings.json` | Default configuration (development defaults) |
| `local.settings.json` | Local overrides (git-ignored, NOT in source control) |
| `Directory.Packages.props` | Central NuGet package version management |
| `Directory.Build.props` | Shared project properties (nullable refs, implicit usings) |
| `.editorconfig` | Code formatting and style rules |
| `.cursorrules` | Cursor AI assistant guidelines |

### Environment Variables

Configuration loads in this order (last wins):
1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. `local.settings.json`
4. Environment variables

**Critical settings in `local.settings.json`:**

```json
{
  "ApplicationSettings": {
    "DbConnectionString": "Server=localhost;Database=AmsDb;..."
  },
  "JwtSettings": {
    "SecretKey": "your-secret-key-at-least-32-chars-long"
  }
}
```

## Authentication Deep Dive

### Two Authentication Schemes

1. **JWT Bearer Token**: User login, role-based access
   - Endpoint: `POST /api/auth/login`
   - Header: `Authorization: Bearer <token>`
   - Expires: 8 hours by default

2. **API Key**: External system integration
   - Header: `X-API-Key: <api-key>`
   - Stored in database, can have expiration

### Securing Endpoints

```csharp
// Open to anyone
public class PublicEndpoint : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/api/public");
        AllowAnonymous();
    }
}

// Requires valid JWT
public class SecureEndpoint : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Get("/api/secure");
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
    }
}

// Requires Admin role
public class AdminEndpoint : Endpoint<Request, Response>
{
    public override void Configure()
    {
        Delete("/api/users/{id}");
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles("Admin");
    }
}

// Multiple roles (any match)
AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
Roles("Admin", "Manager");

// Accepts JWT OR API Key
AuthSchemes(JwtBearerDefaults.AuthenticationScheme, "ApiKey");
```

### Accessing User Info in Handlers

```csharp
// In command/query handler via IHttpContextAccessor
private readonly IHttpContextAccessor _httpContextAccessor;

public async Task<Result<Response>> Handle(MyCommand command, CancellationToken ct)
{
    var user = _httpContextAccessor.HttpContext?.User;
    var userId = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    var userEmail = user?.FindFirst(ClaimTypes.Email)?.Value;
    var roles = user?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
    
    var isAdmin = user?.IsInRole("Admin") ?? false;
}
```

## CQRS Implementation

### Custom CQRS Interfaces

Located in `Application/CQRS/`:

```csharp
// Command: Mutates state, returns result
public interface ICommand<out TResult>
{
}

public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{
    Task<TResult> Handle(TCommand command, CancellationToken cancellationToken);
}

// Query: Reads state, returns result
public interface IQuery<out TResult>
{
}

public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
{
    Task<TResult> Handle(TQuery query, CancellationToken cancellationToken);
}
```

### Custom Dispatcher

The dispatcher at `Application/CQRS/Dispatcher.cs` resolves and invokes handlers:

```csharp
public interface IDispatcher
{
    Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken cancellationToken);
    Task<TResult> Send<TResult>(IQuery<TResult> query, CancellationToken cancellationToken);
}

// Usage in endpoints:
public class CreateUserEndpoint : Endpoint<CreateUserRequest, CreateUserResponse>
{
    private readonly IDispatcher _dispatcher;
    
    public override async Task HandleAsync(CreateUserRequest req, CancellationToken ct)
    {
        var command = new CreateUserCommand(req.Email, req.Name);
        var result = await _dispatcher.Send(command, ct);
        // Handle result...
    }
}
```

### Feature Folder Structure

Each feature is organized as a vertical slice:

```
Features/
  Auth/
    Commands/
      LoginCommand.cs
      RegisterCommand.cs
    Handlers/
      LoginCommandHandler.cs
      RegisterCommandHandler.cs
    Mappers/
      LoginMapper.cs          // Static mapper: LoginCommand -> User
      LoginResponseMapper.cs  // Static mapper: User -> LoginResponse
    Endpoints/
      LoginEndpoint.cs
      RegisterEndpoint.cs
    Validators/
      LoginValidator.cs
    Auth.csproj
  Users/
    Commands/
    Queries/
    Handlers/
    Mappers/
    Endpoints/
    Validators/
    Users.csproj
  Test/
    Commands/
    Queries/
    Handlers/
    Mappers/
    Endpoints/
    Validators/
    Test.csproj
```

### Manual Mappers

Mappers are static classes kept within each feature for simplicity and locality:

```csharp
// Features/Auth/Mappers/LoginMapper.cs
namespace Features.Auth.Mappers;

public static class LoginMapper
{
    public static LoginCommand ToCommand(LoginRequest request) =>
        new LoginCommand(request.Email, request.Password);
    
    public static LoginResponse ToResponse(User user, string token) =>
        new LoginResponse(user.Email.Value, token, user.Roles.Select(r => r.Name).ToList());
}

// Usage in handler:
var command = LoginMapper.ToCommand(request);
var result = await _dispatcher.Send(command, ct);
var response = LoginMapper.ToResponse(result.User, result.Token);
```

**Mapper Guidelines:**
- Keep mappers as static classes within the feature folder
- Simple, focused conversion logic
- Use extension methods for complex entity transformations
- Name by the types being converted: `<SourceType>To<TargetType>` or `ToCommand`, `ToResponse`

## Entity Framework Core Patterns

### Repository Pattern

Generic repository at `Infrastructure/Database/Repositories/GenericEfRepository.cs`:

```csharp
// Inject IRepository<Entity> in handlers
var entity = await _repository.GetAsync(id, cancellationToken);
await _repository.Insert(entity);
await _repository.SaveChangesAsync(cancellationToken);
```

### DbContext Location

`Infrastructure/Database/ApplicationDbContext.cs` - Defines all entity mappings and configurations.

### Value Objects

Domain uses value objects (e.g., `EmailAddress`) that encapsulate validation:
- Located in `Domain/Common/ValueObjects/`
- Implement equality and validation logic
- Used in entities to enforce domain rules

## Code Standards

These are enforced by `.editorconfig`:

- **Nullable reference types**: Enabled (`<Nullable>enable</Nullable>`)
- **Implicit usings**: Enabled (`<ImplicitUsings>enable</ImplicitUsings>`)
- **Naming conventions**: PascalCase for public members, camelCase for private
- **Indentation**: 4 spaces (not tabs)
- **Line length**: Soft limit 120 characters

When adding dependencies: Always update `Directory.Packages.props` with the version, NOT individual `.csproj` files.

## Important Patterns & Conventions

### Result Pattern

Handlers return `Result<T>` or `Result` for operation outcomes:

```csharp
// Success
return Result.Success(data);

// Failure with error details
return Result.Failure<T>(
    Error.NotFound("User.NotFound", "User not found"));

// In endpoint, check result
if (!result.IsSuccess)
{
    AddError(result.Error.Code, result.Error.Description);
    ThrowIfAnyErrors();
}
```

### Domain Events

Entities can raise domain events via `DomainEventsDispatcher`:
- Implement `IDomainEvent` interface
- Call `RaiseDomainEvent()` from entity
- Events dispatched after entity operations complete

### FastEndpoints vs Custom CQRS

**FastEndpoints (Endpoints):** HTTP routing, request validation, authentication/authorization configuration, request/response mapping

**Custom CQRS (Commands/Queries + Handlers):** Core business logic, database operations, domain rules

Endpoint delegates to handler via `IDispatcher.Send()` with explicit command or query object.

## Compiler Warnings & Code Quality

The project uses strict compiler settings:
- Warning as errors enabled
- Nullable reference types enforced
- Unused variable detection

The `.editorconfig` file enforces consistent code style across the team. Run `dotnet format` to auto-fix style issues.

## Docker & Deployment

```bash
# Build docker image
docker build -t ams-api:latest -f Dockerfile .

# Run with compose.yaml (includes database setup)
docker-compose -f compose.yaml up

# Push to registry
docker tag ams-api:latest myregistry/ams-api:latest
docker push myregistry/ams-api:latest
```

## Key Concepts & Gotchas

### Vertical Slices
Each feature is self-contained with its own request/response types. This reduces cross-cutting concerns but means changing a shared type requires updates in multiple features.

### Package Management
Versions are defined centrally in `Directory.Packages.props`. If you see a version error, check there first. Never hardcode package versions in individual `.csproj` files.

### Database Migrations
Always test migrations in a development environment before applying to production. Use `--idempotent` flag for production scripts so they're safe to run multiple times.

### Authentication Claims
JWT tokens include: `NameIdentifier` (userId), `Email`, `Name`, `FirstName`, `LastName`, `Role` (multiple).
API Key stores user info in database; lookup by key to get user context.

### Result Pattern vs Exceptions

Handlers return `Result<T>` or `Result` for operation outcomes:

```csharp
// Command handler example
public class CreateUserCommandHandler : ICommandHandler<CreateUserCommand, Result<User>>
{
    public async Task<Result<User>> Handle(CreateUserCommand command, CancellationToken ct)
    {
        var existingUser = await _repository.GetByEmailAsync(command.Email, ct);
        if (existingUser != null)
        {
            return Result.Failure<User>(
                Error.Conflict("User.EmailExists", "Email already in use"));
        }
        
        var user = new User(command.Email, command.Name);
        await _repository.Insert(user);
        await _repository.SaveChangesAsync(ct);
        
        return Result.Success(user);
    }
}
```

Business rule violations return `Result.Failure`, not exceptions. This keeps error flows explicit and testable.

## References

- **FastEndpoints**: https://fast-endpoints.com/
- **Entity Framework Core**: https://learn.microsoft.com/en-us/ef/core/
- **ASP.NET Core Authentication**: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/
- **JWT Best Practices**: https://tools.ietf.org/html/rfc8725
- **CQRS Pattern**: https://learn.microsoft.com/en-us/azure/architecture/patterns/cqrs

## Feature Documentation

Feature-specific guides are available:
- [AuthenticationGuide.md](AuthenticationGuide.md) - Auth system details, security practices, testing
- [DatabaseMigrations.md](DatabaseMigrations.md) - EF Core migration commands and best practices
- [UserCrudEndpoints.md](UserCrudEndpoints.md) - User management API endpoints
