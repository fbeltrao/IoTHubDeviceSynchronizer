using Newtonsoft.Json.Linq;

namespace IoTHubDeviceSynchronizer.ToExternal
{
    /// <summary>
    /// Input to device device orchestration
    /// </summary>
    public class DeviceDeleteOrchestrationInput
    {
        /// <summary>
        /// Constructor
        /// </summary>
        public DeviceDeleteOrchestrationInput()
        {
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public DeviceDeleteOrchestrationInput(JToken device, string iotHubName)
        {
            Device = device;
            this.IoTHubName = iotHubName;
        }


        /// <summary>
        /// Device definition
        /// </summary>
        public JToken Device { get; set; }


        /// <summary>
        /// Source IoT Hub
        /// </summary>

        public string IoTHubName { get; set; }
    }
}