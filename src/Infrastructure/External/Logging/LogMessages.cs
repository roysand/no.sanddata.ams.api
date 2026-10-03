using Microsoft.Extensions.Logging;

namespace Infrastructure.External.Logging;

// External price/FX clients belong to the ElectricityCost area: event ids 1500 - 1599
internal static class LogMessages
{
    private static readonly Action<ILogger, string, Exception?> _spotPriceTokenMissing =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1505, nameof(SpotPriceTokenMissing)),
            "Spot price fetch skipped for {PriceRegion}: ENTSO-E security token is not configured (reason: TokenMissing)");

    private static readonly Action<ILogger, string, int, Exception?> _spotPriceRequestFailed =
        LoggerMessage.Define<string, int>(
            LogLevel.Warning,
            new EventId(1506, nameof(SpotPriceRequestFailed)),
            "Spot price request failed for {PriceRegion} with HTTP {StatusCode} (reason: HttpError)");

    private static readonly Action<ILogger, string, Exception?> _spotPriceInvalidResponse =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1507, nameof(SpotPriceInvalidResponse)),
            "Spot price response for {PriceRegion} could not be used (reason: InvalidResponse)");

    private static readonly Action<ILogger, int, Exception?> _exchangeRateRequestFailed =
        LoggerMessage.Define<int>(
            LogLevel.Warning,
            new EventId(1508, nameof(ExchangeRateRequestFailed)),
            "Exchange rate request failed with HTTP {StatusCode} (reason: HttpError)");

    private static readonly Action<ILogger, DateOnly, Exception?> _exchangeRateNotPublished =
        LoggerMessage.Define<DateOnly>(
            LogLevel.Information,
            new EventId(1509, nameof(ExchangeRateNotPublished)),
            "No exchange rate published yet for {Date} (reason: NotPublished)");

    private static readonly Action<ILogger, string, Exception?> _spotPriceUnavailable =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1510, nameof(SpotPriceUnavailable)),
            "Spot price request for {PriceRegion} could not reach ENTSO-E (reason: Unavailable)");

    private static readonly Action<ILogger, string, Exception?> _spotPriceNoData =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1513, nameof(SpotPriceNoData)),
            "No spot prices returned by ENTSO-E for {PriceRegion} yet (reason: NoData)");

    private static readonly Action<ILogger, Exception?> _exchangeRateUnavailable =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1511, nameof(ExchangeRateUnavailable)),
            "Exchange rate request could not reach Norges Bank (reason: Unavailable)");

    private static readonly Action<ILogger, Exception?> _exchangeRateInvalidResponse =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(1512, nameof(ExchangeRateInvalidResponse)),
            "Exchange rate response from Norges Bank could not be used (reason: InvalidResponse)");

    public static void SpotPriceTokenMissing(ILogger logger, string priceRegion)
        => _spotPriceTokenMissing(logger, priceRegion, null);

    public static void SpotPriceRequestFailed(ILogger logger, string priceRegion, int statusCode)
        => _spotPriceRequestFailed(logger, priceRegion, statusCode, null);

    public static void SpotPriceInvalidResponse(ILogger logger, string priceRegion)
        => _spotPriceInvalidResponse(logger, priceRegion, null);

    public static void SpotPriceNoData(ILogger logger, string priceRegion)
        => _spotPriceNoData(logger, priceRegion, null);

    public static void SpotPriceUnavailable(ILogger logger, string priceRegion)
        => _spotPriceUnavailable(logger, priceRegion, null);

    public static void ExchangeRateRequestFailed(ILogger logger, int statusCode)
        => _exchangeRateRequestFailed(logger, statusCode, null);

    public static void ExchangeRateNotPublished(ILogger logger, DateOnly date)
        => _exchangeRateNotPublished(logger, date, null);

    public static void ExchangeRateUnavailable(ILogger logger)
        => _exchangeRateUnavailable(logger, null);

    public static void ExchangeRateInvalidResponse(ILogger logger)
        => _exchangeRateInvalidResponse(logger, null);
}
