namespace Services
{
    public interface ISensorDataSaver
    {
        Task<bool> SubscribeAsync(string topic);
    }
}
