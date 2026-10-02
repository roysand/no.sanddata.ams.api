using Microsoft.Extensions.Logging;

namespace Features.ElectricityCost.Logging;

internal static class LogMessages
{
    // ElectricityCost: event ids 1500 - 1599
    private static readonly Action<ILogger, string, int, Exception?> _spotPricesStored =
        LoggerMessage.Define<string, int>(
            LogLevel.Information,
            new EventId(1500, nameof(SpotPricesStored)),
            "Spot prices stored for {PriceRegion}: {Count} new");

    private static readonly Action<ILogger, DateOnly, Exception?> _exchangeRateStored =
        LoggerMessage.Define<DateOnly>(
            LogLevel.Information,
            new EventId(1501, nameof(ExchangeRateStored)),
            "Exchange rate stored for {Date}");

    private static readonly Action<ILogger, string, Exception?> _priceFetchFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1502, nameof(PriceFetchFailed)),
            "Price fetch failed: {Reason}");

    private static readonly Action<ILogger, Guid, Guid, Exception?> _costQueried =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Information,
            new EventId(1503, nameof(CostQueried)),
            "Cost queried by {UserId} for location {LocationId}");

    private static readonly Action<ILogger, Guid, Guid, Exception?> _locationAccessDenied =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Warning,
            new EventId(1504, nameof(LocationAccessDenied)),
            "Cost query denied: user {UserId} has no access to location {LocationId} (reason: LocationNotFound)");

    public static void CostQueried(ILogger logger, Guid userId, Guid locationId)
        => _costQueried(logger, userId, locationId, null);

    public static void LocationAccessDenied(ILogger logger, Guid userId, Guid locationId)
        => _locationAccessDenied(logger, userId, locationId, null);

    public static void SpotPricesStored(ILogger logger, string priceRegion, int count)
        => _spotPricesStored(logger, priceRegion, count, null);

    public static void ExchangeRateStored(ILogger logger, DateOnly date)
        => _exchangeRateStored(logger, date, null);

    public static void PriceFetchFailed(ILogger logger, string reason, Exception exception)
        => _priceFetchFailed(logger, reason, exception);
}
