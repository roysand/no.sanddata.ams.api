using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configuration;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey("Id");

        builder.Property(u => u.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(u => u.LastName).IsRequired().HasMaxLength(100);
        builder.Property(u => u.PasswordHash).IsRequired().HasMaxLength(100);
        builder.OwnsOne(u => u.Email, email => email.Property(e => e.Value).IsRequired().HasMaxLength(100).HasColumnName("Email")); // Email is a Value Object
        builder.Property(u => u.IsActive);

        builder.HasMany(u => u.Roles)
            .WithMany(r => r.Users)
            .UsingEntity<UserRole>();

        builder.HasMany(u => u.Locations)
            .WithMany(l => l.Users)
            .UsingEntity<UserLocation>();

        // Roles are needed for every token and permission check; locations for the admin user list.
        // Note: DbContext.Find (GetByIdAsync) ignores AutoInclude - use FindAsync(predicate) to get them.
        builder.Navigation(u => u.Roles).AutoInclude();
        builder.Navigation(u => u.Locations).AutoInclude();
    }
}
