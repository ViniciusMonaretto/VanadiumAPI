using Shared.Models;

namespace VanadiumAPI.SensorDataSaver
{
    public interface ISensorDataSaver
    {
        void PushSensorData(SensorDataMessageModel message);
        Task<bool> SubscribeAsync(string topic);
    }
}
