namespace VanadiumAPI.Services.Prediction
{
    public interface IPredictionService
    {
        /// <summary>Queues a message from iocloud/{deviceId}/predict (<paramref name="isState"/> false)
        /// or the retained iocloud/{deviceId}/predict/state (true). Processed in order, off the MQTT thread.</summary>
        void PushPrediction(string deviceId, string payload, bool isState);
    }
}
