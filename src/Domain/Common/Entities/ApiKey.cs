using Domain.Common.ValueObjects;

namespace Domain.Common.Entities;

public class ApiKey(
    Guid id,
    string key,
    string description,
    bool isActive,
    DateTime expiresAt) : Entity(id)
{
    public string Key { get; private set; } = key;
    public string Description { get; private set; } = description;
    public bool IsActive { get; private set; } = isActive;
    public DateTime ExpiresAt { get; private set; } = expiresAt;
    public Location Location { get; private set; }
    private List<User> _users = new();
    public IReadOnlyCollection<User> Users => _users.AsReadOnly();
}
