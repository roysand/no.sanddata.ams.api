namespace Domain.Common.Entities;

/// <summary>
/// A Zaptec EV charger connected to a location (spec 016; at most one per location, one location per charger). It
/// holds ids and names only: the Zaptec sign-in lives in the server's configuration, never in the database.
/// <see cref="Entity.CreatedAt"/> is when it was connected, <see cref="Entity.UpdatedAt"/> when it last changed.
/// </summary>
public class EvCharger : Entity
{
    public Guid LocationId { get; private set; }
    public Guid ChargerId { get; private set; }
    public string ChargerName { get; private set; } = null!;
    public Guid InstallationId { get; private set; }
    public string InstallationName { get; private set; } = null!;

    /// <summary>
    /// The end of the last successful read window (research R3); null until the first read, so the first read goes
    /// back to the start of the previous Oslo month.
    /// </summary>
    public DateTime? ReadUpToUtc { get; private set; }

    public EvCharger(
        Guid id, Guid locationId, Guid chargerId, string chargerName, Guid installationId, string installationName)
        : base(id)
    {
        LocationId = locationId;
        ChargerId = chargerId;
        ChargerName = chargerName;
        InstallationId = installationId;
        InstallationName = installationName;
    }

    public EvCharger() : base() { }

    /// <summary>Connects another charger to the location; its history is read back from the start again.</summary>
    public void Replace(Guid chargerId, string chargerName, Guid installationId, string installationName)
    {
        ChargerId = chargerId;
        ChargerName = chargerName;
        InstallationId = installationId;
        InstallationName = installationName;
        ReadUpToUtc = null;
    }

    public void MarkReadUpTo(DateTime utc) => ReadUpToUtc = utc;

    public override string ToString() => $"EvCharger(LocationId={LocationId})";
}
