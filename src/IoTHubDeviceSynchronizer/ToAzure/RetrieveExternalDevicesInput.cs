namespace IoTHubDeviceSynchronizer.ToAzure
{
    /// <summary>
    /// Input for the retrieve external devices activity
    /// </summary>
    public class RetrieveExternalDevicesInput
    {
        public RetrieveExternalDevicesInput()
        {

        }

        public RetrieveExternalDevicesInput(string instanceId, int pageIndex)
        {
            InstanceId = instanceId;
            PageIndex = pageIndex;
        }

        public int PageIndex { get; set; }
        public string InstanceId { get; set; }
    }
}