using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Common.Entities;

namespace Infrastructure.Database.Configuration;

public class UserLocationConfiguration : IEntityTypeConfiguration<UserLocation>
{
    public void Configure(EntityTypeBuilder<UserLocation> builder)
    {
        builder.HasKey(ul => new { ul.UserId, ul.LocationId });

        builder.Property(ul => ul.UserId).IsRequired();
        builder.Property(ul => ul.LocationId).IsRequired();

        builder.HasIndex(ul => new { ul.UserId, ul.LocationId }).IsUnique();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(ul => ul.UserId);

        builder.HasOne<Location>()
            .WithMany()
            .HasForeignKey(ul => ul.LocationId);
    }
}
