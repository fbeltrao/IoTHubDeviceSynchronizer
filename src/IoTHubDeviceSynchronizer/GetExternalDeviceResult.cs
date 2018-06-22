using Newtonsoft.Json.Linq;

namespace IoTHubDeviceSynchronizer
{
    public class GetExternalDeviceResult
    {
        public JArray Devices { get; set; }
        public bool HasMore { get; set; }
        public GetExternalDeviceResult()
        {
        }
        public GetExternalDeviceResult(JArray devices, bool hasMore)
        {
            this.Devices = devices;
            HasMore = hasMore;
        }

        
    }
}
