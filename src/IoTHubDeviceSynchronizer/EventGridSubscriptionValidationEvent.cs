using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace IoTHubDeviceSynchronizer
{
    public class EventGridSubscriptionValidationEvent
    {
        public EventGridSubscriptionValidationEvent()
        {
        }

        [JsonProperty(PropertyName = "validationCode")]
        public string ValidationCode { get; set; }
    }
}
