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
    private readonly List<User> _users = new();
    public IReadOnlyCollection<User> Users => _users.AsReadOnly();
    private readonly List<Meter> _meters = new();
    public IReadOnlyCollection<Meter> Meters => _meters.AsReadOnly();

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

    public void Update(string name, string address, string serialNumber, string zone, bool hasNorgesPriceAgreement)
    {
        Name = name;
        Address = address;
        SerialNumber = serialNumber;
        Zone = zone;
        HasNorgesPriceAgreement = hasNorgesPriceAgreement;
    }

    /// <summary>What an owner may change. Serial number, zone and the Norgespris agreement are administrator-only.</summary>
    public void UpdateDetails(string name, string address)
    {
        Name = name;
        Address = address;
    }

    public void SetActive(bool isActive) => IsActive = isActive;

    public void AssignApiKey(ApiKey apiKey) => ApiKey = apiKey;

    public Location() : base() { }
}
