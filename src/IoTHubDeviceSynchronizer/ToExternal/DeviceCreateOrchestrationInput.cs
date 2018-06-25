namespace IoTHubDeviceSynchronizer.ToExternal
{
    /// <summary>
    /// Input to device create orchestration
    /// </summary>
    public class DeviceCreateOrchestrationInput
    {
        /// <summary>
        /// Constructor
        /// </summary>
        public DeviceCreateOrchestrationInput()
        {
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="deviceId"></param>
        /// <param name="iotHubName"></param>
        public DeviceCreateOrchestrationInput(string deviceId, string iotHubName)
        {
            DeviceId = deviceId;
            IoTHubName = iotHubName;
        }

        /// <summary>
        /// Source IoT Hub
        /// </summary>
        public string IoTHubName { get; set; }

        /// <summary>
        /// Device identifier
        /// </summary>
        public string DeviceId { get; set; }        
    }
}