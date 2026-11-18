using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configuration;

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.HasKey("Id");

        builder.Property(l => l.Name).IsRequired().HasMaxLength(100);
        builder.Property(l => l.Address).IsRequired().HasMaxLength(100);
        builder.Property(l => l.SerialNumber).IsRequired().HasMaxLength(100);
        builder.HasIndex(l => l.SerialNumber).IsUnique();
        builder.Property(l => l.Zone).IsRequired().HasMaxLength(10);
        builder.Property(l => l.IsActive);
        builder.Property(l => l.HasNorgesPriceAgreement);
        builder.Property(l => l.SupplierMarkupPerKwh).IsRequired().HasPrecision(9, 4).HasDefaultValue(0m);
        builder.Property(l => l.SupplierMonthlyFee).IsRequired(false).HasPrecision(9, 4);
        builder.Property(l => l.ElectricityTaxExempt).IsRequired().HasDefaultValue(false);
        builder.Property(l => l.VatExempt).IsRequired().HasDefaultValue(false);
        builder.Property(l => l.IsHolidayHome).IsRequired().HasDefaultValue(false);

        builder.HasOne<GridTariff>()
            .WithMany()
            .HasForeignKey(l => l.GridTariffId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(l => l.Users)
            .WithMany(u => u.Locations)
            .UsingEntity<UserLocation>();
    }
}
