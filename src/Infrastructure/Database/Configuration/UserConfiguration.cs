using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Common.Entities;

namespace Infrastructure.Database.Configuration;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey("Id");

        builder.Property(u => u.FirstName).IsRequired();
        builder.Property(u => u.LastName).IsRequired();
        builder.Property(u => u.PasswordHash).IsRequired();
        builder.Property(u => u.Email.Value).HasColumnName("Email").IsRequired();
        builder.Property(u => u.IsActive);

        builder.HasMany(u => u.Roles)
            .WithMany(r => r.Users)
            .UsingEntity<UserRole>();

        builder.HasMany(u => u.Locations)
            .WithMany(l => l.Users)
            .UsingEntity<UserLocation>();
    }
}
