namespace ERP.Application.Interfaces
{
    public interface IStocktakeService
    {
        Task<int> CreateStocktakeAsync(DTOs.CreateStocktakeDto dto, int createdByUserId);
        Task ApproveStocktakeAsync(int id, int approvedByUserId);
        Task UpdateStocktakeDetailAsync(int stocktakeId, int detailId, DTOs.UpdateStocktakeDetailDto dto);
        Task RecountStocktakeDetailAsync(int stocktakeId, int detailId, string? reason, int userId = 0);
    }
}

