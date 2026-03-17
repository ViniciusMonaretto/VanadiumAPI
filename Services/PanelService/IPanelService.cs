using VanadiumAPI.DTO;

namespace VanadiumAPI.Services
{
    public interface IPanelService
    {
        Task<(PanelDto? Panel, string? Error)> AddPanelAsync(AddPanelDto dto, int enterpriseId);
        Task<(PanelDto? Panel, string? Error)> UpdatePanelAsync(UpdatePanelDto dto, int enterpriseId);
        Task<(bool Success, string? Error)> DeletePanelAsync(int panelId, int enterpriseId);
    }
}
