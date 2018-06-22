namespace IoTHubDeviceSynchronizer.ToAzure
{

    public class UpdateDevicesActivityResult
    {
        public int Start { get; set; }
        public int ProcessedDeviceCount { get; set; }
        public int ChangedDeviceCount { get; set; }
    }
}