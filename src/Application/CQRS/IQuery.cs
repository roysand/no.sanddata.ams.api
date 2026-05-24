namespace Application.CQRS;

/// <summary>
/// Marker interface for queries that read state without side effects.
/// Queries should not mutate state or have observable side effects.
/// </summary>
/// <typeparam name="TResult">The result type returned by the query.</typeparam>
public interface IQuery<out TResult>;
