using Application.Abstractions.Data;
using Application.DomainEvents;
using Domain.Common;
using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Infrastructure.Database;

public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    IDomainEventsDispatcher domainEventsDispatcher)
    : DbContext(options), IApplicationDbContext
{
    public DbSet<ApiKey> ApiKey { get; set; }
    public DbSet<User> User { get; set; }
    public DbSet<Location> Location { get; set; }
    public DbSet<UserLocation> UserLocation { get; set; }
    public DbSet<Role> Role { get; set; }
    public DbSet<UserRole> UserRole { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            var connectionString = Environment.GetEnvironmentVariable("ApplicationSettings:DbConnectionString") ?? throw new InvalidOperationException("Connection string not found.");
            optionsBuilder.UseSqlServer(connectionString);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        modelBuilder.HasDefaultSchema(Schemas.Default);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateSystemColumns();

        // When should you publish domain events?
        //
        // 1. BEFORE calling SaveChangesAsync
        //     - domain events are part of the same transaction
        //     - immediate consistency
        // 2. AFTER calling SaveChangesAsync
        //     - domain events are a separate transaction
        //     - eventual consistency
        //     - handlers can fail

        int result = await base.SaveChangesAsync(cancellationToken);

        await PublishDomainEventsAsync();

        return result;
    }

    private async Task PublishDomainEventsAsync()
    {
        // Todo: Implement domain events publishing logic here.

        // var domainEvents = ChangeTracker
        //     .Entries<Entity>()
        //     .Select(entry => entry.Entity)
        //     .SelectMany(entity =>
        //     {
        //         List<IDomainEvent> domainEvents = entity.DomainEvents;
        //
        //         entity.ClearDomainEvents();
        //
        //         return domainEvents;
        //     })
        //     .ToList();
        //
        // await domainEventsDispatcher.DispatchAsync(domainEvents);
    }

    private void UpdateSystemColumns()
    {
        const string timeZoneId = "Europe/Oslo";
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);

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
