using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Devices;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json.Linq;

namespace IoTHubDeviceSynchronizer.ToExternal
{

    public static partial class ExternalRegistrySynchronizer
    {
        /// <summary>
        /// Creates device using DX API
        /// </summary>
        /// <param name="externalDevice"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName(nameof(CreateExternalDeviceActivity))]
        public static async Task<bool> CreateExternalDeviceActivity([ActivityTrigger] CreateExternalDeviceInput externalDevice, TraceWriter log)
        {
            var externalDeviceRegistry = Utils.ResolveExternalDeviceRegistry();

            try
            {
                await externalDeviceRegistry.CreateExternalDevice(externalDevice.DeviceId, externalDevice.Properties);

                log.Info($"Device {externalDevice.DeviceId} created");

                Utils.TelemetryClient?.TrackEvent(Utils.Event_ExternalDeviceCreated, new Dictionary<string, string>(externalDevice.Properties)
                    {
                        { "deviceId", externalDevice.DeviceId },
                        { "iothubname", externalDevice.IotHubName }
                    });

                return true;

            }
            catch (Exception ex)
            {
                Utils.TelemetryClient?.TrackEvent(Utils.Event_ExternalDeviceCreationError, new Dictionary<string, string>(externalDevice.Properties)
                {
                    { "deviceId", externalDevice.DeviceId },
                    { "iothubname", externalDevice.IotHubName },
                    { "error", ex.Message },
                });

                throw;
            }           
        }



        [FunctionName(nameof(VerifyDeviceTwinActivity))]
        public static async Task<VerifyDeviceTwinResult> VerifyDeviceTwinActivity([ActivityTrigger] DeviceCreateOrchestrationInput req, TraceWriter log)
        {
            var iotHubConnectionString = Environment.GetEnvironmentVariable($"iothub_{req.IoTHubName}");
            if (string.IsNullOrEmpty(iotHubConnectionString))
                iotHubConnectionString = Settings.Instance.IoTHubConnectionString;

            RegistryManager rm = RegistryManager.CreateFromConnectionString(iotHubConnectionString);

            try
            {
                var deviceTwin = await rm.GetTwinAsync(req.DeviceId);
                if (deviceTwin == null)
                    throw new Exception($"Could not retrieve twin from device {req.DeviceId} in iothub {req.IoTHubName}");


                var externalDeviceRegistry = Utils.ResolveExternalDeviceRegistry();
                
                foreach (var prop in externalDeviceRegistry.GetRequiredTwinProperties())
                {
                    if (!deviceTwin.Tags.Contains(prop))
                    {
                        Utils.TelemetryClient?.TrackEvent(Utils.Event_DeviceTwinCheckFail, new Dictionary<string, string>()
                        {
                            { "deviceId", req.DeviceId },
                            { "iothubname", req.IoTHubName },
                            { "missingProperty", prop }
                        });

                        log.Warning($"Missing property {prop} in device {req.DeviceId}");

                        throw new Exception($"Missing property {prop} in device {req.DeviceId}");
                    }
                }

                // all properties are there, create the succeeded response
                var properties = new Dictionary<string, string>();
                foreach (var prop in externalDeviceRegistry.GetRequiredTwinProperties())
                {
                    properties[prop] = deviceTwin.Tags[prop];
                }

                externalDeviceRegistry.ValidateTwinProperties(properties);

                return new VerifyDeviceTwinResult
                {
                    Properties = properties,                 
                };
            }
            catch (Exception ex)
            {
                log.Error($"Error checking device twin properties in device {req.DeviceId}", ex);

                throw;
            }
        }


        /// <summary>
        /// Deletes a device in external sytem
        /// </summary>
        /// <param name="device"></param>
        /// <param name="iotHubName"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName(nameof(DeleteDeviceInExternalSystemActivity))]
        public static async Task DeleteDeviceInExternalSystemActivity([ActivityTrigger] DeviceDeleteOrchestrationInput input, TraceWriter log)
        {
            var externalDeviceRegistry = Utils.ResolveExternalDeviceRegistry();
            var deviceId = input.Device["deviceId"].ToString();
            try
            {
                await externalDeviceRegistry.DeleteExternalDevice(input.Device);

                log.Info($"Device '{deviceId}' from IoT Hub '{input.IoTHubName}' deleted");

                Utils.TelemetryClient?.TrackEvent(Utils.Event_ExternalDeviceDeleted, new Dictionary<string, string>()
                {
                    { "deviceId", deviceId },
                    { "iothubname", input.IoTHubName }
                });
            }            
            catch (Exception ex)
            {
                log.Error($"Failed to device {deviceId} from IoT Hub '{input.IoTHubName}': {ex.Message}");

                Utils.TelemetryClient?.TrackEvent(Utils.Event_ExternalDeviceDeletionError, new Dictionary<string, string>()
                {
                    { "deviceId", deviceId },
                    { "iothubname", input.IoTHubName },
                    { "errorMessage", ex.Message },
                });
            }
        }        
    }
}