using System.Linq.Expressions;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Repositories
{
    public class Repository<T> : IRepository<T> where T : class
    {
        protected readonly ErpKhoDbContext _context;
        protected readonly DbSet<T> _dbSet;

        public Repository(ErpKhoDbContext context)
        {
            _context = context;
            _dbSet = context.Set<T>();
        }

        public virtual async Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return await _dbSet.FindAsync(new object?[] { id }, cancellationToken).AsTask();
        }

        public virtual async Task<IEnumerable<T>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return await _dbSet.ToListAsync(cancellationToken);
        }

        public virtual async Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        {
            return await _dbSet.Where(predicate).ToListAsync(cancellationToken);
        }

        public virtual async Task<T> AddAsync(T entity, CancellationToken cancellationToken = default)
        {
            try
            {
                await _dbSet.AddAsync(entity, cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);
                return entity;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new ERP.Domain.Exceptions.ConcurrencyException("Dữ liệu đã bị thay đổi bởi người khác.", ex);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 1205 })
            {
                throw new ERP.Domain.Exceptions.DeadlockException("Giao dịch database bị deadlock.", ex);
            }
        }

        public virtual async Task UpdateAsync(T entity, CancellationToken cancellationToken = default)
        {
            try
            {
                _dbSet.Update(entity);
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new ERP.Domain.Exceptions.ConcurrencyException("Dữ liệu đã bị thay đổi bởi người khác.", ex);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 1205 })
            {
                throw new ERP.Domain.Exceptions.DeadlockException("Giao dịch database bị deadlock.", ex);
            }
        }

        public virtual async Task DeleteAsync(T entity, CancellationToken cancellationToken = default)
        {
            try
            {
                _dbSet.Remove(entity);
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new ERP.Domain.Exceptions.ConcurrencyException("Dữ liệu đã bị thay đổi bởi người khác.", ex);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 1205 })
            {
                throw new ERP.Domain.Exceptions.DeadlockException("Giao dịch database bị deadlock.", ex);
            }
        }
    }
}
