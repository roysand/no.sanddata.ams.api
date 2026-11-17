using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using NSubstitute;

namespace Features.Tests.Pricing;

/// <summary>
/// The cost handlers now load the spot prices and exchange rates of a whole range with one query each. Tests written
/// against the single-row queries arrange those; these helpers make the range queries answer from the same
/// arrangement, so each test keeps stating its prices in one place.
/// </summary>
internal static class RangeStubs
{
    /// <summary>The range query returns what the single-hour query returns for each hour in [from, to).</summary>
    public static void ServeRangesFromSingleHours(this IElectricityPriceRepository<ElectricityPrice> prices) =>
        prices.GetByRegionAndHourRangeAsync(
                Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var found = new List<ElectricityPrice>();
                DateTime to = call.ArgAt<DateTime>(2);
                for (DateTime hour = call.ArgAt<DateTime>(1); hour < to; hour = hour.AddHours(1))
                {
                    ElectricityPrice? price = await prices.GetByRegionAndHourAsync(
                        call.ArgAt<string>(0), hour, call.ArgAt<CancellationToken>(3));
                    if (price is not null)
                    {
                        found.Add(price);
                    }
                }

                return (IReadOnlyList<ElectricityPrice>)found;
            });

    /// <summary>The range query returns what the single-date query returns for each date in [from, to].</summary>
    public static void ServeRangesFromSingleDates(this IExchangeRateRepository<ExchangeRate> rates) =>
        rates.GetByDateRangeAsync(
                Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var found = new List<ExchangeRate>();
                DateOnly to = call.ArgAt<DateOnly>(2);
                for (DateOnly date = call.ArgAt<DateOnly>(1); date <= to; date = date.AddDays(1))
                {
                    ExchangeRate? rate = await rates.GetByDateAsync(
                        call.ArgAt<string>(0), date, call.ArgAt<CancellationToken>(3));
                    if (rate is not null)
                    {
                        found.Add(rate);
                    }
                }

                return (IReadOnlyList<ExchangeRate>)found;
            });
}
