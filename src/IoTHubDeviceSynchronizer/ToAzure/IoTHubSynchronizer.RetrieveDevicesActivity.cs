using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IoTHubDeviceSynchronizer.ToAzure
{
    public static partial class IoTHubSynchronizer
    {
        

        [FunctionName(nameof(RetrieveExternalDeviceActivity))]
        public static async Task<RetrieveDevicesFromExternalSystemResult> RetrieveExternalDeviceActivity([ActivityTrigger] RetrieveExternalDevicesInput req,
            CancellationToken cancellationToken,
            TraceWriter log)
        {
            // we continue trying until the 3 minute mark
            var startTimer = Stopwatch.StartNew();


            var externalDeviceRegistry = Utils.ResolveExternalDeviceRegistry();

            var pageIndex = req.PageIndex;
            var deviceCount = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var getDevicesResult = await externalDeviceRegistry.GetExternalDevices(pageIndex);

                    // No devices, return that we are finished
                    if (getDevicesResult.Devices?.Count == 0)
                    {
                        return new RetrieveDevicesFromExternalSystemResult
                        {
                            HasMore = false,
                            PageIndex = pageIndex,
                            DeviceCount = deviceCount
                        };
                    }


                    log.Info($"Retrieving page {pageIndex} returned {getDevicesResult.Devices?.Count ?? 0} devices");
                    if (getDevicesResult.Devices?.Count == 0)
                    {
                        return new RetrieveDevicesFromExternalSystemResult
                        {
                            HasMore = false,
                            PageIndex = pageIndex,
                            DeviceCount = deviceCount
                        };
                    }
                    else
                    {
                        await SaveExternalDevicesToBlob(getDevicesResult.Devices, req.InstanceId, log);

                        deviceCount += getDevicesResult.Devices?.Count ?? 0;

                        // Stop if:
                        // - we are running for more than 3 minutes (5 minutes limit in consumption)
                        // - external registry service indicates we have no more devices
                        if (!getDevicesResult.HasMore || (startTimer.Elapsed > TimeSpan.FromMinutes(3)))
                        {
                            return new RetrieveDevicesFromExternalSystemResult
                            {
                                HasMore = getDevicesResult.HasMore,
                                PageIndex = pageIndex,
                                DeviceCount = deviceCount
                            };
                        }

                        pageIndex++;
                    }
                }
                catch (Exception ex)
                {
                    log.Error(ex.Message);

                    return new RetrieveDevicesFromExternalSystemResult
                    {
                        PageIndex = pageIndex,
                        HasError = true
                    };
                }
            }

            // cancellation token was triggered
            return new RetrieveDevicesFromExternalSystemResult
            {
                HasError = true,
                PageIndex = pageIndex,
            };
        }

        private static async Task SaveExternalDevicesToBlob(JArray devices, string instanceId, TraceWriter log)
        {
            CloudStorageAccount storage = CloudStorageAccount.Parse(Settings.Instance.StorageAccountConnectionString);
            CloudBlobClient client = storage.CreateCloudBlobClient();
                
            CloudBlobContainer container = client.GetContainerReference(instanceId);
            await container.CreateIfNotExistsAsync();

            var blob = container.GetAppendBlobReference(Utils.ExternalDeviceBlobName);            
            var jsonPayload = JsonConvert.SerializeObject(devices);
            var itemsPayload = jsonPayload.Substring(1, jsonPayload.Length - 2);

            // file exists, append a comma
            if (await blob.ExistsAsync())
            {
                itemsPayload = string.Concat(",", itemsPayload);
            }
            else
            {
                await blob.CreateOrReplaceAsync();
            }

            await blob.AppendTextAsync(itemsPayload);

            log.Info($"Appended {devices.Count} devices to {blob.Name}");

        }

        private static async Task<JArray> LoadExternalDevicesFromBlob(string instanceId)
        {
            CloudStorageAccount storage = CloudStorageAccount.Parse(Settings.Instance.StorageAccountConnectionString);
            CloudBlobClient client = storage.CreateCloudBlobClient();


            CloudBlobContainer container = client.GetContainerReference(instanceId);
            var blob = container.GetBlobReference(Utils.ExternalDeviceBlobName);

            string jsonArrayPayload = "";
            using (var streamReader = new StreamReader(await blob.OpenReadAsync()))
            {
                var items = await streamReader.ReadToEndAsync();
                jsonArrayPayload = string.Concat("[", items, "]");
            }

            return JsonConvert.DeserializeObject<JArray>(jsonArrayPayload);
        }


    }
}