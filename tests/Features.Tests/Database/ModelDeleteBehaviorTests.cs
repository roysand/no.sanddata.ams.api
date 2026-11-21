using Domain.Common.Entities;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Features.Tests.Database;

/// <summary>Reads the EF model only; nothing connects to a database.</summary>
public class ModelDeleteBehaviorTests
{
    private static IModel BuildModel()
    {
        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=not-used")
            .Options;
        using var context = new ApplicationDbContext(options);
        return context.GetService<IDesignTimeModel>().Model;
    }

    private static IForeignKey ForeignKey(Type dependent, Type principal) =>
        BuildModel().FindEntityType(dependent)!
            .GetForeignKeys()
            .Single(fk => fk.PrincipalEntityType.ClrType == principal);

    [Fact]
    public void ASensorKeyBelongsToItsLocation_SoDeletingTheLocationDeletesTheKey()
    {
        IForeignKey key = ForeignKey(typeof(ApiKey), typeof(Location));

        Assert.Equal(DeleteBehavior.Cascade, key.DeleteBehavior);
        Assert.True(key.IsRequired);
        Assert.True(key.IsUnique);
    }

    [Fact]
    public void DeletingAnApiKey_CannotDeleteALocation() =>
        // A location must never hold a foreign key to its key: that would let a key delete take the location,
        // its meters and every measurement along.
        Assert.DoesNotContain(
            BuildModel().FindEntityType(typeof(Location))!.GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(ApiKey));

    [Fact]
    public void DeletingALocation_StillRemovesItsMeters() =>
        Assert.Equal(DeleteBehavior.Cascade, ForeignKey(typeof(Meter), typeof(Location)).DeleteBehavior);

    [Fact]
    public void AGridTariffInUse_CanNeverBeDeletedByCascade_FromItsRates() =>
        Assert.Equal(DeleteBehavior.Restrict, ForeignKey(typeof(GridTariffRate), typeof(GridTariff)).DeleteBehavior);

    [Fact]
    public void AGridTariffInUse_CanNeverBeDeletedByCascade_FromALocation()
    {
        IForeignKey key = ForeignKey(typeof(Location), typeof(GridTariff));

        Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior);
        Assert.False(key.IsRequired);
    }

    [Fact]
    public void DeletingAGridTariff_NeverTouchesALocationOrItsMeasurements() =>
        // A location refers to a tariff, never the other way round, and the link is restricted.
        Assert.DoesNotContain(
            BuildModel().FindEntityType(typeof(GridTariff))!.GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(Location));

    [Fact]
    public void TheDatedRates_AreUniquePerKindOrTariffAndStartDate()
    {
        IModel model = BuildModel();

        Assert.Contains(
            model.FindEntityType(typeof(PriceRate))!.GetIndexes(),
            i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(["Kind", "ValidFrom"]));
        Assert.Contains(
            model.FindEntityType(typeof(GridTariffRate))!.GetIndexes(),
            i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(["GridTariffId", "ValidFrom"]));
    }
}
