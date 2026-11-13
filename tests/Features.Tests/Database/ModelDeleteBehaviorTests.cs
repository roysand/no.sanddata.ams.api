using Application.DomainEvents;
using Domain.Common.Entities;
using Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using NSubstitute;

namespace Features.Tests.Database;

/// <summary>Reads the EF model only; nothing connects to a database.</summary>
public class ModelDeleteBehaviorTests
{
    private static IModel BuildModel()
    {
        DbContextOptions<ApplicationDbContext> options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=not-used")
            .Options;
        using var context = new ApplicationDbContext(options, Substitute.For<IDomainEventsDispatcher>());
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
        Assert.DoesNotContain(BuildModel().FindEntityType(typeof(Location))!
            .GetForeignKeys()
, fk => fk.PrincipalEntityType.ClrType == typeof(ApiKey));

    [Fact]
    public void DeletingALocation_StillRemovesItsMeters() 
        => Assert.Equal(DeleteBehavior.Cascade, ForeignKey(typeof(Meter), typeof(Location)).DeleteBehavior);
}
