# User CRUD Operations

This folder contains all User CRUD endpoints following the FastEndpoints + MediatR + Result pattern.

## Quick Reference

| Endpoint | Method | Route | Description |
|----------|--------|-------|-------------|
| CreateUser | POST | `/api/users` | Create a new user (IsActive auto-set to true) |
| GetUser | GET | `/api/users/{id}` | Get user by ID |
| GetUsers | GET | `/api/users` | Get paginated list of users with filtering |
| UpdateUser | PUT | `/api/users/{id}` | Update user information |
| DeleteUser | DELETE | `/api/users/{id}` | Soft delete user (sets IsActive=false) |
| ChangePassword | PUT | `/api/users/{id}/password` | Change user password |

## Files

- **CreateUser.cs** - User registration endpoint
- **GetUser.cs** - Single user retrieval
- **GetUsers.cs** - Paginated user list with search/filter
- **UpdateUser.cs** - User profile update
- **DeleteUser.cs** - User soft delete
- **ChangePassword.cs** - Password change functionality

## Common Response Model

All endpoints (except Delete) return a `UserResponse`:

```csharp
public record UserResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    bool IsActive,
    string[] Roles,
    string[] Locations
);
```

## Result Pattern

All handlers return `Result<T>` for consistent error handling:

```csharp
// Success
Result.Success(value)

// Failure
Result.Failure<T>(Error.NotFound("Code", "Message"))
Result.Failure<T>(Error.Conflict("Code", "Message"))
Result.Failure<T>(Error.Validation("Code", "Message"))
```

## Important Notes

### Security TODOs
1. **Password Hashing**: Replace placeholder password handling with BCrypt in:
   - `CreateUser.cs` line 64
   - `ChangePassword.cs` line 62

2. **Authorization**: Add role-based authorization policies (currently commented out)

### Features
- ✅ Result pattern for error handling
- ✅ FluentValidation for input validation
- ✅ Pagination support in GetUsers
- ✅ Search functionality (firstName, lastName, email)
- ✅ Email uniqueness validation
- ✅ Strong password requirements
- ✅ Soft delete (IsActive flag)
- ⚠️ Password hashing (TODO)
- ⚠️ Authorization policies (TODO)

## Example Usage

### Create User
```bash
POST /api/users
Content-Type: application/json

{
  "firstName": "John",
  "lastName": "Doe",
  "email": "john.doe@example.com",
  "password": "SecurePass123!"
}
```

### Get Users with Filtering
```bash
GET /api/users?pageNumber=1&pageSize=10&isActive=true&search=john
```

### Update User
```bash
PUT /api/users/{id}
Content-Type: application/json

{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "firstName": "John",
  "lastName": "Smith",
  "email": "john.smith@example.com",
  "isActive": true
}
```

### Change Password
```bash
PUT /api/users/{id}/password
Content-Type: application/json

{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "currentPassword": "OldPassword123!",
  "newPassword": "NewSecurePass456!"
}
```

## Documentation

See [`UserCrudEndpoints.md`](../../../UserCrudEndpoints.md) in the root directory for complete API documentation.
