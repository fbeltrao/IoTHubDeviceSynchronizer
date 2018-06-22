namespace IoTHubDeviceSynchronizer.ToAzure
{
    public class UpdateDevicesActivityRequest
    {
        public int Start { get; set; }
        public int DeviceCount { get; set; }
        public string BlobContainerName { get; set; }
    }
}