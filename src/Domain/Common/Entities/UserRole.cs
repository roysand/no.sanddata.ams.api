namespace Domain.Common.Entities;

public class UserRole
{
    public UserRole(Guid userId, Guid roleId, DateTime assignedAt)
    {
        UserId = userId;
        RoleId = roleId;
        AssignedAt = assignedAt;
    }

    // Parameterless constructor for EF Core compatibility
    public UserRole() {}

    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public DateTime AssignedAt { get; private set; }
}
