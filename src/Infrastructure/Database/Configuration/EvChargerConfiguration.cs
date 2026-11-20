using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configuration;

public class EvChargerConfiguration : IEntityTypeConfiguration<EvCharger>
{
    public void Configure(EntityTypeBuilder<EvCharger> builder)
    {
        builder.HasKey("Id");

        builder.Property(c => c.LocationId).IsRequired();
        builder.Property(c => c.ChargerId).IsRequired();
        builder.Property(c => c.ChargerName).IsRequired().HasMaxLength(200);
        builder.Property(c => c.InstallationId).IsRequired();
        builder.Property(c => c.InstallationName).IsRequired().HasMaxLength(200);
        builder.Property(c => c.ReadUpToUtc).IsRequired(false);

        builder.HasIndex(c => c.LocationId).IsUnique();
        builder.HasIndex(c => c.ChargerId).IsUnique();

        builder.HasOne<Location>()
            .WithMany()
            .HasForeignKey(c => c.LocationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
