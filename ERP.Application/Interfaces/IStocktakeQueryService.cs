using System.Collections.Generic;
using System.Threading.Tasks;
using ERP.Application.DTOs;

namespace ERP.Application.Interfaces
{
    public interface IStocktakeQueryService
    {
        Task<IEnumerable<StocktakeSummaryDto>> GetStocktakesAsync();
        Task<StocktakeResponseDto> GetStocktakeByIdAsync(int id);
    }
}
