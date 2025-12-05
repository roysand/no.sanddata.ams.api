namespace Domain.Common.Entities;

public class Location : Entity
{
    public string Name { get; private set; } = null!;
    public string Address { get; private set; } = null!;
    public string SerialNumber { get; private set; } = null!;
    public string Zone { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public bool HasNorgesPriceAgreement { get; private set; }
    public ApiKey ApiKey { get; private set; } = null!;
    private List<User> _users = new();
    public IReadOnlyCollection<User> Users => _users.AsReadOnly();

    public Location(Guid id, string name, string address, string serialNumber, string zone, bool isActive, bool hasNorgesPriceAgreement)
        : base(id)
    {
        Name = name;
        Address = address;
        SerialNumber = serialNumber;
        Zone = zone;
        IsActive = isActive;
        HasNorgesPriceAgreement = hasNorgesPriceAgreement;
    }

    public Location() : base() { }
}
