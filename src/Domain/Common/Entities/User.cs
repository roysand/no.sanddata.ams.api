using Domain.Common.ValueObjects;

namespace Domain.Common.Entities;

public class User : Entity
{
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string PasswordHash { get; set; }
    public required EmailAddress Email { get; set; }
    public bool IsActive { get; set; }
    private List<Role> _roles = new();
    public IReadOnlyCollection<Role> Roles => _roles.AsReadOnly();
    private List<Location> _locations = new();
    public IReadOnlyCollection<Location> Locations => _locations.AsReadOnly();
    private List<RefreshToken> _refreshTokens = new();
    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    // Parameterless constructor for EF Core
    public User() { }

    public User(Guid id, string firstName, string lastName, string passwordHash, EmailAddress email, bool isActive)
        : base(id)
    {
        FirstName = firstName;
        LastName = lastName;
        PasswordHash = passwordHash;
        Email = email;
        IsActive = isActive;
    }
}
