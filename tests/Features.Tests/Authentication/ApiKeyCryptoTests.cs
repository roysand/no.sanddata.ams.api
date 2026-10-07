using Application.Common.ApiKeys;

namespace Features.Tests.Authentication;

public class ApiKeyCryptoTests
{
    [Fact]
    public void Hash_KnownVector_MatchesSha256() =>
        // FIPS 180-2 test vector for SHA-256("abc"); also what PostgreSQL's sha256() gives for the same text,
        // which the migration relies on to convert existing keys.
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", ApiKeyCrypto.Hash("abc"));

    [Fact]
    public void Generate_ReturnsA64CharLowercaseHexKey()
    {
        ApiKeyCrypto.GeneratedKey generated = ApiKeyCrypto.Generate();

        Assert.Equal(64, generated.Key.Length);
        Assert.Matches("^[0-9a-f]{64}$", generated.Key);
    }

    [Fact]
    public void Generate_HashAndHintBelongToTheKey()
    {
        ApiKeyCrypto.GeneratedKey generated = ApiKeyCrypto.Generate();

        Assert.Equal(ApiKeyCrypto.Hash(generated.Key), generated.Hash);
        Assert.Equal(generated.Key[^4..], generated.Hint);
        Assert.DoesNotContain(generated.Key, generated.Hash);
    }

    [Fact]
    public void Generate_TwoCalls_ProduceDifferentKeys() 
        => Assert.NotEqual(ApiKeyCrypto.Generate().Key, ApiKeyCrypto.Generate().Key);

    [Fact]
    public void Hint_ShortKey_ReturnsTheWholeKey() 
        => Assert.Equal("ab", ApiKeyCrypto.Hint("ab"));

    [Fact]
    public void KeyLifetime_IsTwoYears() 
        => Assert.Equal(TimeSpan.FromDays(730), ApiKeyCrypto.KeyLifetime);
}
