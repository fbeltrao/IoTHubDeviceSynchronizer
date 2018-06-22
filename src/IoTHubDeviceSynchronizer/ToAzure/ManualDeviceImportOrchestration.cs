using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Common.Exceptions;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;

namespace IoTHubDeviceSynchronizer.ToAzure
{
    /// <summary>
    /// Durable function to update IoT Hub devices using registry manager
    /// IoT Hub jobs are not working really well
    /// </summary>
    public static partial class ManualDeviceImportOrchestration
    {

        [FunctionName(nameof(ManualDeviceImportOrchestration_Runner))]
        public static async Task<int> ManualDeviceImportOrchestration_Runner([OrchestrationTrigger] DurableOrchestrationContext context)
        {
            var blobContainerName = context.GetInput<string>();
            var start = 0;
            const int DEVICE_UPDATE_BATCH_SIZE = 100;
            var changedDeviceCount = 0;

            while (true)
            {
                var request = new UpdateDevicesActivityRequest
                {
                    DeviceCount = DEVICE_UPDATE_BATCH_SIZE,
                    Start = start,
                    BlobContainerName = blobContainerName
                };

                var result = await context.CallActivityAsync<UpdateDevicesActivityResult>(nameof(UpdateDevicesActivity), request);
                changedDeviceCount += result.ChangedDeviceCount;

                if (result.ProcessedDeviceCount < request.DeviceCount)
                    break;

                start = result.Start + result.ProcessedDeviceCount;
            }

            return changedDeviceCount;
        }


        [FunctionName(nameof(UpdateDevicesActivity))]
        public static async Task<UpdateDevicesActivityResult> UpdateDevicesActivity([ActivityTrigger]UpdateDevicesActivityRequest request, TraceWriter log)
        {
            var result = new UpdateDevicesActivityResult
            {
                ProcessedDeviceCount = 0,
                ChangedDeviceCount = 0,
                Start = request.Start
            };

            CloudStorageAccount storage = CloudStorageAccount.Parse(Settings.Instance.StorageAccountConnectionString);
            CloudBlobClient client = storage.CreateCloudBlobClient();


            CloudBlobContainer container = client.GetContainerReference(request.BlobContainerName);
            var blob = container.GetBlobReference(Utils.DeviceToImportBlobName);
            using (var streamReader = new StreamReader(await blob.OpenReadAsync()))
            {
                // skip "start" lines
                for (var line=0; line < request.Start; ++line)
                {
                    await streamReader.ReadLineAsync();
                }

                // process items
                var registryManager = RegistryManager.CreateFromConnectionString(Settings.Instance.IoTHubConnectionString);
                for (var count=0; count < request.DeviceCount; ++count)
                {
                    try
                    {
                        var line = await streamReader.ReadLineAsync();
                        if (!string.IsNullOrEmpty(line))
                        {
                            var device = JsonConvert.DeserializeObject<ExportImportDevice>(line);
                            if (await HandleDevice(device, registryManager, log))
                            {
                                result.ChangedDeviceCount++;
                            }

                            result.ProcessedDeviceCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Error("error", ex);
                        throw;
                    }
                }                
            }

            return result;
        }

        private static async Task<bool> HandleDevice(ExportImportDevice device, RegistryManager registryManager, TraceWriter log)
        {
            if (device.ImportMode == ImportMode.Delete)
            {
                try
                {
                    await registryManager.RemoveDeviceAsync(device.Id);
                    log.Info($"Device {device.Id} deleted");

                    return true;
                }
                catch (Exception ex)
                {
                    log.Error($"Failed to delete device {device.Id}", ex);
                }
            }
            else
            {

                // For now we try to create the device without checking if it already exists
                // add additional logic to update an existing device
                try
                {
                    var newDevice = new Device(device.Id)
                    {
                        Status = DeviceStatus.Enabled
                    };

                    var twin = new Twin
                    {
                        Tags = device.Tags
                    };

                    if (device.Properties != null)
                    {
                        twin.Properties = new TwinProperties();
                        if (device.Properties.DesiredProperties != null)
                            twin.Properties.Desired = new TwinCollection(device.Properties.DesiredProperties.ToJson());

                        if (device.Properties.ReportedProperties != null)
                            twin.Properties.Reported = new TwinCollection(device.Properties.ReportedProperties.ToJson());
                    }

                    await registryManager.AddDeviceWithTwinAsync(newDevice, twin);
                    log.Info($"Device {device.Id} created");

                    return true;
                }
                catch (DeviceAlreadyExistsException)
                {
                    log.Info($"Device {device.Id} already exists, skipping");
                }
            }

            return true;
        }        
    }
}