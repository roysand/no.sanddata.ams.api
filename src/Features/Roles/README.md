# Roles Feature

This feature implements CRUD operations for managing roles in the system using the vertical slice architecture pattern and Result pattern.

## Overview

The Roles feature provides endpoints for creating, reading, updating, and deleting roles. Each operation is implemented as a self-contained vertical slice with its own request, response, handler, validator, and endpoint.

## Architecture

- **Vertical Slice Pattern**: Each operation is self-contained in its own file
- **Result Pattern**: All operations return `Result<T>` for consistent error handling
- **MediatR**: Used for request/response handling
- **FastEndpoints**: Provides the HTTP endpoint infrastructure
- **FluentValidation**: Handles request validation

## Endpoints

### Create Role
- **Endpoint**: `POST /api/roles`
- **Description**: Create a new role in the system
- **Request Body**:
  ```json
  {
    "name": "Administrator",
    "description": "Full system access with all permissions",
    "isActive": true
  }
  ```
- **Response**: `201 Created` with role details
- **Errors**:
  - `409 Conflict`: Role with this name already exists
  - `400 Bad Request`: Invalid request data

### Get Role by ID
- **Endpoint**: `GET /api/roles/{id}`
- **Description**: Retrieve a specific role by ID
- **Response**: `200 OK` with role details
- **Errors**:
  - `404 Not Found`: Role not found

### Get All Roles
- **Endpoint**: `GET /api/roles`
- **Description**: Retrieve a paginated list of roles with optional filtering
- **Query Parameters**:
  - `PageNumber` (optional, default: 1): Page number to retrieve
  - `PageSize` (optional, default: 10, max: 100): Number of items per page
  - `IsActive` (optional): Filter by active status
  - `Search` (optional): Search term to filter by name or description
- **Response**: `200 OK` with paginated role list
- **Example Response**:
  ```json
  {
    "roles": [
      {
        "id": "guid",
        "name": "Administrator",
        "description": "Full system access",
        "isActive": true
      }
    ],
    "totalCount": 50,
    "pageNumber": 1,
    "pageSize": 10,
    "totalPages": 5
  }
  ```

### Update Role
- **Endpoint**: `PUT /api/roles/{id}`
- **Description**: Update an existing role
- **Request Body**:
  ```json
  {
    "id": "guid",
    "name": "Administrator",
    "description": "Updated description",
    "isActive": true
  }
  ```
- **Response**: `200 OK` with updated role details
- **Errors**:
  - `404 Not Found`: Role not found
  - `409 Conflict`: Role name already exists
  - `400 Bad Request`: Invalid request data

### Delete Role
- **Endpoint**: `DELETE /api/roles/{id}`
- **Description**: Delete a role from the system
- **Response**: `204 No Content`
- **Errors**:
  - `404 Not Found`: Role not found

## Domain Model

The [`Role`](../../../Domain/Common/Entities/Role.cs) entity includes:
- `Id` (Guid): Unique identifier
- `Name` (string): Role name (max 100 characters)
- `Description` (string): Role description (max 500 characters)
- `IsActive` (bool): Whether the role is active
- `Users` (IReadOnlyCollection<User>): Collection of users with this role

## Validation Rules

### Create/Update Role
- **Name**: Required, max 100 characters
- **Description**: Required, max 500 characters
- **IsActive**: Boolean value

### Get Roles (Pagination)
- **PageNumber**: Must be greater than 0
- **PageSize**: Must be greater than 0 and not exceed 100

## Error Handling

All operations use the Result pattern for consistent error handling:

- **Success**: Returns `Result.Success<T>(value)`
- **Failure**: Returns `Result.Failure<T>(error)` with appropriate error type:
  - `ErrorType.NotFound`: Resource not found (404)
  - `ErrorType.Conflict`: Duplicate resource (409)
  - `ErrorType.Validation`: Invalid input (400)

## Repository

The feature uses [`IRoleEfRepository<Role>`](../../../Application/Common/Interfaces/Repositories/IRoleEfRepository.cs) for data access, which provides:
- `GetByIdAsync`: Retrieve role by ID
- `FindAsync`: Query roles with predicate
- `Insert`: Add new role
- `Update`: Update existing role
- `Delete`: Remove role
- `SaveChangesAsync`: Persist changes

## Authorization

Currently, all endpoints allow anonymous access. In production, you should:
- Restrict role management to administrators only
- Add appropriate authorization policies
- Uncomment the `Policies` configuration in each endpoint

Example:
```csharp
Policies(new[] { "AdminOnly" });
```

## Usage Example

```csharp
// Create a new role
var createRequest = new CreateRole.CreateRoleRequest(
    "Manager",
    "Can manage team members and projects",
    true
);

// Get all active roles
var getRolesRequest = new GetRoles.GetRolesRequest(
    PageNumber: 1,
    PageSize: 20,
    IsActive: true,
    Search: null
);

// Update a role
var updateRequest = new UpdateRole.UpdateRoleRequest(
    roleId,
    "Senior Manager",
    "Updated description",
    true
);

// Delete a role
var deleteRequest = new DeleteRole.DeleteRoleRequest(roleId);
```

## Related Features

- [Users](../Users/README.md): User management with role assignments
- [Auth](../Auth/): Authentication and authorization

## Files

- [`CreateRole.cs`](CreateRole.cs): Create role operation
- [`GetRole.cs`](GetRole.cs): Get single role operation
- [`GetRoles.cs`](GetRoles.cs): Get all roles with pagination
- [`UpdateRole.cs`](UpdateRole.cs): Update role operation
- [`DeleteRole.cs`](DeleteRole.cs): Delete role operation