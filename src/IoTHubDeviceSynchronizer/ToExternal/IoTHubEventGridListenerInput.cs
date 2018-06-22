namespace IoTHubDeviceSynchronizer.ToExternal
{
    public class IoTHubEventGridListenerInput
    {
        public IoTHubEventGridListenerInput()
        {
        }

        public IoTHubEventGridListenerInput(string deviceId, string iotHubName)
        {
            DeviceId = deviceId;
            IotHubName = iotHubName;
        }

        public string IotHubName { get; set; }
        public string DeviceId { get; set; }        
    }
}