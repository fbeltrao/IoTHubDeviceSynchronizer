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
        [FunctionName(nameof(ExternalRegistrySynchronizer_Orchestration))]
        public static async Task ExternalRegistrySynchronizer_Orchestration(
            [OrchestrationTrigger] DurableOrchestrationContext context, TraceWriter log)
        {
            var input = context.GetInput<IoTHubEventGridListenerInput>();

            if (!context.IsReplaying)
                log.Info($"{nameof(ExternalRegistrySynchronizer_Orchestration)} started for {input.DeviceId} / {input.IotHubName}. {nameof(Settings.Instance.TwinCheckIntervalInSeconds)}: {Settings.Instance.TwinCheckIntervalInSeconds} secs, {nameof(Settings.Instance.TwinCheckMaxRetryCount)}: {Settings.Instance.TwinCheckMaxRetryCount}, {nameof(Settings.Instance.TwinCheckMaxIntervalInSeconds)}: {Settings.Instance.TwinCheckMaxIntervalInSeconds} secs, {nameof(Settings.Instance.TwinCheckRetryTimeoutInMinutes)}: {Settings.Instance.TwinCheckRetryTimeoutInMinutes} minutes");            

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
                IotHubName = input.IotHubName,
                Properties = deviceTwinCheckResult.Properties,
            };

            await context.CallActivityAsync(nameof(CreateExternalDeviceActivity), deviceInfo);            
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
                                    // Create the device if all properties are there
                                    await CreateExternalDeviceActivity(new CreateExternalDeviceInput()
                                    {
                                        DeviceId = deviceId,
                                        IotHubName = iotHubName,
                                        Properties = twinProperties
                                    },
                                    log);
                                }
                                else
                                {
                                    // Otherwise start orchestration to wait until the device twin properties exist
                                    var deviceForwarderRequest = new IoTHubEventGridListenerInput(deviceId, iotHubName);
                                    var instanceId = await starter.StartNewAsync(nameof(ExternalRegistrySynchronizer_Orchestration), deviceForwarderRequest);
                                    orchestrationInstances.Add(instanceId);
                                }
                                break;
                            }

                        case "devicedeleted":
                            {                                
                                await DeleteDeviceInExternalSystem(data, iotHubName, log);
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