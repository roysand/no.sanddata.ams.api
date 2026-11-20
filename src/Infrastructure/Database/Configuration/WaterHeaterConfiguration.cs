using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configuration;

public class WaterHeaterConfiguration : IEntityTypeConfiguration<WaterHeater>
{
    public void Configure(EntityTypeBuilder<WaterHeater> builder)
    {
        builder.HasKey("Id");

        builder.Property(w => w.LocationId).IsRequired();
        builder.Property(w => w.DeviceId).IsRequired().HasMaxLength(200);
        builder.Property(w => w.ProtectedSubscriptionKey).IsRequired().HasMaxLength(500);
        builder.Property(w => w.SubscriptionKeyExpiresOn).IsRequired().HasColumnType("date");

        builder.HasIndex(w => w.LocationId).IsUnique();
        builder.HasIndex(w => w.DeviceId).IsUnique();

        builder.HasOne<Location>()
            .WithMany()
            .HasForeignKey(w => w.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
