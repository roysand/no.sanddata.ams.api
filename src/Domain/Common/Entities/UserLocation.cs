namespace Domain.Common.Entities;

public class UserLocation
{
    public UserLocation(Guid userId, Guid locationId)
    {
        UserId = userId;
        LocationId = locationId;
    }

    // Parameterless constructor for EF Core compatibility
    public UserLocation() {}

    public Guid UserId { get; private set; }
    public Guid LocationId { get; private set; }
}
