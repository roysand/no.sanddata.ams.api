using System.Reflection;

namespace Application.CQRS;

/// <summary>
/// Dispatcher implementation that resolves and executes handlers via dependency injection.
/// Uses reflection to find the appropriate handler for a given command or query.
/// </summary>
public class Dispatcher : IDispatcher
{
    private readonly IServiceProvider _serviceProvider;

    public Dispatcher(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

    public async Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken cancellationToken)
    {
        Type handlerType = typeof(ICommandHandler<,>).MakeGenericType(command.GetType(), typeof(TResult));
        object? handler = _serviceProvider.GetService(handlerType);

        if (handler is null)
        {
            throw new InvalidOperationException($"No handler registered for {command.GetType().Name}");
        }

        MethodInfo? handleMethod = handlerType.GetMethod("Handle");
        if (handleMethod is null)
        {
            throw new InvalidOperationException($"Handler {handlerType.Name} does not have a Handle method");
        }

        return await (Task<TResult>)handleMethod.Invoke(handler, [command, cancellationToken])!;
    }

    public async Task<TResult> Send<TResult>(IQuery<TResult> query, CancellationToken cancellationToken)
    {
        Type handlerType = typeof(IQueryHandler<,>).MakeGenericType(query.GetType(), typeof(TResult));
        object? handler = _serviceProvider.GetService(handlerType);

        if (handler is null)
        {
            throw new InvalidOperationException($"No handler registered for {query.GetType().Name}");
        }

        MethodInfo? handleMethod = handlerType.GetMethod("Handle");
        if (handleMethod is null)
        {
            throw new InvalidOperationException($"Handler {handlerType.Name} does not have a Handle method");
        }

        return await (Task<TResult>)handleMethod.Invoke(handler, [query, cancellationToken])!;
    }
}
