using System.Security.Cryptography;
using Domain.Common;
using Infrastructure.Security;
using Microsoft.Extensions.Configuration;

namespace Features.Tests.WaterHeaters;

public class AesGcmSecretProtectorTests
{
    private const string Secret = "TESTKEY-must-not-leak-0000";
    private static readonly Guid LocationId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    internal static AesGcmSecretProtector Create(string? key)
    {
        var values = new Dictionary<string, string?>();
        if (key is not null)
        {
            values[AesGcmSecretProtector.ConfigKey] = key;
        }

        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new AesGcmSecretProtector(configuration);
    }

    internal static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void Protect_ThenUnprotect_GivesThePlaintextBack()
    {
        AesGcmSecretProtector protector = Create(NewKey());

        string stored = protector.Protect(Secret, LocationId);
        Result<string> back = protector.Unprotect(stored, LocationId);

        Assert.True(protector.IsConfigured);
        Assert.True(back.IsSuccess);
        Assert.Equal(Secret, back.Value);
    }

    [Fact]
    public void Protect_StartsWithVersionAndHidesThePlaintext()
    {
        AesGcmSecretProtector protector = Create(NewKey());

        string stored = protector.Protect(Secret, LocationId);

        Assert.StartsWith("v1:", stored);
        Assert.DoesNotContain(Secret, stored);
    }

    [Fact]
    public void Protect_UsesAFreshNonceEachTime()
    {
        AesGcmSecretProtector protector = Create(NewKey());

        Assert.NotEqual(protector.Protect(Secret, LocationId), protector.Protect(Secret, LocationId));
    }

    [Fact]
    public void Unprotect_FailsWhenAByteIsChanged()
    {
        AesGcmSecretProtector protector = Create(NewKey());
        byte[] bytes = Convert.FromBase64String(protector.Protect(Secret, LocationId)["v1:".Length..]);
        bytes[^1] ^= 0x01;

        Result<string> back = protector.Unprotect("v1:" + Convert.ToBase64String(bytes), LocationId);

        Assert.True(back.IsFailure);
        Assert.Equal("WaterHeater.KeyUnreadable", back.Error.Code);
    }

    [Fact]
    public void Unprotect_FailsForAnotherLocation()
    {
        AesGcmSecretProtector protector = Create(NewKey());

        Result<string> back = protector.Unprotect(protector.Protect(Secret, LocationId), Guid.NewGuid());

        Assert.True(back.IsFailure);
    }

    [Fact]
    public void Unprotect_FailsWithAnotherEncryptionKey()
    {
        string stored = Create(NewKey()).Protect(Secret, LocationId);

        Result<string> back = Create(NewKey()).Unprotect(stored, LocationId);

        Assert.True(back.IsFailure);
        Assert.Equal("WaterHeater.KeyUnreadable", back.Error.Code);
    }

    [Theory]
    [InlineData("v2:AAAA")]
    [InlineData("v1:not base64!")]
    [InlineData("v1:AAAA")]
    [InlineData("")]
    public void Unprotect_FailsForMalformedValues(string stored)
    {
        Result<string> back = Create(NewKey()).Unprotect(stored, LocationId);

        Assert.True(back.IsFailure);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA==")] // 16 bytes, not 32
    public void MissingOrInvalidKey_IsNotConfiguredAndDoesNotThrow(string? key)
    {
        AesGcmSecretProtector protector = Create(key);

        Assert.False(protector.IsConfigured);
        Assert.True(protector.Unprotect("v1:AAAA", LocationId).IsFailure);
        Assert.Throws<InvalidOperationException>(() => protector.Protect(Secret, LocationId));
    }
}
