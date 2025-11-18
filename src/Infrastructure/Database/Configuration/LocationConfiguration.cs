using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Common.Entities;

namespace Infrastructure.Database.Configuration;

public class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.HasKey("Id");

        builder.Property(l => l.Name).IsRequired();
        builder.Property(l => l.Address).IsRequired();
        builder.Property(l => l.SerialNumber).IsRequired();
        builder.Property(l => l.Zone).IsRequired();
        builder.Property(l => l.IsActive);
        builder.Property(l => l.HasNorgesPriceAgreement);

        builder.HasOne(l => l.ApiKey)
            .WithOne(a => a.Location);

        builder.HasMany(l => l.Users)
            .WithMany(u => u.Locations)
            .UsingEntity<UserLocation>();
    }
}
