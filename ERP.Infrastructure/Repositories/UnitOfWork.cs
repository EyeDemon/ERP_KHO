using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ERP.Infrastructure.Repositories
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly ErpKhoDbContext _context;
        private IDbContextTransaction? _transaction;

        public UnitOfWork(ErpKhoDbContext context)
        {
            _context = context;
        }

        public bool HasExternalTransaction => _transaction == null && _context.Database.CurrentTransaction != null;

        public async Task BeginTransactionAsync()
        {
            if (_transaction != null || _context.Database.CurrentTransaction != null) return;
            try
            {
                _transaction = await _context.Database.BeginTransactionAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new ERP.Domain.Exceptions.ConcurrencyException("Dữ liệu đã bị thay đổi bởi người khác.", ex);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 1205 })
            {
                throw new ERP.Domain.Exceptions.DeadlockException("Giao dịch database bị deadlock.", ex);
            }
            catch (SqlException ex) when (ex.Number == 1205)
            {
                throw new ERP.Domain.Exceptions.DeadlockException("Giao dịch database bị deadlock.", ex);
            }
        }

        public async Task CommitTransactionAsync()
        {
            try
            {
                await _context.SaveChangesAsync();
                if (_transaction != null)
                {
                    await _transaction.CommitAsync();
                }
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new ERP.Domain.Exceptions.ConcurrencyException("Dữ liệu đã bị thay đổi bởi người khác.", ex);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 1205 })
            {
                throw new ERP.Domain.Exceptions.DeadlockException("Giao dịch database bị deadlock.", ex);
            }
            catch (SqlException ex) when (ex.Number == 1205)
            {
                throw new ERP.Domain.Exceptions.DeadlockException("Giao dịch database bị deadlock.", ex);
            }
            finally
            {
                if (_transaction != null)
                {
                    await _transaction.DisposeAsync();
                    _transaction = null;
                    _context.ChangeTracker.Clear();
                }
            }
        }

        public async Task RollbackTransactionAsync()
        {
            try
            {
                if (_transaction != null)
                {
                    await _transaction.RollbackAsync();
                }
            }
            finally
            {
                if (_transaction != null)
                {
                    await _transaction.DisposeAsync();
                    _transaction = null;
                }
            }
        }

        public async Task<int> SaveChangesAsync()
        {
            try
            {
                return await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new ERP.Domain.Exceptions.ConcurrencyException("Dữ liệu đã bị thay đổi bởi người khác.", ex);
            }
        }
    }
}
