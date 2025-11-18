namespace Domain.Common.Entities;

public class Role(Guid id, string name, string description, bool isActive) : Entity(id)
{
    public string Name { get; private set; } = name;
    public string Description { get; private set; } = description;
    public bool IsActive { get; private set; } = isActive;
    private List<User> _users = new();
    public IReadOnlyCollection<User> Users => _users.AsReadOnly();
}
