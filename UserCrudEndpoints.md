# User CRUD Endpoints Documentation

This document provides a comprehensive overview of the User CRUD operations implemented in the system.

## Folder Structure

```
Src/Features/Users/
├── CreateUser.cs          # POST /api/users
├── GetUser.cs             # GET /api/users/{id}
├── GetUsers.cs            # GET /api/users (with pagination)
├── UpdateUser.cs          # PUT /api/users/{id}
├── DeleteUser.cs          # DELETE /api/users/{id}
└── ChangePassword.cs      # PUT /api/users/{id}/password
```

## Endpoints Overview

### 1. Create User
**Endpoint:** `POST /api/users`

**Description:** Register a new user in the system. The `IsActive` flag is automatically set to `true` by the API.

**Request Body:**
```json
{
  "firstName": "John",
  "lastName": "Doe",
  "email": "john.doe@example.com",
  "password": "SecurePass123!"
}
```

**Response:** `201 Created`
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "firstName": "John",
  "lastName": "Doe",
  "email": "john.doe@example.com",
  "isActive": true,
  "roles": [],
  "locations": []
}
```

**Validation Rules:**
- First name: Required, max 100 characters
- Last name: Required, max 100 characters
- Email: Required, valid email format, max 255 characters, must be unique
- Password: Required, min 8 characters, must contain uppercase, lowercase, number, and special character

**Error Responses:**
- `409 Conflict` - Email already exists
- `400 Bad Request` - Validation errors

**Authentication:** Anonymous (can be changed to require admin role)

---

### 2. Get User by ID
**Endpoint:** `GET /api/users/{id}`

**Description:** Retrieve a specific user's details by their ID.

**Path Parameters:**
- `id` (Guid) - User ID

**Response:** `200 OK`
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "firstName": "John",
  "lastName": "Doe",
  "email": "john.doe@example.com",
  "isActive": true,
  "roles": ["Admin", "User"],
  "locations": ["Oslo", "Bergen"]
}
```

**Error Responses:**
- `404 Not Found` - User not found

**Authentication:** TODO - Users can view their own profile, admins can view any

---

### 3. Get All Users (Paginated)
**Endpoint:** `GET /api/users`

**Description:** Retrieve a paginated list of users with optional filtering.

**Query Parameters:**
- `pageNumber` (int, default: 1) - Page number (must be > 0)
- `pageSize` (int, default: 10) - Items per page (1-100)
- `isActive` (bool?, optional) - Filter by active status
- `search` (string?, optional) - Search in firstName, lastName, or email

**Example Request:**
```
GET /api/users?pageNumber=1&pageSize=10&isActive=true&search=john
```

**Response:** `200 OK`
```json
{
  "users": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "firstName": "John",
      "lastName": "Doe",
      "email": "john.doe@example.com",
      "isActive": true,
      "roles": ["User"],
      "locations": ["Oslo"]
    }
  ],
  "totalCount": 1,
  "pageNumber": 1,
  "pageSize": 10,
  "totalPages": 1
}
```

**Validation Rules:**
- Page number must be greater than 0
- Page size must be between 1 and 100

**Error Responses:**
- `400 Bad Request` - Invalid query parameters

**Authentication:** TODO - Typically admin only

---

### 4. Update User
**Endpoint:** `PUT /api/users/{id}`

**Description:** Update an existing user's information.

**Path Parameters:**
- `id` (Guid) - User ID

**Request Body:**
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "firstName": "John",
  "lastName": "Smith",
  "email": "john.smith@example.com",
  "isActive": true
}
```

**Response:** `200 OK`
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "firstName": "John",
  "lastName": "Smith",
  "email": "john.smith@example.com",
  "isActive": true,
  "roles": ["User"],
  "locations": ["Oslo"]
}
```

**Validation Rules:**
- User ID: Required
- First name: Required, max 100 characters
- Last name: Required, max 100 characters
- Email: Required, valid email format, max 255 characters, must be unique (if changed)

**Error Responses:**
- `404 Not Found` - User not found
- `409 Conflict` - Email already exists
- `400 Bad Request` - Validation errors

**Authentication:** TODO - Users can update their own profile, admins can update any

---

### 5. Delete User
**Endpoint:** `DELETE /api/users/{id}`

**Description:** Soft delete a user by setting `IsActive` to `false`. The user will no longer be able to log in.

**Path Parameters:**
- `id` (Guid) - User ID

**Response:** `204 No Content`

**Error Responses:**
- `404 Not Found` - User not found

**Authentication:** TODO - Typically admin only

**Note:** This performs a soft delete. For hard delete, uncomment the alternative code in the handler.

---

### 6. Change Password
**Endpoint:** `PUT /api/users/{id}/password`

**Description:** Allow users to change their password by providing current and new password.

**Path Parameters:**
- `id` (Guid) - User ID

**Request Body:**
```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "currentPassword": "OldPassword123!",
  "newPassword": "NewSecurePass456!"
}
```

**Response:** `200 OK`

**Validation Rules:**
- Current password: Required
- New password: Required, min 8 characters, must contain uppercase, lowercase, number, and special character
- New password must be different from current password

**Error Responses:**
- `404 Not Found` - User not found
- `400 Bad Request` - Invalid password or validation errors

**Authentication:** TODO - Users can only change their own password

---

## Architecture Pattern

All endpoints follow the same architectural pattern:

1. **Request/Response DTOs** - Strongly typed request and response models
2. **MediatR Handler** - Business logic implementation with Result pattern
3. **FluentValidation Validator** - Input validation rules
4. **FastEndpoints Endpoint** - HTTP endpoint configuration and routing

### Example Structure:
```csharp
public static class CreateUser
{
    public record CreateUserRequest(...) : IRequest<Result<UserResponse>>;
    public record UserResponse(...);
    
    internal sealed class Handler : IRequestHandler<CreateUserRequest, Result<UserResponse>>
    {
        // Business logic
    }
    
    public class CreateUserValidator : Validator<CreateUserRequest>
    {
        // Validation rules
    }
}

public class CreateUserEndpoint : Endpoint<CreateUser.CreateUserRequest, CreateUser.UserResponse>
{
    // Endpoint configuration
}
```

## Result Pattern

All handlers use the Result pattern for error handling:

```csharp
// Success
return Result.Success(response);

// Failure
return Result.Failure<UserResponse>(
    Error.NotFound("User.NotFound", "User not found")
);
```

### Error Types:
- `Error.NotFound` - Resource not found (404)
- `Error.Conflict` - Conflict with existing data (409)
- `Error.Validation` - Validation error (400)

## Security Considerations

### TODO Items:
1. **Password Hashing**: Replace placeholder password handling with BCrypt
   - In [`CreateUser.cs`](Src/Features/Users/CreateUser.cs:64)
   - In [`ChangePassword.cs`](Src/Features/Users/ChangePassword.cs:62)

2. **Authorization**: Implement role-based authorization
   - CreateUser: Admin or Anonymous (for self-registration)
   - GetUser: User (own profile) or Admin (any profile)
   - GetUsers: Admin only
   - UpdateUser: User (own profile) or Admin (any profile)
   - DeleteUser: Admin only
   - ChangePassword: User (own password only)

3. **Email Verification**: Consider adding email verification for new users

4. **Rate Limiting**: Implement rate limiting for password change attempts

## Database Considerations

The User entity is already configured with:
- Entity Framework Core configuration in [`UserConfiguration.cs`](Src/Infrastructure/Database/Configuration/UserConfiguration.cs:1)
- Repository implementation in [`UserEfRepository.cs`](Src/Infrastructure/Database/Repositories/UserEfRepository.cs:1)
- Database context in [`ApplicationDbContext.cs`](Src/Infrastructure/Database/ApplicationDbContext.cs:1)

## Testing Recommendations

1. **Unit Tests**: Test handlers with mocked repositories
2. **Integration Tests**: Test endpoints with test database
3. **Validation Tests**: Verify all validation rules
4. **Security Tests**: Test authorization rules once implemented

## Next Steps

1. Implement BCrypt password hashing
2. Add authorization policies
3. Add unit and integration tests
4. Consider adding email verification
5. Add audit logging for user changes
6. Implement rate limiting for sensitive operations
