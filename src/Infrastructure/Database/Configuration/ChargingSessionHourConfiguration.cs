using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configuration;

public class ChargingSessionHourConfiguration : IEntityTypeConfiguration<ChargingSessionHour>
{
    public void Configure(EntityTypeBuilder<ChargingSessionHour> builder)
    {
        builder.HasKey("Id");

        builder.Property(h => h.ChargingSessionId).IsRequired();
        builder.Property(h => h.LocationId).IsRequired();
        builder.Property(h => h.HourStartUtc).IsRequired();
        builder.Property(h => h.Kwh).IsRequired().HasPrecision(12, 4);
        builder.Property(h => h.Estimated).IsRequired();

        builder.HasIndex(h => new { h.ChargingSessionId, h.HourStartUtc }).IsUnique();
        builder.HasIndex(h => new { h.LocationId, h.HourStartUtc });
    }
}
