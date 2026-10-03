using System.Security.Cryptography;
using System.Text;

namespace Application.Common.ApiKeys;

/// <summary>
/// Sensor key generation and fingerprinting. Keys are 32 random bytes (256 bits), so a fast hash is enough:
/// the hash cannot be brute-forced, and a slow hash would only add delay to every sensor reading.
/// Only the hash and a short hint are ever stored; the key itself exists only in the one response that creates it.
/// </summary>
public static class ApiKeyCrypto
{
    public static readonly TimeSpan KeyLifetime = TimeSpan.FromDays(730);

    private const int KeyBytes = 32;
    private const int HintLength = 4;

    public readonly record struct GeneratedKey(string Key, string Hash, string Hint);

    public static GeneratedKey Generate()
    {
        string key = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(KeyBytes));
        return new GeneratedKey(key, Hash(key), Hint(key));
    }

    /// <summary>Lowercase hex SHA-256 of the UTF-8 key (the same value PostgreSQL's sha256() gives for the text).</summary>
    public static string Hash(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    public static string Hint(string key) => key.Length <= HintLength ? key : key[^HintLength..];
}
