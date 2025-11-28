using System.Text;
using Application.Abstractions.Data;
using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using Infrastructure.Authentication;
using Infrastructure.Database;
using Infrastructure.Database.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

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
        services.AddScoped<IRefreshTokenRepository, RefreshTokenEfRepository>();

        // Register Authentication Services
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            var secretKey = configuration["JwtSettings:SecretKey"]
                ?? throw new InvalidOperationException("JWT SecretKey not configured");

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
                ValidateIssuer = true,
                ValidIssuer = configuration["JwtSettings:Issuer"],
                ValidateAudience = true,
                ValidAudience = configuration["JwtSettings:Audience"],
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };
        })
        .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
            ApiKeyAuthenticationOptions.DefaultScheme,
            options => { });

        services.AddAuthorization();

        // Register JWT Token Service
        services.AddScoped<IJwtTokenService, JwtTokenService>();

        return services;
    }
}
