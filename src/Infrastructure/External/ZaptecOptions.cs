namespace Infrastructure.External;

/// <summary>
/// Roy's own Zaptec login (spec 016), from configuration only: <c>Zaptec__Username</c> / <c>Zaptec__Password</c> from
/// the server's <c>.env</c>, or <c>local.settings.json</c> locally. Never stored in the database, returned or logged.
/// </summary>
public sealed class ZaptecOptions
{
    public const string SectionName = "Zaptec";

    public string? Username { get; set; }
    public string? Password { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);

    /// <summary>Never prints the login.</summary>
    public override string ToString() => $"ZaptecOptions(IsConfigured={IsConfigured})";
}
