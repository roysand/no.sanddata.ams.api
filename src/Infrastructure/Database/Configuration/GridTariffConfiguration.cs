using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configuration;

public class GridTariffConfiguration : IEntityTypeConfiguration<GridTariff>
{
    public void Configure(EntityTypeBuilder<GridTariff> builder)
    {
        builder.HasKey("Id");

        builder.Property(t => t.Name).IsRequired().HasMaxLength(100);
        builder.HasIndex(t => t.Name).IsUnique();
    }
}
