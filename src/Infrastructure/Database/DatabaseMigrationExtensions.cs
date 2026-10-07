using Infrastructure.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Database;

public static class DatabaseMigrationExtensions
{
    /// <summary>
    /// Applies pending EF Core migrations when RunMigrationsAtStartup is true. Call before app.Run() so the schema
    /// exists before hosted services start. A failed migration throws and stops startup.
    /// </summary>
    public static async Task ApplyMigrationsIfEnabledAsync(this WebApplication app, CancellationToken ct = default)
    {
        ILogger logger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Infrastructure.Database.Migrations");

        if (!app.Configuration.GetValue<bool>("RunMigrationsAtStartup"))
        {
            LogMessages.MigrationsSkipped(logger);
            return;
        }

        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        LogMessages.MigrationsStarting(logger);
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync(ct);
        LogMessages.MigrationsCompleted(logger);
    }
}
