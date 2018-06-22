namespace IoTHubDeviceSynchronizer.ToAzure
{

    public class RetrieveDevicesFromExternalSystemResult
    {
        public bool HasMore { get; set; }
        public int PageIndex { get; set; }
        public bool HasError { get; set; }
        public int DeviceCount { get; set; }
    }
}