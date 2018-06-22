using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using IoTHubDeviceSynchronizer.ToExternal;
using Microsoft.Azure.Devices;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json.Linq;

namespace IoTHubDeviceSynchronizer
{
    /// <summary>
    /// Defines an interface to communicate with an external device registry
    /// </summary>
    public interface IExternalDeviceRegistryService
    {
        /// <summary>
        /// Name of the external device property which contains the IoT Hub DeviceId value
        /// </summary>
        string DeviceIdPropertyNameInExternalDevice { get; }

        /// <summary>
        /// Creates a device in external system
        /// Needs to implemented only in scenarios where IoT Hub is the master
        /// </summary>
        /// <param name="deviceId"></param>
        /// <param name="properties"></param>
        /// <returns></returns>
        Task CreateExternalDevice(string deviceId, IDictionary<string, string> properties);

        /// <summary>
        /// Deletes a device in external system
        /// Needs to implemented only in scenarios where IoT Hub is the master
        /// </summary>
        /// <param name="device"></param>
        /// <returns></returns>
        Task DeleteExternalDevice(JToken device);

        /// <summary>
        /// Required properties in IoT Hub device to send to external system
        /// Indicates which properties of the IoT Hub twin tags must exist before creating the external device
        /// </summary>
        IEnumerable<string> GetRequiredTwinProperties();

        /// <summary>
        /// Gets the IoT Hub device Id value from a external device
        /// </summary>
        /// <param name="externalDevice"></param>
        /// <returns></returns>
        string GetDeviceIdFromExternalDevice(JToken externalDevice);

        /// <summary>
        /// Gets the external devices
        /// Must be implemented only if the external system is the master
        /// </summary>
        /// <param name="pageIndex"></param>
        /// <returns></returns>
        Task<GetExternalDeviceResult> GetExternalDevices(int pageIndex);

        /// <summary>
        /// Create a new IoT Hub Device
        /// Must be implemented only if the external system is the master
        /// </summary>
        /// <param name="externalDevice"></param>
        /// <returns></returns>
        ExportImportDevice CreateNewIoTHubDevice(JToken externalDevice);

        /// <summary>
        /// Validates the device properties before creating in the external system
        /// </summary>
        /// <param name="properties"></param>
        void ValidateTwinProperties(IReadOnlyDictionary<string, string> properties);
    }
}
