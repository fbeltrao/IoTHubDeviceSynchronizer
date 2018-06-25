using Microsoft.ApplicationInsights;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace IoTHubDeviceSynchronizer
{
    /// <summary>
    /// Helper class
    /// </summary>
    static class Utils
    {
        internal static HttpClient HttpClient = new HttpClient();
        internal static TelemetryClient TelemetryClient;

        /// <summary>Device is created in external system</summary>
        internal const string Event_ExternalDeviceCreated = "ExternalDeviceCreated";
        /// <summary>Transient error happened creating device in external system</summary>
        internal const string Event_ExternalDeviceCreationError = "ExternalDeviceCreationError";
        /// <summary>Failed to create the device in external system. A manual process is required</summary>
        internal const string Event_ExternalDeviceCreationFailed = "ExternalDeviceCreationFailed";
        /// <summary>Device is deleted in external system</summary>
        internal const string Event_ExternalDeviceDeleted = "ExternalDeviceDeleted";
        /// <summary>Transient error happened deleting device in external system</summary>
        internal const string Event_ExternalDeviceDeletionError = "ExternalDeviceDeletedError";
        /// <summary>Failed to delete the device in external system. A manual process is required</summary>
        internal const string Event_ExternalDeviceDeleteFailed = "ExternalDeviceDeleteFailed";
        /// <summary>Device twin check fails, indicating that the device does not have all expected properties by the external system. This is a transient error</summary>
        internal const string Event_DeviceTwinCheckFail = "DeviceTwinCheckFail";
        /// <summary>Device twin check failed. Device will not be created in external system requiring a manual process to solve the problem</summary>
        internal const string Event_DeviceTwinCheckFailed = "DeviceTwinCheckFailed";
        /// <summary>IoT Hub job is not yet complete, this is a transient error</summary>
        internal const string Event_IoTHubJobCheckNotComplete = "IoTHubJobCheckNotComplete";
        /// <summary>IoT Hub job failed to complete in a timely manner</summary>
        internal const string Event_IoTHubJobFailed = "IoTHubJobFailed";
        /// <summary>Device is created in IoT Hub</summary>
        internal const string Event_IoTHubDeviceCreated = "IoTHubDeviceCreated";
        /// <summary>Device is deleted in IoT Hub</summary>
        internal const string Event_IoTHubDeviceDeleted = "IoTHubDeviceDeleted";

        static Utils()
        {
            var key = System.Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY");
            if (!string.IsNullOrEmpty(key))
                TelemetryClient = new TelemetryClient() { InstrumentationKey = key };
        }

        internal const string DeviceToImportBlobName = "devices-to-import.txt";
        internal const string ExternalDeviceBlobName = "externalDevices.json";

        internal static async Task<HttpResponseMessage> ApiSend(this HttpClient httpClient, HttpMethod method, string bearerToken, string uri)
        {
            var request = new HttpRequestMessage(method, uri);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);

            return await Utils.HttpClient.SendAsync(request);
        }

        internal static async Task<HttpResponseMessage> ApiPost(this HttpClient httpClient, string bearerToken, string uri, object payload)
        {
            var requestContent = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = requestContent
            };

            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);

            return await Utils.HttpClient.SendAsync(request);
        }

        /// <summary>
        /// Handle event grid web hook requests (subscribe/unsubscribe)
        /// </summary>
        /// <param name="req"></param>
        /// <returns></returns>
        internal static async Task<HttpResponseMessage> HandleEventGridRequest(HttpRequestMessage req)
        {
            IEnumerable<string> eventTypeHeaders = null;
            string eventTypeHeader = null;
            if (req.Headers.TryGetValues("aeg-event-type", out eventTypeHeaders))
            {
                eventTypeHeader = eventTypeHeaders.First();
            }

            if (String.Equals(eventTypeHeader, "SubscriptionValidation", StringComparison.OrdinalIgnoreCase))
            {
                string jsonArray = await req.Content.ReadAsStringAsync();
                EventGridSubscriptionValidationEvent validationEvent = null;
                List<JObject> events = JsonConvert.DeserializeObject<List<JObject>>(jsonArray);

                validationEvent = ((JObject)events[0]["data"]).ToObject<EventGridSubscriptionValidationEvent>();
                EventGridSubscriptionValidationResponse validationResponse = new EventGridSubscriptionValidationResponse { ValidationResponse = validationEvent.ValidationCode };
                var returnMessage = new HttpResponseMessage(HttpStatusCode.OK);
                returnMessage.Content = new StringContent(JsonConvert.SerializeObject(validationResponse));

                return returnMessage;
            }
            else if (String.Equals(eventTypeHeader, "Unsubscribe", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            return null;
        }


        /// <summary>
        /// Split the <paramref name="collection"/> into smaller collections according to the size defined in <paramref name="batchSize"/>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="collection"></param>
        /// <param name="batchSize"></param>
        /// <returns></returns>
        public static IEnumerable<IEnumerable<T>> Batch<T>(this IEnumerable<T> collection, int batchSize)
        {
            var nextbatch = new List<T>(batchSize);
            foreach (T item in collection)
            {
                nextbatch.Add(item);
                if (nextbatch.Count == batchSize)
                {
                    yield return nextbatch;
                    nextbatch.Clear();
                }
            }
            if (nextbatch.Count > 0)
                yield return nextbatch;
        }

        static Lazy<IExternalDeviceRegistryService> externalDeviceRegistry = new Lazy<IExternalDeviceRegistryService>(() =>
        {
            switch (Settings.Instance.ExternalSystemName.ToLowerInvariant())
            {
                case "actility":
                    return new ExternalSystems.Actility.ActilityExternalDeviceRegistryService();

                default:
                    throw new Exception($"Unknown external system '{Settings.Instance.ExternalSystemName}'");
            }

        });
        public static IExternalDeviceRegistryService ResolveExternalDeviceRegistry()
        {
            return externalDeviceRegistry.Value;
        }
    }
}
