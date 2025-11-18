namespace Domain.Common.Entities;

public class Location(
    Guid id,
    string name,
    string address,
    string serialNumber,
    string zone,
    bool isActive,
    bool hasNorgesPriceAgreement) : Entity(id)
{
    public string Name { get; private set; } = name;
    public string Address { get; private set; } = address;
    public string SerialNumber { get; private set; } = serialNumber;
    public string Zone { get; private set; } = zone;
    public bool IsActive { get; private set; } = isActive;
    public bool HasNorgesPriceAgreement { get; private set; } = hasNorgesPriceAgreement;
    public ApiKey ApiKey { get; private set; }
    private List<User> _users = new();
    public IReadOnlyCollection<User> Users => _users.AsReadOnly();
}
