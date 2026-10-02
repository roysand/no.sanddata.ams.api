using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configuration;

public class ElectricityPriceConfiguration : IEntityTypeConfiguration<ElectricityPrice>
{
    public void Configure(EntityTypeBuilder<ElectricityPrice> builder)
    {
        builder.HasKey("Id");

        builder.Property(p => p.PriceRegion).IsRequired().HasMaxLength(10);
        builder.Property(p => p.HourStartUtc).IsRequired();
        builder.Property(p => p.PriceEurPerMwh).IsRequired().HasPrecision(18, 6);

        builder.HasIndex(p => new { p.PriceRegion, p.HourStartUtc }).IsUnique();
    }
}
