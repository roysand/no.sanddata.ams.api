using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configuration;

public class ChargingSessionConfiguration : IEntityTypeConfiguration<ChargingSession>
{
    public void Configure(EntityTypeBuilder<ChargingSession> builder)
    {
        builder.HasKey("Id");

        builder.Property(s => s.LocationId).IsRequired();
        builder.Property(s => s.ZaptecSessionId).IsRequired();
        builder.Property(s => s.ChargerId).IsRequired();
        builder.Property(s => s.StartUtc).IsRequired();
        builder.Property(s => s.EndUtc).IsRequired();
        builder.Property(s => s.Kwh).IsRequired().HasPrecision(12, 4);
        builder.Property(s => s.HasEnergyDetails).IsRequired();

        builder.HasIndex(s => s.ZaptecSessionId).IsUnique();
        builder.HasIndex(s => new { s.LocationId, s.EndUtc });

        builder.HasMany(s => s.Hours)
            .WithOne()
            .HasForeignKey(h => h.ChargingSessionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Hours).HasField("_hours");

        // No foreign key to EvCharger on purpose: sessions are kept when the connection is removed.
        builder.HasOne<Location>()
            .WithMany()
            .HasForeignKey(s => s.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
