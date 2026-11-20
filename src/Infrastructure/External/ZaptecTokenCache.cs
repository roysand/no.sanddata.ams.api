using Domain.Common;

namespace Infrastructure.External;

/// <summary>
/// Holds the Zaptec access token in memory, shared by the read job and the admin endpoints (singleton, spec 016
/// research R1). Zaptec allows one token request per second per IP and asks clients not to sign in for every call, so
/// a token is reused until 5 minutes before it expires (Zaptec gives 24 hours). Never logged; lost on restart.
/// </summary>
public sealed class ZaptecTokenCache(TimeProvider timeProvider)
{
    public static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _renewAt;

    /// <summary>The cached token, or a new one from <paramref name="fetch"/> (token, lifetime in seconds).</summary>
    public async Task<Result<string>> GetAsync(
        Func<CancellationToken, Task<Result<(string Token, int ExpiresInSeconds)>>> fetch, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_token is not null && timeProvider.GetUtcNow() < _renewAt)
            {
                return Result.Success(_token);
            }

            Result<(string Token, int ExpiresInSeconds)> fetched = await fetch(ct);
            if (fetched.IsFailure)
            {
                _token = null;
                return Result.Failure<string>(fetched.Error);
            }

            _token = fetched.Value.Token;
            _renewAt = timeProvider.GetUtcNow() + TimeSpan.FromSeconds(fetched.Value.ExpiresInSeconds) - RenewBefore;
            return Result.Success(_token);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Forgets the token, so the next call signs in again (after Zaptec answered 401 to it).</summary>
    public void Invalidate()
    {
        _lock.Wait();
        try
        {
            _token = null;
        }
        finally
        {
            _lock.Release();
        }
    }
}
