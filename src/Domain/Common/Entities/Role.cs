namespace Domain.Common.Entities;

public class Role : Entity
{
    public string Name { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public bool IsActive { get; private set; }
    private List<User> _users = new();
    public IReadOnlyCollection<User> Users => _users.AsReadOnly();

    // Parameterless constructor for EF Core compatibility
    public Role() : base() { }

    public Role(Guid id, string name, string description, bool isActive)
        : base(id)
    {
        Name = name;
        Description = description;
        IsActive = isActive;
    }
}
