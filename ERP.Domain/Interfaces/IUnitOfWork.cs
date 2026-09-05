namespace ERP.Domain.Interfaces
{
    public interface IUnitOfWork
    {
        bool HasExternalTransaction => false;
        Task BeginTransactionAsync();
        Task CommitTransactionAsync();
        Task RollbackTransactionAsync();
        Task<int> SaveChangesAsync();
    }
}
