using Domain.Common.ValueObjects;

namespace Domain.Common.Entities;

public class User(
    Guid id,
    string firstName,
    string lastName,
    string passwordHash,
    EmailAddress email,
    bool isActive) : Entity(id)
{
    public string FirstName { get; private set; } = firstName;
    public string LastName { get; private set; } = lastName;
    public string PasswordHash { get; private set; } = passwordHash;
    public EmailAddress Email { get; private set; } = email;
    public bool IsActive { get; private set; } = isActive;
    private List<Role> _roles = new();
    public IReadOnlyCollection<Role> Roles => _roles.AsReadOnly();

    private List<Location> _locations = new();
    public IReadOnlyCollection<Location> Locations => _locations.AsReadOnly();
}
