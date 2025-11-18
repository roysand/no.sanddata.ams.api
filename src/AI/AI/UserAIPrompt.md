ApiKey:
- Entity Id
- string Key
- string Description
- bool IsActive
- DateTime CreatedAt
- DateTime ExpiresAt
- Location Location

User:
- Entity Id
- string FirstName
- string LastName
- string PasswordHash
- EmailAddress Email
- bool IsActive
- List<Role> Roles
- List<Location> Locations

:Location
- Entity Id
- string Name
- string Address
- string SerialNumber
- string Zone
- bool IsActive
- bool HasNorgesPriceAgreement
- ApiKey ApiKey
- List<User> Users

-UserLocation:
- Entity UserId
- Entity LocationId
 
- Role:
- Entity Id
- string Name
- string Description
- bool IsActive
- List<User> Users

UserRole:
- Entity UserId
- Entity RoleId
- DateTime AssignedAt


