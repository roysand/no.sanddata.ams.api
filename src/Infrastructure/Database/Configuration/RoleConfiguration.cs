using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Common.Entities;

namespace Infrastructure.Database.Configuration;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.HasKey("Id");

        builder.Property(r => r.Name).IsRequired();
        builder.Property(r => r.Description).IsRequired();
        builder.Property(r => r.IsActive);

        builder.HasMany(r => r.Users)
            .WithMany(u => u.Roles)
            .UsingEntity<UserRole>();
    }
}
