using Domain.Common;
using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Database.Configuration;

public class PriceRateConfiguration : IEntityTypeConfiguration<PriceRate>
{
    public void Configure(EntityTypeBuilder<PriceRate> builder)
    {
        builder.HasKey("Id");

        builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(r => r.ValidFrom).IsRequired();
        builder.Property(r => r.Value).IsRequired().HasPrecision(9, 4);
        builder.Property(r => r.AddedByUserId).IsRequired();

        builder.HasIndex(r => new { r.Kind, r.ValidFrom }).IsUnique();

        // The values in force when rates became data: 25 percent VAT, and the 2026 electricity tax and Enova fee.
        // The first measurement is from 2026, so every stored hour has a value.
        var seededAt = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);
        builder.HasData(
            Seed("b1000000-0000-0000-0000-000000000001", PriceRateKind.Vat, new DateOnly(2020, 1, 1), 0.25m, seededAt),
            Seed("b1000000-0000-0000-0000-000000000002", PriceRateKind.ElectricityTax, new DateOnly(2026, 1, 1), 0.0713m, seededAt),
            Seed("b1000000-0000-0000-0000-000000000003", PriceRateKind.EnovaFee, new DateOnly(2026, 1, 1), 0.01m, seededAt));

        // The Norgespris monthly caps (kWh) since Norgespris started: 5,000 for a home, 1,000 for a holiday home.
        var capsSeededAt = new DateTime(2026, 11, 17, 0, 0, 0, DateTimeKind.Utc);
        builder.HasData(
            Seed("b1000000-0000-0000-0000-000000000004", PriceRateKind.NorgesPrisCapHome, new DateOnly(2025, 10, 1), 5000m, capsSeededAt),
            Seed("b1000000-0000-0000-0000-000000000005", PriceRateKind.NorgesPrisCapHolidayHome, new DateOnly(2025, 10, 1), 1000m, capsSeededAt));
    }

    private static PriceRate Seed(string id, PriceRateKind kind, DateOnly validFrom, decimal value, DateTime at) =>
        new(new Guid(id), kind, validFrom, value, Guid.Empty) { CreatedAt = at, UpdatedAt = at };
}
