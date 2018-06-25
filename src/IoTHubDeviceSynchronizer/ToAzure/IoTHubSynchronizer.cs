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
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;

namespace IoTHubDeviceSynchronizer.ToAzure
{
    /// <summary>
    /// Orchestration to synchronize devices from external registry to IoT Hub
    /// </summary>
    public static partial class IoTHubSynchronizer
    {
        [FunctionName(nameof(IoTHubSynchronizer_Orchestration))]
        public static async Task<string> IoTHubSynchronizer_Orchestration(
            [OrchestrationTrigger] DurableOrchestrationContext context,
            TraceWriter log)
        {
            // 1. If we export using IoT Hub jobs, start it
            Task<string> exportWithJobTask = null;
            if (Settings.Instance.UseJobToExportIoTHubDevices)
            {
                // wait until the iot job is done
                exportWithJobTask = context.CallActivityAsync<string>(nameof(CreateExportIoTHubDeviceJobActivity), context.InstanceId);
            }

            // 2. Get Devices from partner API
            var pageIndex = 0;
            var partnerDevicesCount = 0;
            while (true)
            {
                var res = await context.CallActivityAsync<RetrieveDevicesFromExternalSystemResult>(nameof(RetrieveExternalDeviceActivity), new RetrieveExternalDevicesInput(context.InstanceId, pageIndex));

                if (res.HasError)
                {
                    // Could not load items from rest api, for now abort
                    log.Error("Stopping because rest api to Actility failed");
                    return string.Empty;
                }

                partnerDevicesCount += res.DeviceCount;

                if (!res.HasMore)
                    break;

                pageIndex = res.PageIndex + 1;
            }

            // 3. Get devices from IoT Hub (only if using job)
            if (Settings.Instance.UseJobToExportIoTHubDevices)
            {
                // wait until the iot job is done
                var iotHubJobId = await exportWithJobTask;
                if (string.IsNullOrEmpty(iotHubJobId))
                {
                    throw new Exception("Failed to create iot hub export job");
                }

                var iotHubJobStatus = JobStatus.Unknown;
                try
                {
                    iotHubJobStatus = await context.CallActivityWithRetryAsync<JobStatus>(nameof(CheckIoTJobDoneActivity),
                        new RetryOptions(TimeSpan.FromSeconds(Settings.Instance.RetryIntervalForIoTHubExportJobInSeconds), Settings.Instance.RetryAttemptsForIoTHubExportJob),
                        iotHubJobId);
                }
                catch (FunctionFailedException)
                {
                    iotHubJobStatus = JobStatus.Unknown;

                    Utils.TelemetryClient?.TrackEvent(Utils.Event_IoTHubJobFailed, new Dictionary<string, string>()
                    {
                        { "iotHubJobId", iotHubJobId }
                    });
                }

                if (iotHubJobStatus == JobStatus.Cancelled || iotHubJobStatus == JobStatus.Failed || iotHubJobStatus == JobStatus.Unknown)
                    throw new Exception($"IoT Hub Device export failed: status = {iotHubJobStatus}");
            }
            

            // 3. Save changes to IoT Hub
            var devicesToModify = await context.CallActivityAsync<CreateIoTHubDeviceChangesFileActivityResult>(nameof(CreateIoTHubDeviceChangesFileActivity), context.InstanceId);
            string importJobId = string.Empty;
            if (devicesToModify.DeviceChangesCount > 0)
            {
                if (Settings.Instance.DevicesChangeJobThreshold == 0 || Settings.Instance.DevicesChangeJobThreshold <= devicesToModify.DeviceChangesCount)
                {
                    importJobId = await context.CallActivityAsync<string>(nameof(CreateIoTHubImportJobActivity), context.InstanceId);


                    // give it 30 seconds to finish, otherwise will wait 5x with an interval of 5 minutes between tries.
                    await context.CreateTimer(context.CurrentUtcDateTime.AddSeconds(30), CancellationToken.None);

                    var importDevicesJobStatus = JobStatus.Running;

                    try
                    {
                        await context.CallActivityWithRetryAsync<JobStatus>(nameof(CheckIoTJobDoneActivity),
                            new RetryOptions(TimeSpan.FromSeconds(Settings.Instance.RetryIntervalForIoTHubImportJobInSeconds), Settings.Instance.RetryAttemptsForIoTHubImportJob),
                            importJobId);
                    }
                    catch (FunctionFailedException)
                    {
                        importDevicesJobStatus = JobStatus.Running;

                        Utils.TelemetryClient?.TrackEvent(Utils.Event_IoTHubJobFailed, new Dictionary<string, string>()
                        {
                            { "iotHubJobId", importJobId }
                        });

                    }

                    if (importDevicesJobStatus != JobStatus.Completed)
                    {
                        throw new Exception($"IoT Hub import job did not complete: jobId {importJobId}, status = {importDevicesJobStatus.ToString()}");
                    }
                }
                else
                {
                    var updatedDevicesCount = await context.CallSubOrchestratorAsync<int>(nameof(ManualDeviceImportOrchestration.ManualDeviceImportOrchestration_Runner), context.InstanceId);
                }
            }


            await context.CallActivityAsync(nameof(CleanupStorageActivity), context.InstanceId);

            return $"Finished, partner devices {partnerDevicesCount}, update job id {importJobId}, with {devicesToModify.DeviceChangesCount} device changes";
        }



        [FunctionName(nameof(IoTHubSynchronizer_HttpListener))]
        public static async Task<HttpResponseMessage> IoTHubSynchronizer_HttpListener(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestMessage req,
            [OrchestrationClient] DurableOrchestrationClient starter,
            TraceWriter log)
        {
            if (!Settings.Instance.IoTHubSynchronizerEnabled)            
                return new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest);

            // Function input comes from the request content.
            string instanceId = await starter.StartNewAsync(nameof(IoTHubSynchronizer_Orchestration), null);

            log.Info($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }

        [FunctionName(nameof(IoTHubSynchronizer_TimerTrigger))]
        public static async Task IoTHubSynchronizer_TimerTrigger(
            [TimerTrigger("0 0 */1 * * *")] TimerInfo timerInfo, // every hour
            [OrchestrationClient] DurableOrchestrationClient starter,
            TraceWriter log)
        {
            if (!Settings.Instance.IoTHubSynchronizerEnabled)
                return ;

            string instanceId = await starter.StartNewAsync(nameof(IoTHubSynchronizer_Orchestration), null);        
            log.Info($"Started orchestration with ID = '{instanceId}'.");
        }
    }
}