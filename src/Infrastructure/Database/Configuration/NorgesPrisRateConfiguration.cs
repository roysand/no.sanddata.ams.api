using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configuration;

public class NorgesPrisRateConfiguration : IEntityTypeConfiguration<NorgesPrisRate>
{
    /// <summary>The rate that applied before rates became data (the former NorgesPris configuration section).</summary>
    public static readonly Guid SeedId = new("b0000000-0000-0000-0000-000000000001");

    public void Configure(EntityTypeBuilder<NorgesPrisRate> builder)
    {
        builder.HasKey("Id");

        builder.Property(r => r.ValidFrom).IsRequired();
        builder.Property(r => r.RatePerKwh).IsRequired().HasPrecision(9, 4);
        builder.Property(r => r.TaxPerKwh).IsRequired().HasPrecision(9, 4);
        builder.Property(r => r.AddedByUserId).IsRequired();

        builder.HasIndex(r => r.ValidFrom).IsUnique();

        // A fixed start date long before any measurement, so every stored hour has a rate and no figure changes.
        var seededAt = new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc);
        var seed = new NorgesPrisRate(SeedId, new DateOnly(2020, 1, 1), 0.40m, 0m, Guid.Empty)
        {
            CreatedAt = seededAt,
            UpdatedAt = seededAt
        };
        builder.HasData(seed);
    }
}
