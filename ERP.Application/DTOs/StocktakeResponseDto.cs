using System.Collections.Generic;

namespace ERP.Application.DTOs
{
    public class StocktakeResponseDto : StocktakeSummaryDto
    {
        public IEnumerable<StocktakeDetailRowDto> Details { get; set; } = new List<StocktakeDetailRowDto>();
    }
}
