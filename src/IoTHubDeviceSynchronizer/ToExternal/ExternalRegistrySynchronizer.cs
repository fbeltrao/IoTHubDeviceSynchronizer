using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IoTHubDeviceSynchronizer.ToExternal
{

    /// <summary>
    /// Orchestrator that synchronizes devices from IoT Hub to external device registry
    /// </summary>
    public static partial class ExternalRegistrySynchronizer
    {
        /// <summary>
        /// Orchestration to delete device in external system
        /// </summary>
        /// <param name="context"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName(nameof(ExternalRegistrySynchronizer_DeleteDeviceOrchestration))]
        public static async Task ExternalRegistrySynchronizer_DeleteDeviceOrchestration(
          [OrchestrationTrigger] DurableOrchestrationContext context, TraceWriter log)
        {
            var input = context.GetInput<DeviceDeleteOrchestrationInput>();
            var deviceId = input.Device["deviceId"].ToString();
            if (!context.IsReplaying)
                log.Info($"{nameof(ExternalRegistrySynchronizer_DeleteDeviceOrchestration)} started for {deviceId} / {input.IoTHubName}. {nameof(Settings.Instance.TwinCheckIntervalInSeconds)}: {Settings.Instance.TwinCheckIntervalInSeconds} secs, {nameof(Settings.Instance.TwinCheckMaxRetryCount)}: {Settings.Instance.TwinCheckMaxRetryCount}, {nameof(Settings.Instance.TwinCheckMaxIntervalInSeconds)}: {Settings.Instance.TwinCheckMaxIntervalInSeconds} secs, {nameof(Settings.Instance.TwinCheckRetryTimeoutInMinutes)}: {Settings.Instance.TwinCheckRetryTimeoutInMinutes} minutes");

            var deleteExternalDeviceSucceeded = await context.CallActivityWithRetryAsync<bool>(nameof(DeleteDeviceInExternalSystemActivity),
                new RetryOptions(TimeSpan.FromSeconds(Settings.Instance.ExternalSystemCallRetryIntervalInSeconds), Settings.Instance.ExternalSystemCallMaxRetryCount)
                {
                    BackoffCoefficient = 2,                     // backoff coefficient ^2
                    MaxRetryInterval = TimeSpan.FromSeconds(Settings.Instance.ExternalSystemCallMaxIntervalInSeconds),   // will wait for a new retry up to x seconds
                    RetryTimeout = TimeSpan.FromMinutes(Settings.Instance.ExternalSystemCallRetryTimeoutInMinutes)      // will try up to x minutes
                },
                input);


            log.Info($"{nameof(ExternalRegistrySynchronizer_DeleteDeviceOrchestration)} finished for {deviceId} / {input.IoTHubName}. Succeeded: {deleteExternalDeviceSucceeded.ToString()}");
        }

        /// <summary>
        /// Orchestration to create devices in external system
        /// </summary>
        /// <param name="context"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName(nameof(ExternalRegistrySynchronizer_CreateDeviceOrchestration))]        
        public static async Task ExternalRegistrySynchronizer_CreateDeviceOrchestration(
            [OrchestrationTrigger] DurableOrchestrationContext context, TraceWriter log)
        {
            var input = context.GetInput<DeviceCreateOrchestrationInput>();

            if (!context.IsReplaying)
                log.Info($"{nameof(ExternalRegistrySynchronizer_CreateDeviceOrchestration)} started for {input.DeviceId} / {input.IoTHubName}. {nameof(Settings.Instance.TwinCheckIntervalInSeconds)}: {Settings.Instance.TwinCheckIntervalInSeconds} secs, {nameof(Settings.Instance.TwinCheckMaxRetryCount)}: {Settings.Instance.TwinCheckMaxRetryCount}, {nameof(Settings.Instance.TwinCheckMaxIntervalInSeconds)}: {Settings.Instance.TwinCheckMaxIntervalInSeconds} secs, {nameof(Settings.Instance.TwinCheckRetryTimeoutInMinutes)}: {Settings.Instance.TwinCheckRetryTimeoutInMinutes} minutes");            

            // 1. If it is the start wait a bit until the device twin was updated            
            var deviceTwinCheckResult = await context.CallActivityWithRetryAsync<VerifyDeviceTwinResult>(nameof(VerifyDeviceTwinActivity),
                new RetryOptions(TimeSpan.FromSeconds(Settings.Instance.TwinCheckIntervalInSeconds), Settings.Instance.TwinCheckMaxRetryCount)
                {
                    BackoffCoefficient = 2,                     // backoff coefficient ^2
                    MaxRetryInterval = TimeSpan.FromSeconds(Settings.Instance.TwinCheckMaxIntervalInSeconds),   // will wait for a new retry up to x seconds
                    RetryTimeout =  TimeSpan.FromMinutes(Settings.Instance.TwinCheckRetryTimeoutInMinutes)      // will try up to x minutes
                },
                input);

            // Will only reach here once it succeededs (required properties were found)
            var deviceInfo = new CreateExternalDeviceInput
            {
                DeviceId = input.DeviceId,
                IotHubName = input.IoTHubName,
                Properties = deviceTwinCheckResult.Properties,
            };

            var createExternalDeviceSucceeded = await context.CallActivityWithRetryAsync<bool>(nameof(CreateExternalDeviceActivity),
                new RetryOptions(TimeSpan.FromSeconds(Settings.Instance.ExternalSystemCallRetryIntervalInSeconds), Settings.Instance.ExternalSystemCallMaxRetryCount)
                {
                    BackoffCoefficient = 2,                     // backoff coefficient ^2
                    MaxRetryInterval = TimeSpan.FromSeconds(Settings.Instance.ExternalSystemCallMaxIntervalInSeconds),   // will wait for a new retry up to x seconds
                    RetryTimeout = TimeSpan.FromMinutes(Settings.Instance.ExternalSystemCallRetryTimeoutInMinutes)      // will try up to x minutes
                },
                input);


            log.Info($"{nameof(ExternalRegistrySynchronizer_CreateDeviceOrchestration)} finished for {input.DeviceId} / {input.IoTHubName}. Succeeded: {createExternalDeviceSucceeded.ToString()}");
        }


        /// <summary>
        /// Orchestration trigger
        /// Started by an event grid with deleted/created devices
        /// Update external device registry
        /// </summary>
        /// <param name="req"></param>
        /// <param name="starter"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName(nameof(ExternalRegistrySynchronizer_EventGridListener))]
        public static async Task<HttpResponseMessage> ExternalRegistrySynchronizer_EventGridListener(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestMessage req,            
            [OrchestrationClient] DurableOrchestrationClient starter,
            TraceWriter log)
        {
            var eventGridResponse = await Utils.HandleEventGridRequest(req);
            if (eventGridResponse != null)
            {
                return eventGridResponse;
            }

            var orchestrationInstances = new List<string>();
            var payload = await req.Content.ReadAsStringAsync();
            var events = JsonConvert.DeserializeObject<List<JObject>>(payload);
            if (events != null)
            {
                var externalDeviceRegistry = Utils.ResolveExternalDeviceRegistry();

                foreach (var gridEvent in events)
                {
                    var data = gridEvent["data"];
                    var deviceId = data["deviceId"].ToString();
                    var iotHubName = data["hubName"].ToString();
                    var operationType = data["opType"].ToString();

                    switch (operationType.ToLowerInvariant())
                    {
                        case "devicecreated":
                            {
                                var twinProperties = new Dictionary<string, string>();
                                if (data["twin"]?["tags"] != null)
                                {
                                    var deviceTwinTags = data["twin"]["tags"];
                                    foreach (var requiredProperty in externalDeviceRegistry.GetRequiredTwinProperties())
                                    {
                                        if (deviceTwinTags[requiredProperty] != null)
                                        {
                                            twinProperties.Add(requiredProperty, deviceTwinTags[requiredProperty].ToString());
                                        }
                                        else
                                        {
                                            twinProperties.Clear();
                                            break;
                                        }
                                    }
                                }

                                // create device if all properties are there
                                if (twinProperties.Count > 0)
                                {
                                    try
                                    {
                                        externalDeviceRegistry.ValidateTwinProperties(twinProperties);

                                        // Create the device if all properties are there
                                        await CreateExternalDeviceActivity(new CreateExternalDeviceInput()
                                        {
                                            DeviceId = deviceId,
                                            IotHubName = iotHubName,
                                            Properties = twinProperties
                                        },
                                        log);
                                    }
                                    catch (Exception ex)
                                    {
                                        log.Warning($"Direct device creation for device {deviceId} in iothub {iotHubName} failed, starting an orchestration. {ex.ToString()}");

                                        // failed to create the device immediately, starts an orchestration to retry more times
                                        var deviceForwarderRequest = new DeviceCreateOrchestrationInput(deviceId, iotHubName);
                                        var instanceId = await starter.StartNewAsync(nameof(ExternalRegistrySynchronizer_CreateDeviceOrchestration), deviceForwarderRequest);
                                        orchestrationInstances.Add(instanceId);
                                    }
                                }
                                else
                                {
                                    // Otherwise start orchestration to wait until the device twin properties exist
                                    var deviceForwarderRequest = new DeviceCreateOrchestrationInput(deviceId, iotHubName);
                                    var instanceId = await starter.StartNewAsync(nameof(ExternalRegistrySynchronizer_CreateDeviceOrchestration), deviceForwarderRequest);
                                    orchestrationInstances.Add(instanceId);
                                }
                                break;
                            }

                        case "devicedeleted":
                            {
                                var deleteDeviceInput = new DeviceDeleteOrchestrationInput(data, iotHubName);

                                try
                                {
                                    await DeleteDeviceInExternalSystemActivity(deleteDeviceInput, log);
                                }
                                catch (Exception ex)
                                {
                                    log.Warning($"Direct device delete for device {deviceId} in iothub {iotHubName} failed, starting an orchestration. {ex.ToString()}");

                                    // failed to delete the device, starts an orchestration to retry more times
                                    var instanceId = await starter.StartNewAsync(nameof(ExternalRegistrySynchronizer_DeleteDeviceOrchestration), deleteDeviceInput);
                                    orchestrationInstances.Add(instanceId);
                                }
                                break;
                            }
                    }
                }
            }

            if (orchestrationInstances.Count == 1)
                return starter.CreateCheckStatusResponse(req, orchestrationInstances.First());

            var res = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            return res;
        }
    }
}