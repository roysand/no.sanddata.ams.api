namespace Domain.Common.Entities;

public class UserRole(Guid userId, Guid roleId, DateTime assignedAt)
{
    public Guid UserId { get; private set; } = userId;
    public Guid RoleId { get; private set; } = roleId;
    public DateTime AssignedAt { get; private set; } = assignedAt;
}
