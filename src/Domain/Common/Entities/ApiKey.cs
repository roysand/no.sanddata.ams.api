namespace Domain.Common.Entities;

/// <summary>
/// A sensor key. The key itself is never stored: only its SHA-256 fingerprint (KeyHash) and the last few
/// characters (KeyHint, non-secret, to help an administrator recognise it).
/// </summary>
public class ApiKey : Entity
{
    public string KeyHash { get; private set; } = null!;
    public string KeyHint { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public Location Location { get; private set; } = null!;
    private readonly List<User> _users = new();
    public IReadOnlyCollection<User> Users => _users.AsReadOnly();

    public ApiKey(Guid id, string keyHash, string keyHint, string description, bool isActive, DateTime expiresAt)
        : base(id)
    {
        KeyHash = keyHash;
        KeyHint = keyHint;
        Description = description;
        IsActive = isActive;
        ExpiresAt = expiresAt;
    }

    public ApiKey() : base() { }

    /// <summary>Replaces the key. The previous key stops working at once; a rotated key is always active.</summary>
    public void Rotate(string keyHash, string keyHint, DateTime expiresAt)
    {
        KeyHash = keyHash;
        KeyHint = keyHint;
        ExpiresAt = expiresAt;
        IsActive = true;
    }

    public void SetActive(bool isActive) => IsActive = isActive;
}
