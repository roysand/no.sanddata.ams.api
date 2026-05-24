using Application.Common;
using Application.CQRS;
using Application.DomainEvents;
using Features.Auth.Commands;
using Features.Auth.Handlers;
using Features.Users.Commands;
using Features.Users.Handlers;
using Features.Users.Queries;
using Microsoft.Extensions.DependencyInjection;
using Domain.Common;

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
        // Register custom CQRS Dispatcher
        services.AddScoped<IDispatcher, Dispatcher>();

        // Register CQRS handlers (use full namespace to avoid conflict with FastEndpoints.ICommandHandler)
        // Users - Commands
        services.AddScoped<Application.CQRS.ICommandHandler<CreateUserCommand, Result<CreateUserResponse>>, CreateUserCommandHandler>();
        services.AddScoped<Application.CQRS.ICommandHandler<UpdateUserCommand, Result<UpdateUserResponse>>, UpdateUserCommandHandler>();
        services.AddScoped<Application.CQRS.ICommandHandler<DeleteUserCommand, Result<DeleteUserResponse>>, DeleteUserCommandHandler>();
        services.AddScoped<Application.CQRS.ICommandHandler<ChangePasswordCommand, Result<ChangePasswordResponse>>, ChangePasswordCommandHandler>();

        // Users - Queries
        services.AddScoped<Application.CQRS.IQueryHandler<GetUserQuery, Result<GetUserResponse>>, GetUserQueryHandler>();
        services.AddScoped<Application.CQRS.IQueryHandler<GetUsersQuery, Result<PagedUsersResponse>>, GetUsersQueryHandler>();

        // Auth - Commands
        services.AddScoped<Application.CQRS.ICommandHandler<LoginCommand, Result<LoginResponse>>, LoginCommandHandler>();
        services.AddScoped<Application.CQRS.ICommandHandler<RefreshTokenCommand, Result<RefreshTokenResponse>>, RefreshTokenCommandHandler>();

        // Domain Events
        services.AddScoped<Application.DomainEvents.IDomainEventsDispatcher, Application.DomainEvents.DomainEventsDispatcher>();

        return services;
    }
}
