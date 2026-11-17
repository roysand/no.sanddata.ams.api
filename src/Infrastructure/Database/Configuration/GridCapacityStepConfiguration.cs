using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configuration;

public class GridCapacityStepConfiguration : IEntityTypeConfiguration<GridCapacityStep>
{
    public void Configure(EntityTypeBuilder<GridCapacityStep> builder)
    {
        builder.HasKey("Id");

        builder.Property(s => s.ValidFrom).IsRequired();
        builder.Property(s => s.LowerKw).IsRequired().HasPrecision(9, 4);
        builder.Property(s => s.UpperKw).HasPrecision(9, 4);
        builder.Property(s => s.MonthlyAmount).IsRequired().HasPrecision(9, 4);
        builder.Property(s => s.AddedByUserId).IsRequired();

        builder.HasOne<GridTariff>()
            .WithMany()
            .HasForeignKey(s => s.GridTariffId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.GridTariffId, s.ValidFrom, s.LowerKw }).IsUnique();
    }
}
