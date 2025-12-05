using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configuration;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens", Schemas.Default);

        builder.HasKey(rt => rt.Id);

        builder.Property(rt => rt.Token)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(rt => rt.Token)
            .IsUnique();

        builder.Property(rt => rt.ExpiresAt)
            .IsRequired();

        builder.Property(rt => rt.CreatedAt)
            .IsRequired();

        builder.Property(rt => rt.RevokedAt)
            .IsRequired(false);

        builder.Property(rt => rt.ReplacedByToken)
            .HasMaxLength(256)
            .IsRequired(false);

        builder.Property(rt => rt.ReasonRevoked)
            .HasMaxLength(500)
            .IsRequired(false);

        builder.HasOne(rt => rt.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(rt => rt.UserId);
    }
}
