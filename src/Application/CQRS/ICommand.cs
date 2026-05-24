namespace Application.CQRS;

/// <summary>
/// Marker interface for commands that mutate state.
/// Commands should always return a Result type for explicit error handling.
/// </summary>
/// <typeparam name="TResult">The result type returned by the command.</typeparam>
public interface ICommand<out TResult>;
