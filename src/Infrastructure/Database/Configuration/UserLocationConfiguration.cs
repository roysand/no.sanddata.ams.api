using Domain.Common;
using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configuration;

public class UserLocationConfiguration : IEntityTypeConfiguration<UserLocation>
{
    public void Configure(EntityTypeBuilder<UserLocation> builder)
    {
        builder.HasKey(ul => new { ul.UserId, ul.LocationId });

        builder.Property(ul => ul.UserId).IsRequired();
        builder.Property(ul => ul.LocationId).IsRequired();

        // Stored as text ("Owner" / "Viewer"). The default makes every link that existed before roles an owner link.
        builder.Property(ul => ul.Role)
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired()
            .HasDefaultValue(LocationRole.Owner);

        builder.HasIndex(ul => new { ul.UserId, ul.LocationId }).IsUnique();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(ul => ul.UserId);

        builder.HasOne<Location>()
            .WithMany()
            .HasForeignKey(ul => ul.LocationId);
    }
}
