namespace Application.CQRS;

/// <summary>
/// Handler interface for queries that read state without side effects.
/// Implement this interface to handle a specific query.
/// </summary>
/// <typeparam name="TQuery">The query type to handle.</typeparam>
/// <typeparam name="TResult">The result type returned.</typeparam>
public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
{
    Task<TResult> Handle(TQuery query, CancellationToken cancellationToken);
}
