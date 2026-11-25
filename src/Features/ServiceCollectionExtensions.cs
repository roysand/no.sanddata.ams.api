using Features.Generated;
using Microsoft.Extensions.DependencyInjection;

namespace Features;

/// <summary>
/// Extension methods for registering feature services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers CQRS handlers and related services.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddFeatures(this IServiceCollection services)
    {
        // Registers the generated CQRS dispatcher plus every ICommandHandler/IQueryHandler implementation
        // discovered at compile time. See Cqrs.SourceGenerator and Features.Generated.GeneratedDispatcher -
        // new handlers are picked up automatically on the next build, no manual registration needed here.
        services.AddGeneratedCqrsHandlers();

        services.AddHostedService<Features.Users.Services.AdminBootstrapService>();
        services.AddHostedService<Features.ElectricityCost.Services.PriceFetchService>();
        services.AddHostedService<Features.WaterHeaters.Services.WaterHeaterSamplingService>();
        services.AddHostedService<Features.EvChargers.Services.EvChargerReadService>();
        // Spec 021: one shared Zaptec state read per charger (3 minutes, 1 after a failure).
        services.AddSingleton<Features.EvChargers.Services.ChargerStateCache>();
        services.AddScoped<Features.ElectricityCost.Services.HourlyCostProvider>();

        return services;
    }
}
