namespace Domain.Common.Entities;

public class UserLocation(Guid userId, Guid locationId)
{
    public Guid UserId { get; private set; } = userId;
    public Guid LocationId { get; private set; } = locationId;
}
