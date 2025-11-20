using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Common.Entities;

namespace Infrastructure.Database.Configuration;

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.HasKey("Id");

        builder.Property(l => l.Name).IsRequired().HasMaxLength(100);
        builder.Property(l => l.Address).IsRequired().HasMaxLength(100);
        builder.Property(l => l.SerialNumber).IsRequired().HasMaxLength(100);
        builder.Property(l => l.Zone).IsRequired().HasMaxLength(10);
        builder.Property(l => l.IsActive);
        builder.Property(l => l.HasNorgesPriceAgreement);

        builder.HasOne(l => l.ApiKey)
            .WithOne(a => a.Location);

        builder.HasMany(l => l.Users)
            .WithMany(u => u.Locations)
            .UsingEntity<UserLocation>();
    }
}
