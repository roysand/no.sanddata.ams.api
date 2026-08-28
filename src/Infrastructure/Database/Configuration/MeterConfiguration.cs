using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Common.Entities;

namespace Infrastructure.Database.Configuration;

public class MeterConfiguration : IEntityTypeConfiguration<Meter>
{
    public void Configure(EntityTypeBuilder<Meter> builder)
    {
        builder.HasKey("Id");

        builder.Property(m => m.DeviceId).IsRequired().HasMaxLength(100);
        builder.Property(m => m.MeterId).HasMaxLength(100);
        builder.Property(m => m.MeterType).HasMaxLength(50);
        builder.Property(m => m.Comment).HasMaxLength(200);
        builder.Property(m => m.IsActive);

        builder.HasIndex(m => new { m.LocationId, m.DeviceId }).IsUnique();

        builder.HasOne(m => m.Location)
            .WithMany(l => l.Meters)
            .HasForeignKey(m => m.LocationId);
    }
}
