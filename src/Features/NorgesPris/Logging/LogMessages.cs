using Microsoft.Extensions.Logging;

namespace Features.NorgesPris.Logging;

/// <summary>
/// Norgespris rates: event ids 1600 - 1699. Ids, dates, the rate values and the acting user only.
/// Refusal reason codes: DateExists, NotNewest, ConfirmationRequired, AlreadyStarted, NotFound.
/// </summary>
internal static class LogMessages
{
    private static readonly Action<ILogger, Guid, int, Exception?> _ratesListed =
        LoggerMessage.Define<Guid, int>(
            LogLevel.Information,
            new EventId(1600, nameof(RatesListed)),
            "Norgespris rates listed for {ActingUserId}: {Count} found");

    private static readonly Action<ILogger, Guid, DateOnly, decimal, decimal, Guid, Exception?> _rateAdded =
        LoggerMessage.Define<Guid, DateOnly, decimal, decimal, Guid>(
            LogLevel.Information,
            new EventId(1601, nameof(RateAdded)),
            "Norgespris rate {RateId} added from {ValidFrom}: rate {RatePerKwh} tax {TaxPerKwh} by {ActingUserId}");

    private static readonly Action<ILogger, Guid, DateOnly, Guid, Exception?> _rateDeleted =
        LoggerMessage.Define<Guid, DateOnly, Guid>(
            LogLevel.Information,
            new EventId(1602, nameof(RateDeleted)),
            "Norgespris rate {RateId} starting {ValidFrom} deleted by {ActingUserId}");

    private static readonly Action<ILogger, string, Guid, Exception?> _rateRefused =
        LoggerMessage.Define<string, Guid>(
            LogLevel.Information,
            new EventId(1603, nameof(RateRefused)),
            "Norgespris change refused ({Reason}) for {ActingUserId}");

    public static void RatesListed(ILogger logger, Guid actingUserId, int count)
        => _ratesListed(logger, actingUserId, count, null);

    public static void RateAdded(
        ILogger logger, Guid rateId, DateOnly validFrom, decimal ratePerKwh, decimal taxPerKwh, Guid actingUserId)
        => _rateAdded(logger, rateId, validFrom, ratePerKwh, taxPerKwh, actingUserId, null);

    public static void RateDeleted(ILogger logger, Guid rateId, DateOnly validFrom, Guid actingUserId)
        => _rateDeleted(logger, rateId, validFrom, actingUserId, null);

    public static void RateRefused(ILogger logger, string reason, Guid actingUserId)
        => _rateRefused(logger, reason, actingUserId, null);
}
