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

        // The key belongs to its location: deleting a location deletes its key, and a key can neither
        // outlive its location nor take the location (with its meters and measurements) along when deleted.
        builder.HasOne(a => a.Location)
            .WithOne(l => l.ApiKey)
            .HasForeignKey<ApiKey>("LocationId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex("LocationId").IsUnique();
    }
}
