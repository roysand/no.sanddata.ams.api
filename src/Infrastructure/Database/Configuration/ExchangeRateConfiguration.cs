using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configuration;

public class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        builder.HasKey("Id");

        builder.Property(r => r.CurrencyPair).IsRequired().HasMaxLength(10);
        builder.Property(r => r.RateDate).IsRequired();
        builder.Property(r => r.Rate).IsRequired().HasPrecision(18, 6);

        builder.HasIndex(r => new { r.CurrencyPair, r.RateDate }).IsUnique();
    }
}
