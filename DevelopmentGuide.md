# Development Guide

Implementation-level reference for this codebase: code templates, the full CQRS/EF Core/logging mechanics. For architecture and coding philosophy, start at [CLAUDE.md](CLAUDE.md). For auth specifics see [AuthenticationGuide.md](AuthenticationGuide.md); for migration commands see [DatabaseMigrations.md](DatabaseMigrations.md).

## Adding a New Feature

Follow this checklist when adding a new feature to `Features/YourFeature/`. The example below uses a `User` create flow — the same shape applies to any command/query (e.g. a sensor reading ingestion command).

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
> Handlers are **not** auto-discovered — forgetting this step is the most common cause of a "No handler registered" runtime error.

**7. Update Program.cs** to register the validator
```csharp
builder.Services.AddFastEndpoints(options =>
    options.Assemblies = [typeof(Features.YourFeature.CreateUserCommand).Assembly]);

// FastEndpoints will auto-discover validators implementing Validator<T>
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

`IDispatcher` (`Application/CQRS/IDispatcher.cs`) has no hand-written implementation. It's generated at compile time by `Cqrs.SourceGenerator` (`src/Generators/Cqrs.SourceGenerator/`, referenced as an `OutputItemType="Analyzer"` project reference from `Features.csproj`) — there's nothing to hand-wire and no reflection involved.

**How it works:**
1. `DispatcherGenerator` (an `IIncrementalGenerator`) scans the Features compilation for every non-abstract class implementing `ICommandHandler<TCommand, TResult>` or `IQueryHandler<TQuery, TResult>`.
2. For each one found, it emits a `case TCommand typed:` branch in a `switch` statement — a plain type-check (`isinst` in IL), not reflection.
3. It emits two files into the `Features.Generated` namespace:
   - `GeneratedDispatcher.g.cs` — the `IDispatcher` implementation: `Send<TResult>` switches on the concrete command/query type and calls `_serviceProvider.GetRequiredService<ICommandHandler<TCommand, TResult>>().Handle(...)` directly.
   - `GeneratedCqrsRegistrations.g.cs` — an `IServiceCollection.AddGeneratedCqrsHandlers()` extension that registers the dispatcher itself plus every discovered handler.
4. `Features/ServiceCollectionExtensions.cs` calls `services.AddGeneratedCqrsHandlers();` — a single line, regardless of how many handlers exist.

**Practical effect:** adding a new `ICommandHandler`/`IQueryHandler` anywhere in `Features` is picked up automatically on the next build — no manual `AddScoped<...>()` line, no manual dispatcher case, nothing to forget. The generated files aren't checked in; inspect them via `dotnet build /p:EmitCompilerGeneratedFiles=true /p:CompilerGeneratedFilesOutputPath=generated` on `Features.csproj` if you need to see the actual output.

The single reference-type cast inside the generated dispatcher (`(Task<TResult>)(object)task`) is not reflection — it's a coercion the generator only emits inside the `switch` branch that already proves the types match, so it's always safe at that call site.

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

## Authentication in Handlers

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

See [AuthenticationGuide.md](AuthenticationGuide.md) for login flow, securing endpoints, and API Key usage.

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

Migrations live in `Infrastructure/Database/Migrations/` — see [DatabaseMigrations.md](DatabaseMigrations.md) for commands.

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

### Domain Events

Entities can raise domain events via `IDomainEventsDispatcher`:
- Implement `IDomainEvent` interface on events
- Call `RaiseDomainEvent(domainEvent)` from entity
- Events are dispatched and handled after entity operations complete

## Logging with LogMessages

We prefer structured, high-performance logging using the LoggerMessage pattern and centralized message definitions.

Why
- Avoids repeated allocation of log message templates at runtime.
- Keeps log template strings, event ids and levels in one place for consistency.
- Encourages structured property names (easy querying in observability systems).
- Centralizes translation of domain/feature events to log messages.

Rule (how to use)
- Create static `LogMessages` classes per feature (or a single centralized class for cross-cutting messages).
  - Suggested path for feature-scoped messages: `Features/<FeatureName>/Logging/LogMessages.cs`.
  - Suggested path for cross-cutting messages: `Infrastructure/Logging/LogMessages.cs`.
- Use `LoggerMessage.Define` (or `Define<T1,T2>(...)`) to create static delegates.
- Use `ILogger<T>` injected into services/handlers/endpoints and call the compiled delegate.
- Use event ids (`EventId`) and consistent message templates. Avoid logging secrets (passwords, tokens).
- Prefer structured property names (e.g. "UserId", "Email") instead of embedding them in message strings.

Request / Response logging
- We provide a lightweight middleware for request/response logging. It logs a fixed set of known attributes (Method, Path, QueryString, UserAgent, RemoteIp, Claims) in a structured way.
- Attributes to log fully are configured in `appsettings.json` / `local.settings.json` under `RequestLogging:AttributesToLog` (an array of attribute names). Any known attribute not listed there will still be present in the log but with its value replaced by the mask (default "***").
- Configuration keys (example):

```json
"RequestLogging": {
  "AttributesToLog": [ "Path", "Method", "UserId", "Email" ],
  "MaskValue": "***",
  "LogRequestBody": false,
  "LogResponseBody": false
}
```

- Known attribute names: `Path`, `Method`, `QueryString`, `UserAgent`, `RemoteIp`, `UserId`, `Email`, `Headers`.
- For performance and privacy, request/response bodies are only read when `LogRequestBody` / `LogResponseBody` are enabled; prefer `false` in production or when bodies contain sensitive data.

### Standard failure reason codes

When emitting failure events (for example `LoginFailed`, `UserCreateFailed`, or `ResponseError`) use a short, machine-friendly reason code to indicate why the operation failed. Document the codes here so consumers (alerts, dashboards, support teams) can reliably interpret logs.

Guidelines
- Use short, ASCII-only, CamelCase or PascalCase tokens (e.g. `InvalidPassword`, `UserNotFound`).
- Keep codes stable — treat them as part of the public contract for logs/alerts.
- Prefer semantic codes (why) rather than implementation details (stack traces).
- Update this table when you add new codes.

Common reason codes (suggested)

| Reason Code | Description | Suggested event / usage |
|-------------|-------------|-------------------------|
| UserNotFound | No user exists with the supplied identifier (email, username) | LoginFailed |
| InvalidPassword | Supplied password does not match the stored hash | LoginFailed |
| AccountLocked | Account locked due to repeated failed attempts or admin action | LoginFailed |
| AccountDisabled | Account disabled by administrator or system policy | LoginFailed |
| InvalidCredentials | Generic authentication failure (avoid exposing details) | LoginFailed |
| TwoFactorRequired | User requires two-factor authentication to proceed | LoginFailed / Auth flow |
| PasswordTooWeak | Password does not meet strength policy (on create/change) | CreateUser / ChangePassword |
| EmailAlreadyExists | Attempted to create a user with an email that already exists | CreateUser |
| InvalidToken | JWT or API token is invalid (signature, format) | Token validation / Auth middleware |
| ExpiredToken | JWT has expired | Token validation / Refresh flow |
| InvalidRefreshToken | Refresh token not found, revoked or malformed | RefreshToken flow |
| RefreshTokenExpired | Refresh token is expired | RefreshToken flow |
| RateLimited | Request or operation blocked due to rate limiting | Any endpoint |
| SystemError | Unexpected server-side error; use sparingly and accompany with diagnostics id | Any endpoint / ResponseError |

You can extend this list per feature; prefer adding new codes rather than overloading an existing one with multiple meanings.

Implementation notes
- Provide `Infrastructure/Logging/LogMessages.cs` with compiled logger delegates for request/response lifecycle events.
- Provide `Infrastructure/Middleware/RequestResponseLoggingMiddleware.cs` which reads `RequestLogging` configuration and emits structured logs using the `LogMessages` helpers.
- Masking rule: any known attribute not included in `AttributesToLog` will appear in the structured attributes map with the configured `MaskValue` (default `"***"`).

Do not
- Log passwords, secrets, or PII that you are not permitted to store.
- Build message strings with concatenation — use structured templates and the compiled delegates.

### EventId allocation

Keep this mapping in sync as new features are added, to avoid EventId collisions:

| Range | Area |
|-------|------|
| 1000-1099 | Users |
| 1100-1199 | Auth |
| 1200-1299 | Test |
| 2000-2099 | Infra/Logging |
