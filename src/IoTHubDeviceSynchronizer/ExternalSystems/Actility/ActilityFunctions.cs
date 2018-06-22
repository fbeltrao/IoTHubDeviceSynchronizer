
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net;

namespace IoTHubDeviceSynchronizer.ExternalSystems.Actility
{
#if DEBUG
    /// <summary>
    /// Actility helper functions (only available for development)
    /// </summary>
    public static class ActilityFunctions
    {
        [FunctionName("ActilityGetDevices")]
        public static async Task<IActionResult> ActilityGetDevices([HttpTrigger(AuthorizationLevel.Function, "get", Route = "actility/devices")]HttpRequest req, TraceWriter log)
        {
            var externalDeviceRegistry = (ActilityExternalDeviceRegistryService)Utils.ResolveExternalDeviceRegistry();

            var token = await externalDeviceRegistry.EnsureValidToken();

            var listDeviceRes = await Utils.HttpClient.ApiSend(HttpMethod.Get, token, externalDeviceRegistry.ApiDevicesUri);
            var rawResponse = await listDeviceRes.Content.ReadAsStringAsync();
            return new ContentResult()
            {
                Content = rawResponse,
                ContentType = "application/json",
                StatusCode = (int)HttpStatusCode.OK
            };
        } 
    }
#endif
}
