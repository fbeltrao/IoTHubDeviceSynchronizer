using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IoTHubDeviceSynchronizer.ExternalSystems.Actility
{
    /// <summary>
    /// Actility based external registry implementation
    /// </summary>
    public class ActilityExternalDeviceRegistryService : IExternalDeviceRegistryService
    {
        internal string ApiTokenUri { get; private set; }
        internal string ApiClientId { get; private set; }
        internal string ApiClientSecret { get; private set; }
        internal string ApiDevicesUri { get; private set; }

        /// <inheritdoc />
        public string DeviceIdPropertyNameInExternalDevice => "Name";

        public ActilityExternalDeviceRegistryService()
        {
            this.ApiTokenUri = Environment.GetEnvironmentVariable("actility_api_token_uri");
            this.ApiClientId = Environment.GetEnvironmentVariable("actility_api_client_id");
            this.ApiClientSecret = Environment.GetEnvironmentVariable("actility_api_client_secret");
            this.ApiDevicesUri = Environment.GetEnvironmentVariable("actility_api_devices_uri");

        }

        /// <summary>
        /// Ensures that a valid REST api token is available
        /// </summary>
        /// <returns></returns>
        internal async Task<string> EnsureValidToken()
        {
            var token = await OAuthTokenManager.Instance.GetToken(
             this.ApiTokenUri,
             "client_credentials",
             this.ApiClientId,
             this.ApiClientSecret,
             TimeSpan.FromDays(6));

            if (string.IsNullOrEmpty(token))
            {
                throw new Exception("Could not obtain API token");
            }

            return token;
        }

        /// <inheritdoc />
        public async Task CreateExternalDevice(string deviceId, IDictionary<string, string> deviceProperties)
        {
            var token = await EnsureValidToken();

            var createDevicePayload = new Dictionary<string, string>
            {
                ["name"] = deviceId
            };

            foreach (var kv in deviceProperties)
            {
                createDevicePayload[kv.Key] = kv.Value;
            }

            var createDeviceRes = await Utils.HttpClient.ApiPost(token, this.ApiDevicesUri, createDevicePayload);
            if (!createDeviceRes.IsSuccessStatusCode)
            {
                var errorMessage = await createDeviceRes.Content.ReadAsStringAsync();

                throw new Exception(errorMessage);
            }                
        }

        /// <inheritdoc />
        public async Task DeleteExternalDevice(JToken device)
        {
            var token = await EnsureValidToken();

            var deviceEUI = device["twin"]["tags"]["EUI"].ToString();

            UriBuilder builder = new UriBuilder(this.ApiDevicesUri)
            {
                Query = $"deviceEUI={deviceEUI}"
            };

            var getDeviceRes = await Utils.HttpClient.ApiSend(HttpMethod.Get, token, builder.Uri.ToString());
            if (getDeviceRes.IsSuccessStatusCode)
            {
                var deviceDetails = JsonConvert.DeserializeObject<JArray>(await getDeviceRes.Content.ReadAsStringAsync());
                if (deviceDetails.Count == 1)
                {
                    var deviceRef = deviceDetails[0]["ref"].ToString();
                    var deleteDeviceRes = await Utils.HttpClient.ApiSend(HttpMethod.Delete, token, $"{this.ApiDevicesUri}/{deviceRef}");

                    if (!deleteDeviceRes.IsSuccessStatusCode)
                    {
                        var errorMessage = await deleteDeviceRes.Content.ReadAsStringAsync();

                        throw new ExternalDeviceDeleteException(errorMessage);
                    }

                }
                else
                {
                    throw new Exception($"Get device by EUI returned {deviceDetails.Count} devices, expected 1 device");
                }
            }
            else
            {
                var errorMessage = await getDeviceRes.Content.ReadAsStringAsync();

                throw new Exception($"Failed to retrieve device {deviceEUI}, code {getDeviceRes.StatusCode}: {errorMessage}");
            }
        }

        /// <inheritdoc />
        public IEnumerable<string> GetRequiredTwinProperties() => new string[]
        {
            "EUI",
            "activationType",
            "deviceProfileId",
            "applicationEUI",
            "applicationKey"
        };

        /// <inheritdoc />
        public async Task<GetExternalDeviceResult> GetExternalDevices(int pageIndex)
        {
            var token = await EnsureValidToken();

            var uri = string.Concat(this.ApiDevicesUri, "?pageIndex=", (pageIndex + 1).ToString());
            var listDeviceRes = await Utils.HttpClient.ApiSend(HttpMethod.Get, token, uri);
            var bodyRes = await listDeviceRes.Content.ReadAsStringAsync();

            // Will return 404 if the page is empty
            var isEmptyResult = listDeviceRes.StatusCode == System.Net.HttpStatusCode.NotFound;
            if (isEmptyResult)
            {
                return new GetExternalDeviceResult(new JArray(), hasMore: false);
            }

            // Error, stop
            if (!listDeviceRes.IsSuccessStatusCode)
            {
                throw new Exception(bodyRes);
            }

            return new GetExternalDeviceResult(JsonConvert.DeserializeObject<JArray>(bodyRes), hasMore: true);
        }

        /// <inheritdoc />
        public string GetDeviceIdFromExternalDevice(JToken externalDevice) => externalDevice["name"].ToString();

        /// <inheritdoc />
        public ExportImportDevice CreateNewIoTHubDevice(JToken externalDevice)
        {
            var newDevice = new ExportImportDevice()
            {
                Id = externalDevice["name"].ToString(),
                ImportMode = ImportMode.Create,
                Status = DeviceStatus.Enabled,
                Authentication = new AuthenticationMechanism()
                {
                    SymmetricKey = new SymmetricKey()
                    {
                        PrimaryKey = CryptoKeyGenerator.GenerateKey(32),
                        SecondaryKey = CryptoKeyGenerator.GenerateKey(32)
                    }
                },
                Tags = new Microsoft.Azure.Devices.Shared.TwinCollection(),
            };

            foreach (var prop in GetRequiredTwinProperties())
            {
                var sourcePropertyValue = externalDevice[prop];
                if (sourcePropertyValue != null)
                {
                    newDevice.Tags[prop] = sourcePropertyValue.ToString();
                }
            }
           
            newDevice.Tags["ref"] = externalDevice["ref"].ToString();

            return newDevice;
        }

        /// <inheritdoc />
        public void ValidateTwinProperties(IReadOnlyDictionary<string, string> properties)
        {
            // 1. Ensure that the EUI property has 16 characters
            if (!properties.TryGetValue("EUI", out var eui))
                throw new Exception("Property EUI not found");

            if (string.IsNullOrEmpty(eui) || eui.Length != 16)
            {
                throw new Exception($"Property EUI should have 16 characters. Value: {eui}. Length: {(eui?.Length ?? 0)}");
            }
        }
    }
}
