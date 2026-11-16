using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configuration;

public class GridTariffRateConfiguration : IEntityTypeConfiguration<GridTariffRate>
{
    public void Configure(EntityTypeBuilder<GridTariffRate> builder)
    {
        builder.HasKey("Id");

        builder.Property(r => r.ValidFrom).IsRequired();
        builder.Property(r => r.DayRatePerKwh).IsRequired().HasPrecision(9, 4);
        builder.Property(r => r.NightRatePerKwh).IsRequired().HasPrecision(9, 4);
        builder.Property(r => r.AddedByUserId).IsRequired();

        // A tariff in use can never disappear under the rates and locations that refer to it.
        builder.HasOne<GridTariff>()
            .WithMany()
            .HasForeignKey(r => r.GridTariffId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.GridTariffId, r.ValidFrom }).IsUnique();
    }
}
