using Domain.Common;
using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configuration;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    // Fixed ids so the seed rows are identical in every environment and stable across migrations.
    private static readonly Guid AdminRoleId = new("a0000000-0000-0000-0000-000000000001");
    private static readonly Guid UserRoleId = new("a0000000-0000-0000-0000-000000000002");
    private static readonly DateTime SeededAt = new(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.HasKey("Id");

        builder.Property(r => r.Name).IsRequired().HasMaxLength(100);
        builder.Property(r => r.Description).IsRequired().HasMaxLength(100);
        builder.Property(r => r.IsActive);

        builder.HasMany(r => r.Users)
            .WithMany(u => u.Roles)
            .UsingEntity<UserRole>();

        // Seed rows bypass SaveChanges, so the audit columns are set explicitly.
        builder.HasData(
            new
            {
                Id = AdminRoleId,
                Name = RoleNames.Admin,
                Description = "Manages users, roles and location links",
                IsActive = true,
                CreatedAt = SeededAt,
                UpdatedAt = SeededAt
            },
            new
            {
                Id = UserRoleId,
                Name = RoleNames.User,
                Description = "Uses own account and own locations",
                IsActive = true,
                CreatedAt = SeededAt,
                UpdatedAt = SeededAt
            });
    }
}
