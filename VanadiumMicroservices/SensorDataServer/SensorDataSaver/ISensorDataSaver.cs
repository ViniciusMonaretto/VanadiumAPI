namespace SensorDataSaver
{
    public interface ISensorDataSaver
    {
        Task<bool> SubscribeAsync(string topic);
    }
}
