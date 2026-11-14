using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using NSubstitute;

namespace Features.Tests.NorgesPris;

/// <summary>Builds rates and a stub rate repository for the cost and Norgespris tests.</summary>
internal static class NorgesPrisRates
{
    public static NorgesPrisRate Rate(string validFrom, decimal rate, decimal tax = 0m) =>
        new(Guid.NewGuid(), DateOnly.Parse(validFrom), rate, tax, Guid.Empty);

    /// <summary>A repository that returns the rates ordered by start date, as the real one does.</summary>
    public static INorgesPrisRateRepository Repository(params NorgesPrisRate[] rates)
    {
        INorgesPrisRateRepository repository = Substitute.For<INorgesPrisRateRepository>();
        repository.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<NorgesPrisRate>>(
                rates.OrderBy(r => r.ValidFrom).ToList()));
        return repository;
    }

    /// <summary>The seeded rate every database starts with: 0.40 from 2020-01-01.</summary>
    public static INorgesPrisRateRepository Seeded(decimal tax = 0m) =>
        Repository(Rate("2020-01-01", 0.40m, tax));
}
