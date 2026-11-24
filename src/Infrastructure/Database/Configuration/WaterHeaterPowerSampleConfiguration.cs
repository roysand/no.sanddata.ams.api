using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configuration;

public class WaterHeaterPowerSampleConfiguration : IEntityTypeConfiguration<WaterHeaterPowerSample>
{
    public void Configure(EntityTypeBuilder<WaterHeaterPowerSample> builder)
    {
        builder.HasKey("Id");

        builder.Property(s => s.LocationId).IsRequired();
        builder.Property(s => s.SampledAtUtc).IsRequired();
        builder.Property(s => s.PowerKw).IsRequired().HasPrecision(9, 6);

        // Spec 020: missing when OSO did not give it, and for every sample stored before; no backfill.
        builder.Property(s => s.AvailableLitres40);

        builder.HasIndex(s => new { s.LocationId, s.SampledAtUtc });

        // No foreign key to WaterHeater on purpose: samples are kept when the connection is removed.
        builder.HasOne<Location>()
            .WithMany()
            .HasForeignKey(s => s.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
