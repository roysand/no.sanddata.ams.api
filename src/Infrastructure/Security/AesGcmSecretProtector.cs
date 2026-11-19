using System.Security.Cryptography;
using System.Text;
using Application.Common.Interfaces.Security;
using Application.WaterHeaters;
using Domain.Common;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Security;

/// <summary>
/// AES-256-GCM protection for secrets stored in the database (spec 011, research R1). The 32-byte key comes from
/// <c>WaterHeater:KeyEncryptionKey</c> (base64), set in the server's .env, so a database copy alone reveals nothing.
/// Stored form: <c>v1:</c> + base64(nonce ‖ ciphertext ‖ tag), with the row's id as associated data so a value
/// copied to another row does not decrypt. A missing or invalid key never stops the app; it only makes
/// <see cref="IsConfigured"/> false.
/// </summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    public const string ConfigKey = "WaterHeater:KeyEncryptionKey";

    private const string Prefix = "v1:";
    private const int KeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    private readonly byte[]? _key;

    public AesGcmSecretProtector(IConfiguration configuration) => _key = ReadKey(configuration[ConfigKey]);

    public bool IsConfigured => _key is not null;

    public string Protect(string plaintext, Guid associatedId)
    {
        if (_key is null)
        {
            throw new InvalidOperationException("No encryption key is configured; check IsConfigured first.");
        }

        byte[] plain = Encoding.UTF8.GetBytes(plaintext);
        byte[] output = new byte[NonceBytes + plain.Length + TagBytes];
        Span<byte> nonce = output.AsSpan(0, NonceBytes);
        Span<byte> cipher = output.AsSpan(NonceBytes, plain.Length);
        Span<byte> tag = output.AsSpan(NonceBytes + plain.Length, TagBytes);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_key, TagBytes);
        aes.Encrypt(nonce, plain, cipher, tag, AssociatedData(associatedId));

        return Prefix + Convert.ToBase64String(output);
    }

    public Result<string> Unprotect(string protectedValue, Guid associatedId)
    {
        if (_key is null || !protectedValue.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return Result.Failure<string>(WaterHeaterErrors.KeyUnreadable);
        }

        byte[] input;
        try
        {
            input = Convert.FromBase64String(protectedValue[Prefix.Length..]);
        }
        catch (FormatException)
        {
            return Result.Failure<string>(WaterHeaterErrors.KeyUnreadable);
        }

        if (input.Length < NonceBytes + TagBytes)
        {
            return Result.Failure<string>(WaterHeaterErrors.KeyUnreadable);
        }

        int cipherLength = input.Length - NonceBytes - TagBytes;
        byte[] plain = new byte[cipherLength];
        try
        {
            using var aes = new AesGcm(_key, TagBytes);
            aes.Decrypt(
                input.AsSpan(0, NonceBytes),
                input.AsSpan(NonceBytes, cipherLength),
                input.AsSpan(NonceBytes + cipherLength, TagBytes),
                plain,
                AssociatedData(associatedId));
        }
        catch (CryptographicException)
        {
            return Result.Failure<string>(WaterHeaterErrors.KeyUnreadable);
        }

        return Result.Success(Encoding.UTF8.GetString(plain));
    }

    private static byte[] AssociatedData(Guid associatedId) => Encoding.UTF8.GetBytes(associatedId.ToString("N"));

    private static byte[]? ReadKey(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return null;
        }

        try
        {
            byte[] key = Convert.FromBase64String(base64);
            return key.Length == KeyBytes ? key : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
