using System.Linq.Expressions;

namespace Application.Common.Interfaces.Repositories;

public interface IRepository
{
}

public interface IRepository<T> : IRepository where T : class
{
    T Insert(T entity);
    T Update(T entity);
    bool Delete(T entity);
    Task<T?> GetAsync(int id, CancellationToken cancellationToken);
    Task<T?> GetAsync(string id, CancellationToken cancellationToken);
    Task<T?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<T?> GetAsync(object id, CancellationToken cancellationToken);

    Task<IEnumerable<T?>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken,
        bool noTrack = false);

    Task<IEnumerable<T?>> AllAsync(CancellationToken cancellationToken);
    Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
