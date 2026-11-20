namespace Domain.Common.Entities;

public class Location : Entity
{
    public string Name { get; private set; } = null!;
    public string Address { get; private set; } = null!;
    public string SerialNumber { get; private set; } = null!;
    public string Zone { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public bool HasNorgesPriceAgreement { get; private set; }

    /// <summary>The grid tariff the location pays; null until an administrator has set one.</summary>
    public Guid? GridTariffId { get; private set; }

    /// <summary>Supplier markup in NOK per kWh excluding VAT; added on every kWh, Norgespris included.</summary>
    public decimal SupplierMarkupPerKwh { get; private set; }

    /// <summary>
    /// The supplier monthly fee in NOK per month excluding VAT, a full month on every bill; null until an administrator
    /// has set it (0 for a supplier without a fee).
    /// </summary>
    public decimal? SupplierMonthlyFee { get; private set; }

    /// <summary>No electricity tax (households in the tiltakssone); the Enova fee still applies.</summary>
    public bool ElectricityTaxExempt { get; private set; }

    /// <summary>No VAT on electricity (households in Nordland, Troms and Finnmark).</summary>
    public bool VatExempt { get; private set; }

    /// <summary>A holiday home (fritidsbolig): picks the holiday-home Norgespris monthly cap.</summary>
    public bool IsHolidayHome { get; private set; }

    /// <summary>
    /// Count the EV charger's kWh in the location's kWh totals (spec 016). A view choice: costs never include it.
    /// </summary>
    public bool IncludeEvChargerInTotals { get; private set; }

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

    /// <summary>Administrator-only. Changing these changes how every past hour of the location is priced.</summary>
    public void SetPricing(
        Guid? gridTariffId,
        decimal supplierMarkupPerKwh,
        bool electricityTaxExempt,
        bool vatExempt,
        bool isHolidayHome)
    {
        GridTariffId = gridTariffId;
        SupplierMarkupPerKwh = supplierMarkupPerKwh;
        ElectricityTaxExempt = electricityTaxExempt;
        VatExempt = vatExempt;
        IsHolidayHome = isHolidayHome;
    }

    /// <summary>Administrator-only. Not dated, like the markup: a change applies to past months too.</summary>
    public void SetSupplierMonthlyFee(decimal? supplierMonthlyFee) => SupplierMonthlyFee = supplierMonthlyFee;

    /// <summary>Owner or administrator: whether the EV charger's kWh are counted with the house's.</summary>
    public void SetIncludeEvCharger(bool include) => IncludeEvChargerInTotals = include;

    public void AssignApiKey(ApiKey apiKey) => ApiKey = apiKey;

    public Location() : base() { }
}
