using ERP.Domain.Entities;

namespace ERP.Domain.Interfaces
{
    public interface IStocktakeRepository : IRepository<Stocktake>
    {
        Task<Stocktake?> GetByIdWithDetailsAsync(int id);
    }
}
