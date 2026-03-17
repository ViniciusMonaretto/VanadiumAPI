using Shared.Models;

namespace VanadiumAPI.SensorDataSaver
{
    public interface ISensorDataSaver
    {
        void PushSensorData(SensorDataMessageModel message);
        Task<bool> SubscribeAsync(string topic);
        void AddPanel(Panel panel);
        void UpdatePanel(Panel panel);
        void RemovePanel(int panelId, string gatewayId, string index);
    }
}
