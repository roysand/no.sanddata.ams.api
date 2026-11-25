using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Application.Common.Interfaces.External;
using Application.EvChargers;
using Domain.Common;
using Infrastructure.External.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.External;

/// <summary>
/// Read-only client for Zaptec's cloud API (https://docs.zaptec.com, spec 016 research R1/R2), signing in with the
/// server's own Zaptec login. What the parsing relies on, checked against Roy's charger on 2026-11-20:
/// - <c>/api/chargers</c> answers in PascalCase and <c>/api/sessions/archived</c> in camelCase, so matching is
///   case-insensitive.
/// - <c>energyDetails</c> points are the kWh of the period ending at each timestamp, not cumulative (research R5).
/// - A token lasts 24 hours and there is no refresh token; <see cref="ZaptecTokenCache"/> reuses it.
/// - <c>/api/chargers/{id}/state</c> is an array of <c>{ StateId, Timestamp, ValueAsString }</c> with UTC timestamps
///   without a <c>Z</c>, checked on Roy's Zaptec Go on 2026-11-24 (spec 021, research R1).
/// The token request holds the password and every other request the token: this class never logs a URL, header, body,
/// exception or message, and its HttpClient is registered without the factory's request loggers.
/// </summary>
public class ZaptecEvChargerClient(
    HttpClient httpClient,
    IOptions<ZaptecOptions> options,
    ZaptecTokenCache tokenCache,
    ILogger<ZaptecEvChargerClient> logger) : IEvChargerClient
{
    private const string UserAgent = "AmsApi/1.0";
    private const int SessionsPageSize = 200;
    private const int MaxSessionPages = 50;

    /// <summary>The state read gives up after this, sign-in included (spec 021, research R4).</summary>
    public static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(3);

    // The only state ids kept (spec 021 FR-006); every other value is dropped here.
    private const int OnlineId = -2;
    private const int CheckInId = -1;
    private const int PowerId = 513;
    private const int SessionKwhId = 553;
    private const int ModeId = 710;
    private const int SessionId = 721;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly ZaptecOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    public async Task<Result<IReadOnlyList<ZaptecCharger>>> GetChargersAsync(CancellationToken cancellationToken)
    {
        Result<ChargersPageDto> page =
            await GetJsonAsync<ChargersPageDto>("api/chargers?PageSize=100", "chargers", cancellationToken);
        if (page.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ZaptecCharger>>(page.Error);
        }

        IReadOnlyList<ZaptecCharger> chargers = (page.Value.Data ?? [])
            .Where(c => c.Id != Guid.Empty)
            .Select(c => new ZaptecCharger(c.Id, c.Name ?? string.Empty, c.InstallationId, c.InstallationName ?? string.Empty))
            .ToList();
        return Result.Success(chargers);
    }

    public async Task<Result<IReadOnlyList<ZaptecSession>>> GetCompletedSessionsAsync(
        Guid chargerId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        var sessions = new List<ZaptecSession>();
        string? cursor = null;
        for (int page = 0; page < MaxSessionPages; page++)
        {
            string path = $"api/sessions/archived?ChargerId={chargerId}&From={Format(fromUtc)}&To={Format(toUtc)}" +
                          $"&PageSize={SessionsPageSize}" +
                          (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");
            Result<SessionsPageDto> result = await GetJsonAsync<SessionsPageDto>(path, "sessions", cancellationToken);
            if (result.IsFailure)
            {
                return Result.Failure<IReadOnlyList<ZaptecSession>>(result.Error);
            }

            sessions.AddRange((result.Value.Sessions ?? []).Select(ToSession).OfType<ZaptecSession>());
            cursor = result.Value.Cursor;
            if (!result.Value.HasMore || string.IsNullOrEmpty(cursor))
            {
                return Result.Success<IReadOnlyList<ZaptecSession>>(sessions);
            }
        }

        // More pages than any household has: a cursor that never ends is an answer we can't use.
        ZaptecLogMessages.InvalidResponse(logger, "sessions");
        return Result.Failure<IReadOnlyList<ZaptecSession>>(EvChargerErrors.InvalidResponse);
    }

    public async Task<Result<ZaptecChargerState>> GetStateAsync(Guid chargerId, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StateTimeout);
        try
        {
            // No wait-and-retry after a 429: the caller holds the failure for a minute instead (research R3/R4).
            Result<List<StateDto>> values = await GetJsonAsync<List<StateDto>>(
                $"api/chargers/{chargerId}/state", "state", timeout.Token, retryBusy: false);
            return values.IsFailure
                ? Result.Failure<ZaptecChargerState>(values.Error)
                : Result.Success(ToState(values.Value));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ZaptecLogMessages.Unavailable(logger, "state");
            return Result.Failure<ZaptecChargerState>(EvChargerErrors.Unavailable);
        }
    }

    /// <summary>Keeps the six values the state needs, each from its latest entry; unreadable values are null.</summary>
    private static ZaptecChargerState ToState(List<StateDto> values)
    {
        Dictionary<int, StateEntry> latest = [];
        foreach (StateDto dto in values)
        {
            if (dto.StateId is not (OnlineId or CheckInId or PowerId or SessionKwhId or ModeId or SessionId)
                || !TryParseUtc(dto.Timestamp, out DateTime timestamp))
            {
                continue;
            }

            if (!latest.TryGetValue(dto.StateId, out StateEntry? seen) || timestamp > seen.TimestampUtc)
            {
                latest[dto.StateId] = new StateEntry(dto.ValueAsString, timestamp);
            }
        }

        return new ZaptecChargerState(
            Online: IntValue(latest, OnlineId) is { } online ? new(online.Value == 1, online.TimestampUtc) : null,
            LastCheckInUtc: latest.TryGetValue(CheckInId, out StateEntry? checkIn) ? checkIn.TimestampUtc : null,
            PowerW: DecimalValue(latest, PowerId),
            SessionKwh: DecimalValue(latest, SessionKwhId),
            OperationMode: IntValue(latest, ModeId),
            SessionSetUtc: latest.TryGetValue(SessionId, out StateEntry? session) ? session.TimestampUtc : null);
    }

    private static ZaptecStateValue<int>? IntValue(
        Dictionary<int, StateEntry> latest, int id) =>
        latest.TryGetValue(id, out StateEntry? entry)
        && int.TryParse(entry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? new ZaptecStateValue<int>(value, entry.TimestampUtc)
            : null;

    private static ZaptecStateValue<decimal>? DecimalValue(
        Dictionary<int, StateEntry> latest, int id) =>
        latest.TryGetValue(id, out StateEntry? entry)
        && decimal.TryParse(entry.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value)
            ? new ZaptecStateValue<decimal>(value, entry.TimestampUtc)
            : null;

    /// <summary>Zaptec's state timestamps are UTC without a "Z".</summary>
    private static bool TryParseUtc(string? text, out DateTime utc) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out utc);

    /// <summary>Null for a session without an end (not completed): it is skipped.</summary>
    private static ZaptecSession? ToSession(SessionDto dto)
    {
        if (dto.Id == Guid.Empty || dto.StartDateTime is not { } start || dto.EndDateTime is not { } end)
        {
            return null;
        }

        IReadOnlyList<EnergyPoint> details = (dto.EnergyDetails ?? [])
            .Select(p => new EnergyPoint(p.Timestamp.UtcDateTime, p.Energy))
            .ToList();
        return new ZaptecSession(
            dto.Id,
            start.UtcDateTime,
            end.UtcDateTime,
            dto.Energy,
            details,
            Discard: dto.Voided || dto.ReplacedBySessionId is not null);
    }

    private async Task<Result<T>> GetJsonAsync<T>(string path, string call, CancellationToken ct, bool retryBusy = true)
        where T : class
    {
        if (!_options.IsConfigured)
        {
            return Result.Failure<T>(EvChargerErrors.NotConfigured);
        }

        Result<string> body = await SendWithTokenAsync(path, call, retrySignIn: true, retryBusy, ct);
        return body.IsFailure ? Result.Failure<T>(body.Error) : Parse<T>(body.Value, call);
    }

    private async Task<Result<string>> SendWithTokenAsync(
        string path, string call, bool retrySignIn, bool retryBusy, CancellationToken ct)
    {
        Result<string> token = await tokenCache.GetAsync(FetchTokenAsync, ct);
        if (token.IsFailure)
        {
            return token;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        Result<(HttpStatusCode Status, string Body, TimeSpan? RetryAfter)> sent = await SendAsync(request, call, ct);
        if (sent.IsFailure)
        {
            return Result.Failure<string>(sent.Error);
        }

        (HttpStatusCode status, string responseBody, TimeSpan? retryAfter) = sent.Value;
        if (status == HttpStatusCode.Unauthorized && retrySignIn)
        {
            // The token may have been revoked or expired early: sign in once more before giving up.
            tokenCache.Invalidate();
            return await SendWithTokenAsync(path, call, retrySignIn: false, retryBusy, ct);
        }

        if (status == HttpStatusCode.TooManyRequests && retryBusy)
        {
            ZaptecLogMessages.RateLimited(logger, call);
            await Task.Delay(retryAfter ?? TimeSpan.FromSeconds(1), ct);
            return await SendWithTokenAsync(path, call, retrySignIn, retryBusy: false, ct);
        }

        if (status != HttpStatusCode.OK)
        {
            ZaptecLogMessages.RequestFailed(logger, call, (int)status);
            return Result.Failure<string>(ErrorFor(status, isSignIn: false));
        }

        return Result.Success(responseBody);
    }

    private async Task<Result<(string Token, int ExpiresInSeconds)>> FetchTokenAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "oauth/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["username"] = _options.Username!,
                ["password"] = _options.Password!,
                ["scope"] = "openid",
            })
        };

        Result<(HttpStatusCode Status, string Body, TimeSpan? RetryAfter)> sent = await SendAsync(request, "token", ct);
        if (sent.IsFailure)
        {
            return Result.Failure<(string, int)>(sent.Error);
        }

        (HttpStatusCode status, string body, _) = sent.Value;
        if (status != HttpStatusCode.OK)
        {
            Error error = ErrorFor(status, isSignIn: true);
            if (error == EvChargerErrors.SignInRejected)
            {
                ZaptecLogMessages.SignInRejected(logger);
            }
            else
            {
                ZaptecLogMessages.RequestFailed(logger, "token", (int)status);
            }

            return Result.Failure<(string, int)>(error);
        }

        Result<TokenDto> token = Parse<TokenDto>(body, "token");
        if (token.IsFailure || string.IsNullOrEmpty(token.Value.Access_token) || token.Value.Expires_in <= 0)
        {
            if (token.IsSuccess)
            {
                ZaptecLogMessages.InvalidResponse(logger, "token");
            }

            return Result.Failure<(string, int)>(EvChargerErrors.InvalidResponse);
        }

        return Result.Success((token.Value.Access_token, token.Value.Expires_in));
    }

    private async Task<Result<(HttpStatusCode Status, string Body, TimeSpan? RetryAfter)>> SendAsync(
        HttpRequestMessage request, string call, CancellationToken ct)
    {
        request.Headers.UserAgent.ParseAdd(UserAgent);
        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(request, ct);
            string body = await response.Content.ReadAsStringAsync(ct);
            return Result.Success((response.StatusCode, body, response.Headers.RetryAfter?.Delta));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                   && !ct.IsCancellationRequested)
        {
            ZaptecLogMessages.Unavailable(logger, call);
            return Result.Failure<(HttpStatusCode, string, TimeSpan?)>(EvChargerErrors.Unavailable);
        }
    }

    private Result<T> Parse<T>(string body, string call) where T : class
    {
        try
        {
            T? value = JsonSerializer.Deserialize<T>(body, Json);
            if (value is not null)
            {
                return Result.Success(value);
            }
        }
        catch (JsonException)
        {
            // Falls through: the message could quote the body.
        }

        ZaptecLogMessages.InvalidResponse(logger, call);
        return Result.Failure<T>(EvChargerErrors.InvalidResponse);
    }

    private static Error ErrorFor(HttpStatusCode status, bool isSignIn) => status switch
    {
        HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized when isSignIn => EvChargerErrors.SignInRejected,
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => EvChargerErrors.SignInRejected,
        >= HttpStatusCode.InternalServerError => EvChargerErrors.Unavailable,
        HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests => EvChargerErrors.Unavailable,
        _ => EvChargerErrors.InvalidResponse
    };

    private static string Format(DateTime utc) =>
        Uri.EscapeDataString(utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));

    // Names match Zaptec's fields case-insensitively (PascalCase for chargers, camelCase for sessions).
    private sealed record TokenDto(string? Access_token, int Expires_in);

    private sealed record ChargersPageDto(List<ChargerDto>? Data);

    private sealed record ChargerDto(Guid Id, string? Name, Guid InstallationId, string? InstallationName);

    private sealed record SessionsPageDto(List<SessionDto>? Sessions, string? Cursor, bool HasMore);

    private sealed record SessionDto(
        Guid Id,
        Guid? ReplacedBySessionId,
        bool Voided,
        DateTimeOffset? StartDateTime,
        DateTimeOffset? EndDateTime,
        decimal Energy,
        List<EnergyPointDto>? EnergyDetails);

    private sealed record EnergyPointDto(DateTimeOffset Timestamp, decimal Energy);

    // Timestamp and value as text: both are parsed by hand, so one odd entry doesn't fail the whole answer.
    private sealed record StateDto(int StateId, string? Timestamp, string? ValueAsString);

    private sealed record StateEntry(string? Value, DateTime TimestampUtc);
}
