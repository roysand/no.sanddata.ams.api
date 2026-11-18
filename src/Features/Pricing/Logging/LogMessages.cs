using Microsoft.Extensions.Logging;

namespace Features.Pricing.Logging;

/// <summary>
/// Pricing (national rates, grid tariffs, location pricing settings): event ids 1700 - 1799. Ids, dates, the values and
/// the acting user only. Refusal reason codes: DateExists, NotNewest, ConfirmationRequired, AlreadyStarted, NotFound,
/// TariffNameExists, TariffNotFound, LocationNotFound.
/// </summary>
internal static class LogMessages
{
    private static readonly Action<ILogger, string, Guid, int, Exception?> _listed =
        LoggerMessage.Define<string, Guid, int>(
            LogLevel.Information,
            new EventId(1700, nameof(Listed)),
            "Pricing {What} listed for {ActingUserId}: {Count} found");

    private static readonly Action<ILogger, Guid, string, DateOnly, decimal, Guid, Exception?> _priceRateAdded =
        LoggerMessage.Define<Guid, string, DateOnly, decimal, Guid>(
            LogLevel.Information,
            new EventId(1701, nameof(PriceRateAdded)),
            "Price rate {RateId} ({Kind}) added from {ValidFrom}: {Value} by {ActingUserId}");

    private static readonly Action<ILogger, Guid, string, DateOnly, Guid, Exception?> _priceRateDeleted =
        LoggerMessage.Define<Guid, string, DateOnly, Guid>(
            LogLevel.Information,
            new EventId(1702, nameof(PriceRateDeleted)),
            "Price rate {RateId} ({Kind}) starting {ValidFrom} deleted by {ActingUserId}");

    private static readonly Action<ILogger, Guid, string, Guid, Exception?> _gridTariffCreated =
        LoggerMessage.Define<Guid, string, Guid>(
            LogLevel.Information,
            new EventId(1703, nameof(GridTariffCreated)),
            "Grid tariff {TariffId} ({Name}) created by {ActingUserId}");

    private static readonly Action<ILogger, Guid, Guid, DateOnly, decimal, decimal, Guid, Exception?> _gridTariffRateAdded =
        LoggerMessage.Define<Guid, Guid, DateOnly, decimal, decimal, Guid>(
            LogLevel.Information,
            new EventId(1704, nameof(GridTariffRateAdded)),
            "Grid tariff rate {RateId} of tariff {TariffId} added from {ValidFrom}: day {DayRate} night {NightRate} by {ActingUserId}");

    private static readonly Action<ILogger, Guid, Guid, DateOnly, Guid, Exception?> _gridTariffRateDeleted =
        LoggerMessage.Define<Guid, Guid, DateOnly, Guid>(
            LogLevel.Information,
            new EventId(1705, nameof(GridTariffRateDeleted)),
            "Grid tariff rate {RateId} of tariff {TariffId} starting {ValidFrom} deleted by {ActingUserId}");

    private static readonly Action<ILogger, Guid, Guid?, decimal, decimal?, Guid, Exception?> _locationPricingChanged =
        LoggerMessage.Define<Guid, Guid?, decimal, decimal?, Guid>(
            LogLevel.Information,
            new EventId(1706, nameof(LocationPricingChanged)),
            "Pricing of location {LocationId} set to tariff {TariffId}, markup {Markup} and monthly fee {MonthlyFee} by {ActingUserId}");

    private static readonly Action<ILogger, Guid, bool, bool, bool, Guid, Exception?> _locationPricingFlagsChanged =
        LoggerMessage.Define<Guid, bool, bool, bool, Guid>(
            LogLevel.Information,
            new EventId(1708, nameof(LocationPricingFlagsChanged)),
            "Pricing flags of location {LocationId} set to tax exempt {ElectricityTaxExempt}, VAT exempt {VatExempt}, holiday home {IsHolidayHome} by {ActingUserId}");

    private static readonly Action<ILogger, Guid, DateOnly, int, Guid, Exception?> _capacityStepsAdded =
        LoggerMessage.Define<Guid, DateOnly, int, Guid>(
            LogLevel.Information,
            new EventId(1709, nameof(CapacityStepsAdded)),
            "Capacity step table of tariff {TariffId} added from {ValidFrom} with {StepCount} steps by {ActingUserId}");

    private static readonly Action<ILogger, Guid, DateOnly, Guid, Exception?> _capacityStepsDeleted =
        LoggerMessage.Define<Guid, DateOnly, Guid>(
            LogLevel.Information,
            new EventId(1710, nameof(CapacityStepsDeleted)),
            "Capacity step table of tariff {TariffId} starting {ValidFrom} deleted by {ActingUserId}");

    private static readonly Action<ILogger, string, Guid, Exception?> _refused =
        LoggerMessage.Define<string, Guid>(
            LogLevel.Information,
            new EventId(1707, nameof(Refused)),
            "Pricing change refused ({Reason}) for {ActingUserId}");

    public static void Listed(ILogger logger, string what, Guid actingUserId, int count)
        => _listed(logger, what, actingUserId, count, null);

    public static void PriceRateAdded(
        ILogger logger, Guid rateId, string kind, DateOnly validFrom, decimal value, Guid actingUserId)
        => _priceRateAdded(logger, rateId, kind, validFrom, value, actingUserId, null);

    public static void PriceRateDeleted(ILogger logger, Guid rateId, string kind, DateOnly validFrom, Guid actingUserId)
        => _priceRateDeleted(logger, rateId, kind, validFrom, actingUserId, null);

    public static void GridTariffCreated(ILogger logger, Guid tariffId, string name, Guid actingUserId)
        => _gridTariffCreated(logger, tariffId, name, actingUserId, null);

    public static void GridTariffRateAdded(
        ILogger logger, Guid rateId, Guid tariffId, DateOnly validFrom, decimal dayRate, decimal nightRate,
        Guid actingUserId)
        => _gridTariffRateAdded(logger, rateId, tariffId, validFrom, dayRate, nightRate, actingUserId, null);

    public static void GridTariffRateDeleted(
        ILogger logger, Guid rateId, Guid tariffId, DateOnly validFrom, Guid actingUserId)
        => _gridTariffRateDeleted(logger, rateId, tariffId, validFrom, actingUserId, null);

    public static void LocationPricingChanged(
        ILogger logger, Guid locationId, Guid? tariffId, decimal markup, decimal? monthlyFee, Guid actingUserId)
        => _locationPricingChanged(logger, locationId, tariffId, markup, monthlyFee, actingUserId, null);

    public static void LocationPricingFlagsChanged(
        ILogger logger, Guid locationId, bool electricityTaxExempt, bool vatExempt, bool isHolidayHome,
        Guid actingUserId)
        => _locationPricingFlagsChanged(
            logger, locationId, electricityTaxExempt, vatExempt, isHolidayHome, actingUserId, null);

    public static void CapacityStepsAdded(
        ILogger logger, Guid tariffId, DateOnly validFrom, int stepCount, Guid actingUserId)
        => _capacityStepsAdded(logger, tariffId, validFrom, stepCount, actingUserId, null);

    public static void CapacityStepsDeleted(ILogger logger, Guid tariffId, DateOnly validFrom, Guid actingUserId)
        => _capacityStepsDeleted(logger, tariffId, validFrom, actingUserId, null);

    public static void Refused(ILogger logger, string reason, Guid actingUserId)
        => _refused(logger, reason, actingUserId, null);
}
