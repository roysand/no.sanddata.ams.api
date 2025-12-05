namespace Domain.Common.Entities;

public class RefreshToken : Entity
{
    public required Guid UserId { get; set; }
    public required string Token { get; set; }
    public required DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByToken { get; set; }
    public string? ReasonRevoked { get; set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsRevoked => RevokedAt != null;
    public bool IsActive => !IsRevoked && !IsExpired;

    // Navigation property
    public User User { get; set; } = null!;

    // Parameterless constructor for EF Core
    public RefreshToken() { }

    public RefreshToken(Guid id, Guid userId, string token, DateTime expiresAt)
        : base(id)
    {
        UserId = userId;
        Token = token;
        ExpiresAt = expiresAt;
        CreatedAt = DateTime.UtcNow;
    }
}
