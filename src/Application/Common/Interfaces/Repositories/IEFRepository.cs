using System.Linq.Expressions;

namespace Application.Common.Interfaces.Repositories;

public interface IEfRepository
{
}

public interface IEfRepository<T> : IEfRepository where T : class
{
    T Insert(T entity);
    T Update(T entity);
    bool Delete(T entity);
    Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<T?> GetByIdAsync(string id, CancellationToken cancellationToken);
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken);

    Task<IEnumerable<T?>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken,
        bool noTrack = false);

    Task<IEnumerable<T?>> AllAsync(CancellationToken cancellationToken);
    Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
