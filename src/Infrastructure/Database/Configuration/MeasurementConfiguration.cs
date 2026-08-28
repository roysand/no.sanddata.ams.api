using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Common.Entities;

namespace Infrastructure.Database.Configuration;

public class MeasurementConfiguration : IEntityTypeConfiguration<Measurement>
{
    public void Configure(EntityTypeBuilder<Measurement> builder)
    {
        builder.HasKey("Id");

        builder.Property(m => m.LocationId).IsRequired();
        builder.Property(m => m.DeviceId).IsRequired().HasMaxLength(100);
        builder.Property(m => m.Timestamp).IsRequired();
        builder.Property(m => m.MeterId).HasMaxLength(100);
        builder.Property(m => m.MeterType).HasMaxLength(50);
        builder.Property(m => m.PowerWatts).IsRequired();

        builder.HasIndex(m => new { m.LocationId, m.Timestamp });
    }
}
