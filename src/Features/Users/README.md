# User CRUD Operations

This folder contains all User CRUD endpoints following the FastEndpoints + CQRS + Result pattern.

## Quick Reference

| Endpoint | Method | Route | Description |
|----------|--------|-------|-------------|
| CreateUser | POST | `/api/users` | Create a new user (IsActive auto-set to true) |
| GetUser | GET | `/api/users/{id}` | Get user by ID |
| GetUsers | GET | `/api/users` | Get paginated list of users with filtering |
| UpdateUser | PUT | `/api/users/{id}` | Update user information |
| DeleteUser | DELETE | `/api/users/{id}` | Soft delete user (sets IsActive=false) |
| ChangePassword | PUT | `/api/users/{id}/password` | Change user password |
| GrantAdmin | PUT | `/api/users/{id}/roles/admin` | Admin only: make the user an Admin |
| RevokeAdmin | DELETE | `/api/users/{id}/roles/admin` | Admin only: remove the Admin role (not from the last Admin) |
| LinkUserLocation | PUT | `/api/users/{id}/locations/{locationId}` | Admin only: let the user see a location |
| UnlinkUserLocation | DELETE | `/api/users/{id}/locations/{locationId}` | Admin only: remove that access |

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

### Access rules
Every endpoint requires sign-in. Admin only: create, list and delete users, grant/revoke the Admin role, link/unlink
locations. Own account (or Admin): get, update, change password; another person's account looks like a missing one
(404). See [AuthenticationGuide.md](../../../AuthenticationGuide.md#roles-and-first-admin).

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
