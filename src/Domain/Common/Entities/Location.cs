namespace Domain.Common.Entities;

public class Location : Entity
{
    public string Name { get; private set; }
    public string Address { get; private set; }
    public string SerialNumber { get; private set; }
    public string Zone { get; private set; }
    public bool IsActive { get; private set; }
    public bool HasNorgesPriceAgreement { get; private set; }
    public ApiKey ApiKey { get; private set; }
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
