using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace IoTHubDeviceSynchronizer
{
    public class EventGridSubscriptionValidationResponse
    {
        public EventGridSubscriptionValidationResponse()
        {

        }

        [JsonProperty(PropertyName = "validationResponse")]
        public string ValidationResponse { get; set; }
    }
}
