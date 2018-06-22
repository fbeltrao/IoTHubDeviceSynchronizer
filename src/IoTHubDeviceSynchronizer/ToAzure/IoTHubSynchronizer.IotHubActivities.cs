using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Common;
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
    public static partial class IoTHubSynchronizer
    {


        [FunctionName(nameof(CreateIoTHubDeviceChangesFileActivity))]
        public static async Task<CreateIoTHubDeviceChangesFileActivityResult> CreateIoTHubDeviceChangesFileActivity([ActivityTrigger] string instanceId, TraceWriter log)
        {
            // 1. open and parse actility devices
            var externalDevices = await LoadExternalDevicesFromBlob(instanceId);

            // 2. load iothub devices
            var existingDevices = await (Settings.Instance.UseJobToExportIoTHubDevices ? LoadIoTHubDevicesFromBlob(instanceId) : LoadIoTHubDevicesFromRegistry());

            var externalDeviceRegistry = Utils.ResolveExternalDeviceRegistry();

            var devicesToModify = new List<ExportImportDevice>();

            foreach (var externalDevice in externalDevices)
            {
                var deviceId = externalDeviceRegistry.GetDeviceIdFromExternalDevice(externalDevice);
                if (!existingDevices.TryGetValue(deviceId, out var destinationDevice))
                {
                    ExportImportDevice deviceToAdd = externalDeviceRegistry.CreateNewIoTHubDevice(externalDevice);
                    devicesToModify.Add(deviceToAdd);
                }
                else
                {
                    // found it, just remove from 
                    existingDevices.Remove(deviceId);
                }
            }

            // remove the ones that remain in the collection
            foreach (var devicesToDelete in existingDevices.Values)
            {
                devicesToDelete.ImportMode = ImportMode.Delete;
                devicesToModify.Add(devicesToDelete);
            }

            // Create file with changes            
            await WriteDevicesToBlob(instanceId, devicesToModify, log);

            var result = new CreateIoTHubDeviceChangesFileActivityResult()
            {
                DeviceChangesCount = devicesToModify.Count
            };

            return result;
            
        }


        static async Task<Dictionary<string, ExportImportDevice>> LoadIoTHubDevicesFromRegistry()
        {
            var result = new Dictionary<string, ExportImportDevice>();

            var registryManager = RegistryManager.CreateFromConnectionString(Settings.Instance.IoTHubConnectionString);
            var query = registryManager.CreateQuery("SELECT * FROM DEVICES", 500);
            while (query.HasMoreResults)
            {
                var page = await query.GetNextAsTwinAsync();
                foreach (var twin in page)
                {
                    var exportedDevice = new ExportImportDevice()
                    {
                        ETag = twin.ETag,
                        Id = twin.DeviceId,
                        Status = twin.Status ?? DeviceStatus.Disabled,
                        StatusReason = twin.StatusReason,
                        Tags = twin.Tags,                        
                    };

                    exportedDevice.Properties = new ExportImportDevice.PropertyContainer()
                    {
                        DesiredProperties = new Microsoft.Azure.Devices.Shared.TwinCollection(twin.Properties.Desired.ToJson()),
                        ReportedProperties = new Microsoft.Azure.Devices.Shared.TwinCollection(twin.Properties.Reported.ToJson()),
                    };

                    result.Add(twin.DeviceId, exportedDevice);
                }
            }

            return result;
            
        }

        /// <summary>
        /// Cleans up files stored in storage account for iot hub jobs
        /// </summary>
        /// <param name="instanceId"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName(nameof(CleanupStorageActivity))]
        public static async Task CleanupStorageActivity([ActivityTrigger] string instanceId, TraceWriter log)
        {
            try
            {
                //Parse the connection string and return a reference to the storage account.
                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(Settings.Instance.StorageAccountConnectionString);

                //Create the blob client object.
                CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

                //Get a reference to a container to use for the sample code, and create it if it does not exist.
                CloudBlobContainer container = blobClient.GetContainerReference(instanceId);
                await container.DeleteIfExistsAsync();
            }            
            catch (Exception ex)
            {
                log.Error("Could not delete storage container", ex);
                throw;
            }
        }

        [FunctionName(nameof(CreateIoTHubImportJobActivity))]
        public static async Task<string> CreateIoTHubImportJobActivity([ActivityTrigger]string instanceId, TraceWriter log)
        {
            try
            {
                //Parse the connection string and return a reference to the storage account.
                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(Settings.Instance.StorageAccountConnectionString);

                //Create the blob client object.
                CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

                //Get a reference to a container to use for the sample code, and create it if it does not exist.
                CloudBlobContainer container = blobClient.GetContainerReference(instanceId);
                await container.CreateIfNotExistsAsync();

                //Set the expiry time and permissions for the container.
                //In this case no start time is specified, so the shared access signature becomes valid immediately.
                SharedAccessBlobPolicy sasConstraints = new SharedAccessBlobPolicy();
                sasConstraints.SharedAccessExpiryTime = DateTimeOffset.UtcNow.AddHours(1);
                sasConstraints.Permissions = SharedAccessBlobPermissions.Write | SharedAccessBlobPermissions.Read | SharedAccessBlobPermissions.Delete;

                //Generate the shared access signature on the container, setting the constraints directly on the signature.
                string sasContainerToken = container.GetSharedAccessSignature(sasConstraints);

                //Return the URI string for the container, including the SAS token.
                var containerSasUri = container.Uri + sasContainerToken;



                var registryManager = RegistryManager.CreateFromConnectionString(Settings.Instance.IoTHubConnectionString);

                // Call an export job on the IoT Hub to retrieve all devices
                //var containerSasUri = string.Empty;
                var exportJob = await registryManager.ImportDevicesAsync(containerSasUri, containerSasUri, Utils.DeviceToImportBlobName);
                return exportJob.JobId;
            }
            catch (JobQuotaExceededException jobQuotaExceededException)
            {
                log.Error("Exceeded job quota", jobQuotaExceededException);
                throw;
            }
            catch (Exception ex)
            {
                log.Error("Could not import IoT Hub Devices", ex);
                throw;
            }
        }

        static async Task WriteDevicesToBlob(string instanceId, IList<ExportImportDevice> devices, TraceWriter log)
        {
            try
            {
                CloudStorageAccount storage = CloudStorageAccount.Parse(Settings.Instance.StorageAccountConnectionString);
                CloudBlobClient client = storage.CreateCloudBlobClient();


                CloudBlobContainer container = client.GetContainerReference(instanceId);
                await container.CreateIfNotExistsAsync();
                var blob = container.GetBlockBlobReference(Utils.DeviceToImportBlobName);                
                var buffer = new StringBuilder();

                using (var stream = await blob.OpenWriteAsync())
                {
                    for (var i = 0; i < devices.Count;)
                    {

                        // write in blocks of 100
                        for (var y = 0; y < 100 && i < devices.Count; i++, y++)
                        {
                            buffer.AppendLine(JsonConvert.SerializeObject(devices[i]));
                        }

                        var bytes = Encoding.UTF8.GetBytes(buffer.ToString());
                        await stream.WriteAsync(bytes, 0, bytes.Length);

                        buffer.Length = 0;
                    }

                    if (buffer.Length > 0)
                    {
                        var bytes = Encoding.UTF8.GetBytes(buffer.ToString());
                        await stream.WriteAsync(bytes, 0, bytes.Length);
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error("Failed writing import file to blog", ex);
                throw;
            }
        }

        static async Task<Dictionary<string, ExportImportDevice>> LoadIoTHubDevicesFromBlob(string instanceId)
        {
            CloudStorageAccount storage = CloudStorageAccount.Parse(Settings.Instance.StorageAccountConnectionString);
            CloudBlobClient client = storage.CreateCloudBlobClient();            
            CloudBlobContainer container = client.GetContainerReference(instanceId);
            var blob = container.GetBlobReference("devices.txt");

            var exportedDevices = new Dictionary<string, ExportImportDevice>();
            using (var streamReader = new StreamReader(await blob.OpenReadAsync()))
            {
                while (streamReader.Peek() != -1)
                {
                    string line = await streamReader.ReadLineAsync();
                    if (line.Length > 0 && line[0] == '{')
                    {
                        var device = JsonConvert.DeserializeObject<ExportImportDevice>(line);
                        exportedDevices.Add(device.Id, device);
                    }
                }
            }

            return exportedDevices;
        }


        [FunctionName(nameof(CheckIoTJobDoneActivity))]
        public static async Task<JobStatus> CheckIoTJobDoneActivity([ActivityTrigger] string jobId, TraceWriter log)
        {
            var registryManager = RegistryManager.CreateFromConnectionString(Settings.Instance.IoTHubConnectionString);
            var status = (await registryManager.GetJobAsync(jobId))?.Status ?? JobStatus.Unknown;
            switch (status)
            {
                case JobStatus.Enqueued:
                case JobStatus.Queued:
                case JobStatus.Running:
                case JobStatus.Scheduled:
                    Utils.TelemetryClient?.TrackEvent(Utils.Event_IoTHubJobCheckFailed, new Dictionary<string, string>()
                    {                        
                        { "jobId", jobId },
                        { "jobStatus", status.ToString() }
                    });
                    throw new Exception($"Job {jobId} not yet ready, status = {status}");
            }

            return status;
            
        }

        
        
        [FunctionName(nameof(CreateExportIoTHubDeviceJobActivity))]
        public static async Task<string> CreateExportIoTHubDeviceJobActivity([ActivityTrigger] string instanceId, TraceWriter log)
        {
            try
            {
                //Parse the connection string and return a reference to the storage account.
                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(Settings.Instance.StorageAccountConnectionString);

                //Create the blob client object.
                CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

                //Get a reference to a container to use for the sample code, and create it if it does not exist.
                CloudBlobContainer container = blobClient.GetContainerReference(instanceId);
                await container.CreateIfNotExistsAsync();

                //Set the expiry time and permissions for the container.
                //In this case no start time is specified, so the shared access signature becomes valid immediately.
                SharedAccessBlobPolicy sasConstraints = new SharedAccessBlobPolicy();
                sasConstraints.SharedAccessExpiryTime = DateTimeOffset.UtcNow.AddHours(24);
                sasConstraints.Permissions = SharedAccessBlobPermissions.Write | SharedAccessBlobPermissions.Read | SharedAccessBlobPermissions.Delete;

                //Generate the shared access signature on the container, setting the constraints directly on the signature.
                string sasContainerToken = container.GetSharedAccessSignature(sasConstraints);

                //Return the URI string for the container, including the SAS token.
                var containerSasUri = container.Uri + sasContainerToken;

                var registryManager = RegistryManager.CreateFromConnectionString(Settings.Instance.IoTHubConnectionString);                    

                // Call an export job on the IoT Hub to retrieve all devices
                //var containerSasUri = string.Empty;
                var exportJob = await registryManager.ExportDevicesAsync(containerSasUri, true);

                log.Info($"Started export device job {exportJob.JobId} for orchestration {instanceId}");

                return exportJob.JobId;
            }
            catch (JobQuotaExceededException jobQuotaExceededException)
            {
                

                log.Error("Exceeded job quota", jobQuotaExceededException);
                throw;
            }
            catch (Exception ex)
            {
                log.Error("Could not export IoT Hub Devices", ex);
                throw;
            }
        }

    }
}