using System.Text;
using Application.Abstractions.Data;
using Application.Common.Interfaces.External;
using Application.Common.Interfaces.Repositories;
using Application.ElectricityCost;
using Domain.Common.Entities;
using Infrastructure.Authentication;
using Infrastructure.Database;
using Infrastructure.Database.Repositories;
using Infrastructure.External;
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
        string connectionString = configuration.GetConnectionString("DefaultConnection")
                                  ?? configuration["ApplicationSettings:DbConnectionString"]
                                  ?? throw new InvalidOperationException("Connection string not found. Please configure 'ConnectionStrings:DefaultConnection' or 'ApplicationSettings:DbConnectionString'.");

        // Register DbContext with PostgreSQL
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString));

        // Register DbContext interface
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

        // Register Repositories
        services.AddScoped<IApiKeyRepository<ApiKey>, ApiKeyEfRepository>();
        services.AddScoped<IUserRepository<User>, UserEfRepository>();
        services.AddScoped<ILocationRepository<Location>, LocationEfRepository>();
        services.AddScoped<IRoleRepository<Role>, RoleEfRepository>();
        services.AddScoped<IUserLocationRepository<UserLocation>, UserLocationEfRepository>();
        services.AddScoped<IUserRoleRepository<UserRole>, UserRoleEfRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenEfRepository>();
        services.AddScoped<IMeasurementRepository<Measurement>, MeasurementEfRepository>();
        services.AddScoped<IMeterRepository<Meter>, MeterEfRepository>();
        services.AddScoped<IElectricityPriceRepository<ElectricityPrice>, ElectricityPriceEfRepository>();
        services.AddScoped<IExchangeRateRepository<ExchangeRate>, ExchangeRateEfRepository>();
        services.AddScoped<IConsumptionRepository, ConsumptionEfRepository>();

        // Electricity cost: options, external clients, calculator
        services.AddOptions<EntsoeOptions>().Bind(configuration.GetSection(EntsoeOptions.SectionName));
        services.AddSingleton(configuration.GetSection(FlatRateOptions.SectionName).Get<FlatRateOptions>()
                              ?? new FlatRateOptions());
        services.AddHttpClient<ISpotPriceClient, EntsoeSpotPriceClient>(client =>
            client.BaseAddress = new Uri("https://web-api.tp.entsoe.eu/api"));
        services.AddHttpClient<IExchangeRateClient, NorgesBankExchangeRateClient>(client =>
            client.BaseAddress = new Uri("https://data.norges-bank.no/api/data/EXR/B.EUR.NOK.SP"));
        services.AddScoped<CostCalculator>();

        // Register Authentication Services
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            string secretKey = configuration["JwtSettings:SecretKey"];

            // If secret key is not configured, use an invalid key to ensure token validation always fails
            // This allows the app to start but will result in 401 responses for authenticated endpoints
            if (string.IsNullOrEmpty(secretKey))
            {
                secretKey = "INVALID_KEY_FOR_MISSING_CONFIGURATION_DO_NOT_USE_IN_PRODUCTION";
            }

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

        // Register Password Hasher
        services.AddScoped<IPasswordHasher, PasswordHasher>();

        return services;
    }
}
