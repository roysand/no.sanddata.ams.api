using System.Linq.Expressions;
using Application.Common.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Database.Repositories;

public class GenericEfRepository<T> : IEfRepository<T> where T : class
{
    private readonly ApplicationDbContext _context;

    public GenericEfRepository(ApplicationDbContext applicationDbContext)
    {
        _context = applicationDbContext;
    }
     public virtual T Insert(T entity)
    {
        return _context
            .Add(entity)
            .Entity;
    }

    public virtual T Update(T entity)
    {
        return _context.Update(entity)
            .Entity;
    }

    public virtual bool Delete(T entity)
    {
        _context.Remove(entity);

        var count = _context.SaveChanges();
        return count >= 1;
    }

    public async virtual Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            return await _context.FindAsync<T>(new object[] {id}, cancellationToken);
        }
        catch (ArgumentException)
        {
            // The entity type doesn't support int keys, return null
            return null;
        }
    }

    public async virtual Task<T?> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        // Try to parse as Guid first, then fall back to string
        if (Guid.TryParse(id, out var guidId))
        {
            return await _context.FindAsync<T>(new object[] {guidId}, cancellationToken);
        }
        return await _context.FindAsync<T>(new object[] {id}, cancellationToken);
    }

    public virtual async Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.FindAsync<T>(new object[] {id}, cancellationToken);
    }

    public virtual async Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken)
    {
        return await _context.FindAsync<T>(new object[] {id}, cancellationToken);
    }

    public virtual async Task<IEnumerable<T?>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken, bool noTrack = false)
    {
        var query = _context.Set<T>()
            .AsQueryable()
            .Where(predicate);

        if(noTrack)
        {
            query = query.AsNoTracking();
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<T?>> AllAsync(CancellationToken cancellationToken)
    {
        return await _context.Set<T>().ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken)
    {
        var result = await _context.Set<T>()
            .AsQueryable()
            .Where(predicate)
            .CountAsync(cancellationToken);

        return result > 0;
    }

    public virtual async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        return await _context.SaveChangesAsync(cancellationToken);

    }
}
