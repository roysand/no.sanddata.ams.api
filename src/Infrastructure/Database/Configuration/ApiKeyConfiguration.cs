using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configuration;

public class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.HasKey("Id");

        builder.Property(a => a.KeyHash).IsRequired().HasMaxLength(64);
        builder.Property(a => a.KeyHint).IsRequired().HasMaxLength(8);
        builder.HasIndex(a => a.KeyHash).IsUnique();
        builder.Property(a => a.Description).IsRequired().HasMaxLength(100);
        builder.Property(a => a.IsActive);
        builder.Property(a => a.CreatedAt);
        builder.Property(a => a.ExpiresAt);

        builder.HasOne(a => a.Location)
            .WithOne(l => l.ApiKey)
            .HasForeignKey<Location>("ApiKeyId");

        builder.HasMany(a => a.Users)
            .WithMany();
    }
}
