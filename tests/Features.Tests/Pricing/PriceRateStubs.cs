using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using NSubstitute;

namespace Features.Tests.Pricing;

/// <summary>Builds a stub <see cref="IPriceRateRepository"/> for the cost tests.</summary>
internal static class PriceRateStubs
{
    public static readonly DateOnly Since = new(2020, 1, 1);

    public static PriceRate Rate(PriceRateKind kind, string validFrom, decimal value) =>
        new(Guid.NewGuid(), kind, DateOnly.Parse(validFrom), value, Guid.Empty);

    public static GridTariffRate Grid(Guid tariffId, string validFrom, decimal day, decimal night) =>
        new(Guid.NewGuid(), tariffId, DateOnly.Parse(validFrom), day, night, Guid.Empty);

    /// <summary>The rows the repository returns, in any order; the calculator sorts them.</summary>
    public static IPriceRateRepository Repository(
        IEnumerable<PriceRate> rates, IEnumerable<GridTariffRate>? gridRates = null)
    {
        IPriceRateRepository repository = Substitute.For<IPriceRateRepository>();
        PriceRate[] rateRows = rates.ToArray();
        GridTariffRate[] gridRows = (gridRates ?? []).ToArray();
        repository.GetPriceRatesAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<PriceRate>>(rateRows));
        repository.GetGridTariffRatesAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<GridTariffRate>>(gridRows));
        return repository;
    }

    /// <summary>
    /// No VAT, no electricity tax, no Enova fee and no grid tariff: the price is the energy part alone, so the tests
    /// written for the energy-only calculation keep their numbers.
    /// </summary>
    public static IPriceRateRepository Zero() => Repository(
    [
        Rate(PriceRateKind.Vat, "2020-01-01", 0m),
        Rate(PriceRateKind.ElectricityTax, "2020-01-01", 0m),
        Rate(PriceRateKind.EnovaFee, "2020-01-01", 0m)
    ]);

    /// <summary>The 2026 national values: VAT 25 percent, electricity tax 0.0713, Enova fee 0.0100.</summary>
    public static IEnumerable<PriceRate> National2026() =>
    [
        Rate(PriceRateKind.Vat, "2020-01-01", 0.25m),
        Rate(PriceRateKind.ElectricityTax, "2026-01-01", 0.0713m),
        Rate(PriceRateKind.EnovaFee, "2026-01-01", 0.0100m)
    ];
}
