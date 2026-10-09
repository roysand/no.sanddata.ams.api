namespace Domain.Common.Entities;

public class UserLocation
{
    /// <summary>A link without a stated role is an owner link, which is what every link was before roles existed.</summary>
    public UserLocation(Guid userId, Guid locationId) : this(userId, locationId, LocationRole.Owner)
    {
    }

    public UserLocation(Guid userId, Guid locationId, LocationRole role)
    {
        UserId = userId;
        LocationId = locationId;
        Role = role;
    }

    // Parameterless constructor for EF Core compatibility
    public UserLocation() { }

    public Guid UserId { get; private set; }
    public Guid LocationId { get; private set; }
    public LocationRole Role { get; private set; } = LocationRole.Owner;

    public void ChangeRole(LocationRole role) => Role = role;
}
