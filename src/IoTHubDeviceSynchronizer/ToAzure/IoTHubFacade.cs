
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using Microsoft.Azure.Devices.Common.Exceptions;

namespace IoTHubDeviceSynchronizer.ToAzure
{
    /// <summary>
    /// Façade CRUD operations for IoT Hub
    /// </summary>
    public static class IoTHubFacade
    {
        /// <summary>
        /// Creates a device
        /// </summary>
        /// <param name="req"></param>
        /// <param name="iothubname"></param>
        /// <param name="deviceId"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName(nameof(CreateDevice))]
        public static async Task<IActionResult> CreateDevice([HttpTrigger(AuthorizationLevel.Function, "post", Route = "devices/{iothubname?}/{deviceId}")] HttpRequest req, 
            string iothubname,
            string deviceId,
            TraceWriter log)
        {
            if (!Settings.Instance.IoTHubFacadeEnabled)
                return new BadRequestObjectResult("IoT Hub Façade is disabled");

            if (string.IsNullOrEmpty(deviceId))
                return new BadRequestObjectResult("Missing deviceId value");

            var iotHubConnectionString = Settings.Instance.IoTHubConnectionString;
            if (!string.IsNullOrEmpty(iothubname))
            {
                var specificIotHubConnectionString = Environment.GetEnvironmentVariable($"iothub_{iothubname}");
                if (!string.IsNullOrEmpty(specificIotHubConnectionString))
                    iotHubConnectionString = specificIotHubConnectionString;
            }

            var registryManager = RegistryManager.CreateFromConnectionString(iotHubConnectionString);

            var newDevice = new Device(deviceId)
            {
                Status = DeviceStatus.Enabled
            };

            string requestBody = new StreamReader(req.Body).ReadToEnd();
            if (!string.IsNullOrEmpty(requestBody))
            {
                var deviceProperties = JsonConvert.DeserializeObject<Dictionary<string, string>>(requestBody);

                var twin = new Twin
                {
                    Tags = new TwinCollection(requestBody)
                };

                await registryManager.AddDeviceWithTwinAsync(newDevice, twin);
            }
            else
            {
                await registryManager.AddDeviceAsync(newDevice);
            }

            Utils.TelemetryClient?.TrackEvent(Utils.Event_IoTHubDeviceCreated, new Dictionary<string, string>()
                    {
                        { "deviceId", deviceId },
                        { "iothubname", iothubname ?? string.Empty }
                    });

            return new StatusCodeResult((int)StatusCodes.Status201Created);           
        }


        /// <summary>
        /// Deletes a device
        /// </summary>
        /// <param name="req"></param>
        /// <param name="iothubname"></param>
        /// <param name="deviceId"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName(nameof(DeleteDevice))]
        public static async Task<IActionResult> DeleteDevice([HttpTrigger(AuthorizationLevel.Function, "delete", Route = "devices/{iothubname?}/{deviceId}")] HttpRequest req,
            string iothubname,
            string deviceId,
            TraceWriter log)
        {
            if (!Settings.Instance.IoTHubFacadeEnabled)
                return new BadRequestObjectResult("IoT Hub Façade is disabled");

            if (string.IsNullOrEmpty(deviceId))
                return new BadRequestObjectResult("Missing deviceId value");

            var iotHubConnectionString = Settings.Instance.IoTHubConnectionString;
            if (!string.IsNullOrEmpty(iothubname))
            {
                var specificIotHubConnectionString = Environment.GetEnvironmentVariable($"iothub_{iothubname}");
                if (!string.IsNullOrEmpty(specificIotHubConnectionString))
                    iotHubConnectionString = specificIotHubConnectionString;
            }

            var registryManager = RegistryManager.CreateFromConnectionString(iotHubConnectionString);

            if (Settings.Instance.IotHubFacadeUseSoftDelete)
            {
                var device = await registryManager.GetDeviceAsync(deviceId);
                if (device == null)
                    return new NotFoundResult();

                device.Status = DeviceStatus.Disabled;
                device.StatusReason = "Deleted by external system";

                await registryManager.UpdateDeviceAsync(device);
            }
            else
            {
                try
                {
                    await registryManager.RemoveDeviceAsync(deviceId);                    
                }
                catch (DeviceNotFoundException)
                {
                    return new NotFoundResult();
                }

            }

            Utils.TelemetryClient?.TrackEvent(Utils.Event_IoTHubDeviceDeleted, new Dictionary<string, string>()
            {
                { "deviceId", deviceId },
                { "iothubname", iothubname ?? string.Empty },
                { "softDelete", Settings.Instance.IotHubFacadeUseSoftDelete.ToString().ToLowerInvariant() }
            });

            return new OkResult();
        }
    }
}
