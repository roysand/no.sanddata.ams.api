using Application.Abstractions.Data;
using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Infrastructure.Database;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options), IApplicationDbContext
{
    public DbSet<ApiKey> ApiKey { get; set; } = null!;
    public DbSet<User> User { get; set; } = null!;
    public DbSet<Location> Location { get; set; } = null!;
    public DbSet<UserLocation> UserLocation { get; set; } = null!;
    public DbSet<Role> Role { get; set; } = null!;
    public DbSet<UserRole> UserRole { get; set; } = null!;
    public DbSet<RefreshToken> RefreshToken { get; set; } = null!;
    public DbSet<Measurement> Measurement { get; set; } = null!;
    public DbSet<Meter> Meter { get; set; } = null!;
    public DbSet<ElectricityPrice> ElectricityPrice { get; set; } = null!;
    public DbSet<ExchangeRate> ExchangeRate { get; set; } = null!;
    public DbSet<NorgesPrisRate> NorgesPrisRate { get; set; } = null!;
    public DbSet<PriceRate> PriceRate { get; set; } = null!;
    public DbSet<GridTariff> GridTariff { get; set; } = null!;
    public DbSet<GridTariffRate> GridTariffRate { get; set; } = null!;
    public DbSet<GridCapacityStep> GridCapacityStep { get; set; } = null!;
    public DbSet<WaterHeater> WaterHeater { get; set; } = null!;
    public DbSet<WaterHeaterPowerSample> WaterHeaterPowerSample { get; set; } = null!;
    public DbSet<EvCharger> EvCharger { get; set; } = null!;
    public DbSet<ChargingSession> ChargingSession { get; set; } = null!;
    public DbSet<ChargingSessionHour> ChargingSessionHour { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            string connectionString = Environment.GetEnvironmentVariable("ApplicationSettings:DbConnectionString") ?? throw new InvalidOperationException("Connection string not found.");
            optionsBuilder.UseNpgsql(connectionString);

        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        modelBuilder.HasDefaultSchema(Schemas.Default);

        // Read-only projections of TimescaleDB continuous aggregates - never written to via EF
        // Core, so they're mapped as keyless views rather than tracked entities.
        modelBuilder.Entity<MinuteConsumption>().HasNoKey().ToView("measurement_minute", Schemas.Default);
        modelBuilder.Entity<HourConsumption>().HasNoKey().ToView("measurement_hour", Schemas.Default);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateSystemColumns();

        return await base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateSystemColumns()
    {
        DateTime now = DateTime.UtcNow;

        foreach (EntityEntry<Entity> entry in ChangeTracker
                     .Entries<Entity>())
        {
            entry.Entity.UpdatedAt = now;
        }

        foreach (EntityEntry<Entity> entry in ChangeTracker
                     .Entries<Entity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    break;

                case EntityState.Modified:
                    // entry.Entity.BdnChangedByUser = databaseUserName;
                    break;
            }
        }
    }
}
