# Authentication & Authorization Guide

This guide explains how to use the authentication and authorization system in the AMS API project.

## Overview

The API supports two authentication methods:
1. **JWT Bearer Token** - For user authentication with role-based authorization
2. **API Key** - For external system integration

## Authentication Methods

### 1. JWT Bearer Token Authentication

Used for user login and role-based access control.

#### Login Endpoint

**POST** `/api/auth/login`

**Request:**
```json
{
  "email": "user@example.com",
  "password": "password123"
}
```

**Response:**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "email": "user@example.com",
  "roles": ["Admin", "User"]
}
```

**Using the Token:**
```http
GET /api/users/profile
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

### 2. API Key Authentication

Used for external systems and service-to-service communication.

**Using API Key:**
```http
GET /api/external/data
X-API-Key: your-api-key-here
```

## Securing Endpoints

### Open Endpoint (No Authentication)

```csharp
public class PublicEndpoint : Endpoint<MyRequest, MyResponse>
{
    public override void Configure()
    {
        Get("/api/public/data");
        AllowAnonymous();  // Anyone can access
    }
}
```

### JWT Authentication Required

```csharp
public class SecureEndpoint : Endpoint<MyRequest, MyResponse>
{
    public override void Configure()
    {
        Get("/api/users/profile");
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);  // Requires valid JWT
    }
}
```

### Role-Based Authorization

```csharp
public class AdminEndpoint : Endpoint<MyRequest, MyResponse>
{
    public override void Configure()
    {
        Delete("/api/users/{id}");
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles("Admin");  // Only users with Admin role can access
    }
}
```

### Multiple Roles (Any of)

```csharp
public class ModeratorEndpoint : Endpoint<MyRequest, MyResponse>
{
    public override void Configure()
    {
        Put("/api/content/{id}");
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles("Admin", "Moderator");  // Admin OR Moderator can access
    }
}
```

### API Key Authentication

```csharp
public class ExternalEndpoint : Endpoint<MyRequest, MyResponse>
{
    public override void Configure()
    {
        Post("/api/external/data");
        AuthSchemes("ApiKey");  // Requires valid API Key in X-API-Key header
    }
}
```

### Multiple Authentication Schemes (JWT OR API Key)

```csharp
public class FlexibleEndpoint : Endpoint<MyRequest, MyResponse>
{
    public override void Configure()
    {
        Get("/api/data");
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme, "ApiKey");  // Either JWT or API Key
    }
}
```

## Accessing User Information in Endpoints

### Get Current User Information

```csharp
public class MyEndpoint : Endpoint<MyRequest, MyResponse>
{
    public override async Task HandleAsync(MyRequest req, CancellationToken ct)
    {
        // Get user ID
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        
        // Get user email
        var userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
        
        // Get user name
        var userName = User.FindFirst(ClaimTypes.Name)?.Value;
        
        // Get all user roles
        var userRoles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        
        // Check if user has specific role
        var isAdmin = User.IsInRole("Admin");
        
        // For API Key authentication
        var apiKey = User.FindFirst("ApiKey")?.Value;
        
        // Use the information...
    }
}
```

### Get User Information in Handlers

```csharp
public class MyHandler : IRequestHandler<MyRequest, Result<MyResponse>>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    
    public MyHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }
    
    public async Task<Result<MyResponse>> Handle(MyRequest request, CancellationToken cancellationToken)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var userId = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        
        // Use the information...
    }
}
```

## Configuration

### appsettings.json

```json
{
  "JwtSettings": {
    "SecretKey": "your-secret-key-must-be-at-least-32-characters-long",
    "Issuer": "AmsApi",
    "Audience": "AmsApiClients"
  }
}
```

### local.settings.json (Not in source control)

```json
{
  "JwtSettings": {
    "SecretKey": "your-production-secret-key-here"
  }
}
```

**IMPORTANT:** 
- The `SecretKey` must be at least 32 characters long
- Use different keys for development and production
- Never commit production secrets to source control
- Store production secrets in environment variables or Azure Key Vault

## Testing Authentication

### 1. Test Login Endpoint

```http
POST http://localhost:5231/api/auth/login
Content-Type: application/json

{
  "email": "admin@example.com",
  "password": "Admin123!"
}
```

### 2. Use Returned Token

```http
GET http://localhost:5231/api/users/profile
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

### 3. Test API Key

```http
GET http://localhost:5231/api/external/data
X-API-Key: your-api-key-from-database
```

## Common Scenarios

### Scenario 1: Public API with Some Protected Endpoints

```csharp
// Public endpoint
public class GetProductsEndpoint : Endpoint<EmptyRequest, ProductsResponse>
{
    public override void Configure()
    {
        Get("/api/products");
        AllowAnonymous();
    }
}

// Protected endpoint
public class CreateProductEndpoint : Endpoint<CreateProductRequest, ProductResponse>
{
    public override void Configure()
    {
        Post("/api/products");
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles("Admin", "ProductManager");
    }
}
```

### Scenario 2: External API Integration

```csharp
public class WebhookEndpoint : Endpoint<WebhookRequest, WebhookResponse>
{
    public override void Configure()
    {
        Post("/api/webhooks/external");
        AuthSchemes("ApiKey");  // External systems use API Key
    }
}
```

### Scenario 3: Flexible Authentication

```csharp
public class DataEndpoint : Endpoint<DataRequest, DataResponse>
{
    public override void Configure()
    {
        Get("/api/data");
        // Accept either JWT (for users) or API Key (for external systems)
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme, "ApiKey");
    }
}
```

## Security Best Practices

### 1. Password Hashing

**TODO:** Implement password hashing in the Login handler:

```csharp
// Install: dotnet add package BCrypt.Net-Next

// When creating user
var passwordHash = BCrypt.Net.BCrypt.HashPassword(plainTextPassword);

// When verifying login
if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
{
    return Result.Failure<LoginResponse>(
        Error.NotFound("Auth.InvalidCredentials", "Invalid email or password"));
}
```

### 2. Token Expiration

Tokens expire after 8 hours by default. Configure in [`JwtTokenService.cs`](Src/Infrastructure/Authentication/JwtTokenService.cs:1):

```csharp
expires: DateTime.UtcNow.AddHours(8)  // Adjust as needed
```

### 3. API Key Management

- Store API keys securely in the database
- Set expiration dates for API keys
- Implement API key rotation
- Monitor API key usage
- Revoke compromised keys immediately

### 4. HTTPS Only

Always use HTTPS in production:

```csharp
// In Program.cs
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseHttpsRedirection();
```

## Troubleshooting

### 401 Unauthorized

**Causes:**
- Missing or invalid token
- Expired token
- Invalid API key
- Token signature validation failed

**Solutions:**
- Check if token is included in Authorization header
- Verify token hasn't expired
- Ensure SecretKey matches between token generation and validation
- For API Key: verify key exists in database and is active

### 403 Forbidden

**Causes:**
- User doesn't have required role
- User account is inactive

**Solutions:**
- Check user roles in database
- Verify role name matches exactly (case-sensitive)
- Ensure user.IsActive is true

### Token Validation Errors

**Common Issues:**
- SecretKey too short (must be ≥32 characters)
- Issuer/Audience mismatch
- Clock skew issues

**Solutions:**
- Use a strong, long secret key
- Ensure Issuer and Audience match in configuration and validation
- Set ClockSkew to TimeSpan.Zero for strict validation

## Example: Complete Feature with Authentication

```csharp
namespace Features.Users;

public static class GetUserProfile
{
    public record Request : IRequest<Result<Response>>;
    
    public record Response(string Email, string Name, string[] Roles);

    internal sealed class Handler : IRequestHandler<Request, Result<Response>>
    {
        private readonly IUserEfRepository<User> _userRepository;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public Handler(
            IUserEfRepository<User> userRepository,
            IHttpContextAccessor httpContextAccessor)
        {
            _userRepository = userRepository;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<Result<Response>> Handle(Request request, CancellationToken cancellationToken)
        {
            var userId = _httpContextAccessor.HttpContext?.User
                .FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var userGuid))
            {
                return Result.Failure<Response>(
                    Error.NotFound("User.NotFound", "User not found"));
            }

            var user = await _userRepository.GetAsync(userGuid, cancellationToken);
            if (user == null)
            {
                return Result.Failure<Response>(
                    Error.NotFound("User.NotFound", "User not found"));
            }

            var roles = user.Roles.Select(r => r.Name).ToArray();
            return Result.Success(new Response(
                user.Email.Value,
                $"{user.FirstName} {user.LastName}",
                roles));
        }
    }
}

public class GetUserProfileEndpoint : Endpoint<GetUserProfile.Request, GetUserProfile.Response>
{
    private readonly ISender _sender;

    public GetUserProfileEndpoint(ISender sender) => _sender = sender;

    public override void Configure()
    {
        Get("/api/users/profile");
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);  // Requires JWT
        Summary(s =>
        {
            s.Summary = "Get current user profile";
            s.Description = "Returns the profile of the authenticated user";
        });
    }

    public override async Task<GetUserProfile.Response> HandleAsync(
        GetUserProfile.Request req,
        CancellationToken ct)
    {
        var result = await _sender.Send(req, ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Code, result.Error.Description);
            ThrowIfAnyErrors();
        }

        return result.Value;
    }
}
```

## API Testing with Authentication

### Using curl

```bash
# Login
curl -X POST http://localhost:5231/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"user@example.com","password":"password123"}'

# Use token
TOKEN="eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
curl -X GET http://localhost:5231/api/users/profile \
  -H "Authorization: Bearer $TOKEN"

# Use API Key
curl -X GET http://localhost:5231/api/external/data \
  -H "X-API-Key: your-api-key"
```

### Using Postman

1. **Login:**
   - Method: POST
   - URL: `http://localhost:5231/api/auth/login`
   - Body (JSON):
     ```json
     {
       "email": "user@example.com",
       "password": "password123"
     }
     ```

2. **Use Token:**
   - Copy the `token` from login response
   - In subsequent requests:
     - Go to Authorization tab
     - Type: Bearer Token
     - Token: paste the token

3. **Use API Key:**
   - Go to Headers tab
   - Add header: `X-API-Key` with your API key value

### Using api.http (VS Code REST Client)

```http
### Login
POST http://localhost:5231/api/auth/login
Content-Type: application/json

{
  "email": "user@example.com",
  "password": "password123"
}

### Save token from response, then use it
@token = eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...

### Get Profile (JWT)
GET http://localhost:5231/api/users/profile
Authorization: Bearer {{token}}

### External API (API Key)
GET http://localhost:5231/api/external/data
X-API-Key: your-api-key-here
```

## Claims Available in JWT Token

When a user logs in, the following claims are included in the JWT:

| Claim Type | Description | Example |
|------------|-------------|---------|
| `ClaimTypes.NameIdentifier` | User ID (Guid) | `"123e4567-e89b-12d3-a456-426614174000"` |
| `ClaimTypes.Email` | User email | `"user@example.com"` |
| `ClaimTypes.Name` | Full name | `"John Doe"` |
| `FirstName` | First name | `"John"` |
| `LastName` | Last name | `"Doe"` |
| `ClaimTypes.Role` | User roles (multiple) | `"Admin"`, `"User"` |

## Managing API Keys

### Creating API Keys

API keys should be created through an admin endpoint or database seeding:

```csharp
var apiKey = new ApiKey(
    id: Guid.NewGuid(),
    key: GenerateSecureApiKey(),  // Use cryptographically secure random generator
    description: "External System Integration",
    isActive: true,
    expiresAt: DateTime.UtcNow.AddYears(1)
);

await _apiKeyRepository.Insert(apiKey);
await _apiKeyRepository.SaveChangesAsync(cancellationToken);
```

### Generating Secure API Keys

```csharp
public static string GenerateSecureApiKey()
{
    var bytes = new byte[32];
    using var rng = RandomNumberGenerator.Create();
    rng.GetBytes(bytes);
    return Convert.ToBase64String(bytes);
}
```

## Role Management

### Assigning Roles to Users

```csharp
var userRole = new UserRole(
    userId: user.Id,
    roleId: role.Id,
    assignedAt: DateTime.UtcNow
);

await _userRoleRepository.Insert(userRole);
await _userRoleRepository.SaveChangesAsync(cancellationToken);
```

### Common Roles

Suggested role structure:
- **Admin** - Full system access
- **Manager** - Manage users and locations
- **User** - Standard user access
- **ReadOnly** - View-only access
- **ApiUser** - For API-only access

## Security Checklist

- [ ] Implement password hashing (BCrypt recommended)
- [ ] Use HTTPS in production
- [ ] Store JWT SecretKey in secure configuration (Azure Key Vault, environment variables)
- [ ] Implement password complexity requirements
- [ ] Add rate limiting to login endpoint
- [ ] Implement account lockout after failed login attempts
- [ ] Log authentication failures for security monitoring
- [ ] Implement refresh tokens for long-lived sessions
- [ ] Add CORS configuration for web clients
- [ ] Implement API key rotation policy
- [ ] Add audit logging for sensitive operations
- [ ] Implement two-factor authentication (2FA) for admin accounts

## Next Steps

1. **Implement Password Hashing:**
   ```bash
   dotnet add package BCrypt.Net-Next --project Src/Infrastructure/Infrastructure.csproj
   ```

2. **Add HttpContextAccessor** (if needed in handlers):
   ```csharp
   // In AddInfrastructureToDI.cs
   services.AddHttpContextAccessor();
   ```

3. **Create User Registration Endpoint:**
   - Similar to Login endpoint
   - Hash password before storing
   - Assign default role

4. **Create Admin Endpoints:**
   - Manage users
   - Manage roles
   - Manage API keys

5. **Add Refresh Token Support:**
   - Extend token lifetime without re-login
   - Store refresh tokens securely

## References

- [FastEndpoints Security Documentation](https://fast-endpoints.com/docs/security)
- [ASP.NET Core Authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/)
- [JWT Best Practices](https://tools.ietf.org/html/rfc8725)
