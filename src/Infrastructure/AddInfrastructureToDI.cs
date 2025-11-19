using Application.Abstractions.Data;
using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using Infrastructure.Database;
using Infrastructure.Database.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure;

public static class AddInfrastructureToDI
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Get connection string from configuration
        // Priority: 1. ConnectionStrings:DefaultConnection, 2. ApplicationSettings:DbConnectionString
        var connectionString = configuration.GetConnectionString("DefaultConnection")
                               ?? configuration["ApplicationSettings:DbConnectionString"]
                               ?? throw new InvalidOperationException("Connection string not found. Please configure 'ConnectionStrings:DefaultConnection' or 'ApplicationSettings:DbConnectionString'.");

        // Register DbContext with SQL Server
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(connectionString));

        // Register DbContext interface
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

        // Register Repositories
        services.AddScoped<IApiKeyEfRepository<ApiKey>, ApiKeyEfRepository>();
        services.AddScoped<IUserEfRepository<User>, UserEfRepository>();
        services.AddScoped<ILocationEfRepository<Location>, LocationEfRepository>();
        services.AddScoped<IRoleEfRepository<Role>, RoleEfRepository>();
        services.AddScoped<IUserLocationEfRepository<UserLocation>, UserLocationEfRepository>();
        services.AddScoped<IUserRoleEfRepository<UserRole>, UserRoleEfRepository>();

        return services;
    }
}
