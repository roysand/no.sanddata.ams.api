using Domain.Common.ValueObjects;

namespace Domain.Common.Entities;

public class ApiKey : Entity
{
    public string Key { get; private set; }
    public string Description { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public Location Location { get; private set; }
    private List<User> _users = new();
    public IReadOnlyCollection<User> Users => _users.AsReadOnly();

    public ApiKey(Guid id, string key, string description, bool isActive, DateTime expiresAt)
        : base(id)
    {
        Key = key;
        Description = description;
        IsActive = isActive;
        ExpiresAt = expiresAt;
    }

    public ApiKey() : base() { }
}
