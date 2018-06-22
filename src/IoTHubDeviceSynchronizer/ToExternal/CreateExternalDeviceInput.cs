using System.Collections.Generic;

namespace IoTHubDeviceSynchronizer.ToExternal
{
    public class CreateExternalDeviceInput
    {
        public string IotHubName { get; set; }
        public string DeviceId { get; set; }

        public Dictionary<string, string> Properties { get; set; }
    }
}