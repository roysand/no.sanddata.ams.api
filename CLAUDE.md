# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a .NET 10 ASP.NET Core Web API project following Clean Architecture with vertical slices. It implements a user management and test management system with JWT and API Key authentication.

**Key Stack:**
- Framework: ASP.NET Core 10
- REST: FastEndpoints
- CQRS: Custom implementation (no external CQRS framework dependencies)
- Database: Entity Framework Core with SQL Server
- Authentication: JWT Bearer + API Key
- Password Security: BCrypt.Net-Next
- Validation: FluentValidation (integrated with FastEndpoints pipeline)
- Mapping: Manual mappers (static classes per feature)
- Documentation: Scalar (OpenAPI/Swagger)

## Solution Structure

### Core Projects

| Project | Purpose |
|---------|---------|
| `src/api/Api.csproj` | REST API entry point and endpoints (FastEndpoints) |
| `src/Domain/Domain.csproj` | Core business entities, value objects, and domain logic |
| `src/Application/Application.csproj` | Use cases, commands, queries, business rules, interfaces, CQRS abstractions |
| `src/Infrastructure/Infrastructure.csproj` | Database, repositories, authentication, external services, DI registration |
| `src/Features/Features.csproj` | Vertical slices (Auth, Users, Test) with handlers and endpoints |
| `src/AI/AI/AI.csproj` | AI-related functionality |

### Clean Architecture Layer Rules

- **Domain** (innermost): No dependencies on other layers. Pure business logic.
- **Application**: Depends only on Domain. Defines CQRS interfaces, business rules, error types.
- **Infrastructure**: Depends on Application & Domain. Implements repositories, database, authentication.
- **Features**: Depends on Application, Infrastructure, Domain. Implements specific use cases.
- **API** (outermost): Depends on all layers. HTTP entry point and configuration.

### Architecture Pattern

**Vertical Slices**: Each feature (Auth, Users, Test) is self-contained and organized as:

```
Features/YourFeature/
  Commands/
    CreateUserCommand.cs
    UpdateUserCommand.cs
  Queries/
    GetUserQuery.cs
    GetUsersQuery.cs
  Handlers/
    CreateUserCommandHandler.cs
    UpdateUserCommandHandler.cs
    GetUserQueryHandler.cs
    GetUsersQueryHandler.cs
  Endpoints/
    CreateUserEndpoint.cs
    UpdateUserEndpoint.cs
    GetUserEndpoint.cs
    GetUsersEndpoint.cs
  Mappers/
    UserMapper.cs           // CreateUserRequest → CreateUserCommand, User → UserResponse
  Validators/
    CreateUserValidator.cs
    UpdateUserValidator.cs
    GetUserValidator.cs
  YourFeature.csproj
```

**Request Flow**: HTTP request → FastEndpoint → FluentValidation Pipeline → Handler (if valid) → Domain logic → EF Repository → Database

**Custom CQRS Pattern**: Located in `Application/CQRS/`, provides lightweight abstractions:
- `ICommand<TResult>`: Interface for mutation commands
- `ICommandHandler<TCommand, TResult>`: Interface for command handlers
- `IQuery<TResult>`: Interface for read queries
- `IQueryHandler<TQuery, TResult>`: Interface for query handlers
- `IDispatcher`: Resolves and executes handlers via dependency injection

## Getting Started

### First Time Setup

```bash
# 1. Clone and restore packages
git clone <repo-url>
cd no.sanddata.ams.api
dotnet restore

# 2. Configure local settings
# Create src/api/local.settings.json with your database connection and JWT secret
# Template provided in Configuration section below

# 3. Apply database migrations
dotnet ef database update \
  --project src/Infrastructure/Infrastructure.csproj \
  --startup-project src/api/api.csproj

# 4. Build and run
dotnet build
dotnet run --project src/api/api.csproj

# 5. Access API documentation
# Open http://localhost:5231/scalar/v1 in your browser (HTTP)
# Or https://localhost:7130/scalar/v1 (HTTPS)
```

### Quick Feature Creation Checklist

Follow this checklist when adding a new feature to `Features/YourFeature/`:

**1. Create Command** (`Features/YourFeature/Commands/CreateUserCommand.cs`)
```csharp
namespace Features.YourFeature.Commands;

public record CreateUserCommand(string Email, string Name) : ICommand<Result<CreateUserResponse>>;
public record CreateUserResponse(Guid Id, string Email);
```

**2. Create Handler** (`Features/YourFeature/Handlers/CreateUserCommandHandler.cs`)
```csharp
namespace Features.YourFeature.Handlers;

public class CreateUserCommandHandler : ICommandHandler<CreateUserCommand, Result<CreateUserResponse>>
{
    private readonly IRepository<User> _userRepository;
    
    public CreateUserCommandHandler(IRepository<User> userRepository)
    {
        _userRepository = userRepository;
    }
    
    public async Task<Result<CreateUserResponse>> Handle(CreateUserCommand command, CancellationToken ct)
    {
        // Handler only executes if validation passed in FastEndpoints pipeline
        var user = new User(command.Email, command.Name);
        await _userRepository.Insert(user);
        await _userRepository.SaveChangesAsync(ct);
        
        return Result.Success(new CreateUserResponse(user.Id, user.Email.Value));
    }
}
```

**3. Create Validator** (`Features/YourFeature/Validators/CreateUserValidator.cs`)
```csharp
namespace Features.YourFeature.Validators;

public class CreateUserValidator : Validator<CreateUserCommand>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Email must be valid");
        
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required")
            .MinimumLength(2).WithMessage("Name must be at least 2 characters");
    }
}
```

**4. Create Mapper** (`Features/YourFeature/Mappers/UserMapper.cs`)
```csharp
namespace Features.YourFeature.Mappers;

public static class UserMapper
{
    public static CreateUserCommand ToCommand(CreateUserRequest request) =>
        new CreateUserCommand(request.Email, request.Name);
    
    public static CreateUserResponse ToResponse(User user) =>
        new CreateUserResponse(user.Id, user.Email.Value);
}
```

**5. Create Endpoint** (`Features/YourFeature/Endpoints/CreateUserEndpoint.cs`)
```csharp
namespace Features.YourFeature.Endpoints;

public class CreateUserEndpoint : Endpoint<CreateUserRequest, CreateUserResponse>
{
    private readonly IDispatcher _dispatcher;
    
    public CreateUserEndpoint(IDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }
    
    public override void Configure()
    {
        Post("/api/users");
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
    }
    
    public override async Task HandleAsync(CreateUserRequest req, CancellationToken ct)
    {
        // Validation already executed by FastEndpoints before this method
        var command = UserMapper.ToCommand(req);
        var result = await _dispatcher.Send(command, ct);
        
        // Handle failure result
        if (!result.IsSuccess)
        {
            AddError(result.Error.Code, result.Error.Description);
            ThrowIfAnyErrors();
        }
        
        await SendOkAsync(UserMapper.ToResponse(result.Value), cancellation: ct);
    }
}
```

**6. Register Handler in DI** (`Infrastructure/AddInfrastructureToDI.cs`)
```csharp
public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
{
    // ... existing registrations ...
    
    // Register CQRS handlers
    services.AddScoped<ICommandHandler<CreateUserCommand, Result<CreateUserResponse>>, CreateUserCommandHandler>();
    services.AddScoped<IQueryHandler<GetUserQuery, Result<UserResponse>>, GetUserQueryHandler>();
    
    return services;
}
```

**7. Update Program.cs** to register the validator
```csharp
builder.Services.AddFastEndpoints(options =>
    options.Assemblies = [typeof(Features.YourFeature.CreateUserCommand).Assembly]);

// FastEndpoints will auto-discover validators implementing Validator<T>
```

## Common Development Commands

### Build & Run

```bash
# Build the entire solution
dotnet build

# Run the API server
# HTTP:  http://localhost:5231
# HTTPS: https://localhost:7130
dotnet run --project src/api/api.csproj

# Build in release mode
dotnet build --configuration Release
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
# Check .editorconfig compliance
dotnet format --verify-no-changes

# Apply formatting according to .editorconfig
dotnet format

# Build solution (includes analyzer warnings)
dotnet build
```

### Package Management

```bash
# Restore packages
dotnet restore

# Add a new package (updates Directory.Packages.props)
dotnet add package PackageName --project [project-path]
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

### Environment Variables

Configuration loads in this order (last wins):
1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. `local.settings.json`
4. Environment variables

### Critical Settings Template

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

### Validation Pipeline

FastEndpoints handles validation **before** the handler executes:

1. **Request reaches endpoint** → FastEndpoints validates using `Validator<TRequest>`
2. **If validation fails** → FastEndpoints returns 400 Bad Request with errors
3. **If validation passes** → Handler executes, can assume input is valid

**Never validate twice** - no redundant checks in handlers.

### Error Handling Pattern

Handlers return `Result<T>` for domain/business errors:

```csharp
public async Task<Result<UserResponse>> Handle(GetUserQuery query, CancellationToken ct)
{
    var user = await _userRepository.GetAsync(query.Id, ct);
    
    if (user is null)
    {
        // Domain error - return Result.Failure, don't throw
        return Result.Failure<UserResponse>(
            Error.NotFound("User.NotFound", "User not found"));
    }
    
    return Result.Success(new UserResponse(user.Id, user.Email.Value));
}
```

**In Endpoint**, check result and communicate to client:
```csharp
var result = await _dispatcher.Send(query, ct);

if (!result.IsSuccess)
{
    AddError(result.Error.Code, result.Error.Description);
    ThrowIfAnyErrors();  // Sends error response to client
}

await SendOkAsync(result.Value, cancellation: ct);
```

### Error Type Examples

```csharp
Error.NotFound("User.NotFound", "User not found")
Error.Conflict("User.EmailExists", "Email already in use")
Error.Unauthorized("Auth.InvalidCredentials", "Invalid email or password")
Error.BadRequest("Validation.InvalidInput", "Input validation failed")
Error.InternalServerError("System.Exception", "An unexpected error occurred")
```

## Authentication

### Two Authentication Schemes

1. **JWT Bearer Token** - User login with role-based access
   - Endpoint: `POST /api/auth/login`
   - Header: `Authorization: Bearer <token>`
   - Expires: 6 hours by default
   - Claims: NameIdentifier, Email, Name, FirstName, LastName, Role (multiple)

2. **API Key** - External system integration
   - Header: `X-API-Key: <api-key>`
   - Stored in database, can have expiration

### Securing Endpoints

```csharp
// Open to anyone
public override void Configure()
{
    Get("/api/public");
    AllowAnonymous();
}

// Requires valid JWT
public override void Configure()
{
    Get("/api/secure");
    AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
}

// Requires Admin role
public override void Configure()
{
    Delete("/api/users/{id}");
    AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
    Roles("Admin");
}

// Multiple roles (any match)
Roles("Admin", "Manager");

// Accepts JWT OR API Key
AuthSchemes(JwtBearerDefaults.AuthenticationScheme, "ApiKey");
```

### Accessing User Info in Handlers

```csharp
public class MyCommandHandler : ICommandHandler<MyCommand, Result<MyResponse>>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    
    public MyCommandHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }
    
    public async Task<Result<MyResponse>> Handle(MyCommand command, CancellationToken ct)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var userId = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userEmail = user?.FindFirst(ClaimTypes.Email)?.Value;
        var isAdmin = user?.IsInRole("Admin") ?? false;
        
        // Use user context...
    }
}
```

## CQRS Implementation Details

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

// Query: Reads state, returns result (no side effects)
public interface IQuery<out TResult>
{
}

public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
{
    Task<TResult> Handle(TQuery query, CancellationToken cancellationToken);
}
```

### Custom Dispatcher

```csharp
// Application/CQRS/IDispatcher.cs
public interface IDispatcher
{
    Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken cancellationToken);
    Task<TResult> Send<TResult>(IQuery<TResult> query, CancellationToken cancellationToken);
}

// Application/CQRS/Dispatcher.cs
public class Dispatcher : IDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    
    public Dispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }
    
    public async Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken cancellationToken)
    {
        var handlerType = typeof(ICommandHandler<,>).MakeGenericType(command.GetType(), typeof(TResult));
        var handler = _serviceProvider.GetService(handlerType);
        
        if (handler is null)
            throw new InvalidOperationException($"No handler registered for {command.GetType().Name}");
        
        var handleMethod = handlerType.GetMethod("Handle");
        return await (Task<TResult>)handleMethod!.Invoke(handler, [command, cancellationToken])!;
    }
    
    public async Task<TResult> Send<TResult>(IQuery<TResult> query, CancellationToken cancellationToken)
    {
        var handlerType = typeof(IQueryHandler<,>).MakeGenericType(query.GetType(), typeof(TResult));
        var handler = _serviceProvider.GetService(handlerType);
        
        if (handler is null)
            throw new InvalidOperationException($"No handler registered for {query.GetType().Name}");
        
        var handleMethod = handlerType.GetMethod("Handle");
        return await (Task<TResult>)handleMethod!.Invoke(handler, [query, cancellationToken])!;
    }
}

// Register in Program.cs
builder.Services.AddScoped<IDispatcher, Dispatcher>();
```

### Manual Mappers

Mappers are static classes within each feature folder, converting between DTOs and domain entities:

```csharp
// Features/YourFeature/Mappers/UserMapper.cs
namespace Features.YourFeature.Mappers;

public static class UserMapper
{
    // Request → Command
    public static CreateUserCommand ToCreateCommand(CreateUserRequest request) =>
        new CreateUserCommand(request.Email, request.Name);
    
    public static UpdateUserCommand ToUpdateCommand(string id, UpdateUserRequest request) =>
        new UpdateUserCommand(Guid.Parse(id), request.Email, request.Name);
    
    // Domain Entity → Response
    public static UserResponse ToResponse(User user) =>
        new UserResponse(
            Id: user.Id,
            Email: user.Email.Value,
            Name: user.Name,
            Roles: user.Roles.Select(r => r.Name).ToList());
    
    public static UserListResponse ToListResponse(User user) =>
        new UserListResponse(user.Id, user.Email.Value, user.Name);
}
```

**Mapper Guidelines:**
- Keep mappers simple and focused on conversion only
- Place in `Mappers/` folder within the feature
- Use extension methods for complex transformations
- Name clearly: `To<TargetType>` or `<SourceType>To<TargetType>`

## Entity Framework Core Patterns

### Repository Pattern

Generic repository at `Infrastructure/Database/Repositories/GenericEfRepository.cs`:

```csharp
// Inject IRepository<Entity> in handlers
public class GetUserQueryHandler : IQueryHandler<GetUserQuery, Result<UserResponse>>
{
    private readonly IRepository<User> _userRepository;
    
    public GetUserQueryHandler(IRepository<User> userRepository)
    {
        _userRepository = userRepository;
    }
    
    public async Task<Result<UserResponse>> Handle(GetUserQuery query, CancellationToken ct)
    {
        var user = await _userRepository.GetAsync(query.Id, ct);
        if (user is null)
            return Result.Failure<UserResponse>(Error.NotFound("User.NotFound", "User not found"));
        
        return Result.Success(UserMapper.ToResponse(user));
    }
}
```

### DbContext Location

`Infrastructure/Database/ApplicationDbContext.cs` - Defines all entity mappings, configurations, and DbSets.

### Migrations

Migrations live in `Infrastructure/Database/Migrations/`. Create migrations after entity changes:

```bash
dotnet ef migrations add DescriptiveName \
  --project src/Infrastructure/Infrastructure.csproj \
  --startup-project src/api/api.csproj \
  --output-dir Database/Migrations
```

### Value Objects

Domain uses value objects (e.g., `EmailAddress`) that encapsulate validation:
- Located in `Domain/Common/ValueObjects/`
- Implement equality and validation logic
- Used in entities to enforce domain rules

```csharp
// Domain/Common/ValueObjects/EmailAddress.cs
public class EmailAddress : ValueObject
{
    public string Value { get; }
    
    public EmailAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Contains("@"))
            throw new DomainException("Invalid email address");
        
        Value = value;
    }
}
```

## Code Standards

These are enforced by `.editorconfig`:

- **Nullable reference types**: Enabled (`<Nullable>enable</Nullable>`)
- **Implicit usings**: Enabled (`<ImplicitUsings>enable</ImplicitUsings>`)
- **Naming conventions**: PascalCase for public members, camelCase for private
- **Indentation**: 4 spaces (not tabs)
- **Line length**: Soft limit 120 characters

**Central Package Management**: Always update `Directory.Packages.props` with new package versions, NOT individual `.csproj` files.

## Important Patterns & Conventions

### Result Pattern

All handlers return `Result<T>` for explicit, testable error handling:

```csharp
// Success
return Result.Success(data);

// Domain/business error
return Result.Failure<T>(Error.NotFound("User.NotFound", "User not found"));

// In endpoint, check and respond
if (!result.IsSuccess)
{
    AddError(result.Error.Code, result.Error.Description);
    ThrowIfAnyErrors();
}
```

**Never throw exceptions for expected business errors** - use Result pattern instead. This keeps error flows explicit and testable.

### Domain Events

Entities can raise domain events via `IDomainEventsDispatcher`:
- Implement `IDomainEvent` interface on events
- Call `RaiseDomainEvent(domainEvent)` from entity
- Events are dispatched and handled after entity operations complete

## Docker & Deployment

```bash
# Build docker image
docker build -t ams-api:latest -f Dockerfile .

# Run with docker compose (v2)
docker compose -f compose.yaml up

# Push to registry
docker tag ams-api:latest myregistry/ams-api:latest
docker push myregistry/ams-api:latest
```

## Key Concepts & Gotchas

### Vertical Slices
Each feature is self-contained with its own commands, queries, handlers, endpoints, validators, and mappers. Changing a shared type requires updates in multiple features—this is by design to maximize feature independence.

### Commands vs Queries
- **Commands**: Mutate state (Create, Update, Delete). Always return `Result<T>`.
- **Queries**: Read-only. No side effects. Always return `Result<T>`.

### Handler Registration
Every handler must be manually registered in `Infrastructure/AddInfrastructureToDI.cs`:
```csharp
services.AddScoped<ICommandHandler<CreateUserCommand, Result<CreateUserResponse>>, CreateUserCommandHandler>();
```

### Package Management
Versions are defined centrally in `Directory.Packages.props`. Never hardcode versions in individual `.csproj` files.

### Database Migrations
Always test migrations in development before production. Use `--idempotent` flag for production scripts so they're safe to run multiple times.

### Validation Before Handler Execution
FastEndpoints validates requests using `Validator<TRequest>` **before** the handler is invoked. Never validate again inside the handler—assume input is valid if execution reaches that point.

## References

- **FastEndpoints**: https://fast-endpoints.com/
- **Entity Framework Core**: https://learn.microsoft.com/en-us/ef/core/
- **ASP.NET Core Authentication**: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/
- **JWT Best Practices**: https://tools.ietf.org/html/rfc8725
- **CQRS Pattern**: https://learn.microsoft.com/en-us/azure/architecture/patterns/cqrs
- **Clean Architecture**: https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html

## Feature Documentation

Feature-specific guides are available:
- [AuthenticationGuide.md](AuthenticationGuide.md) - Auth system details, security practices, testing
- [DatabaseMigrations.md](DatabaseMigrations.md) - EF Core migration commands and best practices
- [UserCrudEndpoints.md](UserCrudEndpoints.md) - User management API endpoints
