using Shared.Models;

namespace VanadiumAPI.Services.PanelConfiguration
{
    /// <summary>
    /// Drives the SET_SENSOR_CONFIG round-trip that backs panel creation and deletion.
    /// Both Start* methods return immediately; the device round-trip runs detached.
    /// </summary>
    public interface IPanelConfigurationService
    {
        /// <summary>True while a device round-trip for this panel is in flight in this process.</summary>
        bool IsInFlight(int panelId);

        /// <summary>Sends SET_SENSOR_CONFIG(enabled:true). On "ok" clears Configuring and broadcasts the change;
        /// on failure leaves Configuring set and pushes an Error to the enterprise. Never throws.</summary>
        void StartEnable(Panel panel, int enterpriseId, int sensorId);

        /// <summary>Sends SET_SENSOR_CONFIG(enabled:false) and deletes the row, broadcasting PanelRemoved,
        /// only once the device answers "ok". On failure the row is left Configuring so a retry
        /// purges it locally. Never throws.</summary>
        void StartDisableAndDelete(Panel panel, int enterpriseId, int sensorId);
    }
}
