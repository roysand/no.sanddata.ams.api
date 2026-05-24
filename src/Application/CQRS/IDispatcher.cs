namespace Application.CQRS;

/// <summary>
/// Dispatcher interface for sending commands and queries to their handlers.
/// Resolves and executes handlers via dependency injection.
/// </summary>
public interface IDispatcher
{
    Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken cancellationToken);
    Task<TResult> Send<TResult>(IQuery<TResult> query, CancellationToken cancellationToken);
}
