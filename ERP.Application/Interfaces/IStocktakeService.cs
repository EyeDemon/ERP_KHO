namespace ERP.Application.Interfaces
{
    public interface IStocktakeService
    {
        Task<int> CreateStocktakeAsync(DTOs.CreateStocktakeDto dto, int createdByUserId);
        Task ApproveStocktakeAsync(int id, int approvedByUserId);
        Task UpdateStocktakeDetailAsync(int stocktakeId, int detailId, DTOs.UpdateStocktakeDetailDto dto);
    }
}
