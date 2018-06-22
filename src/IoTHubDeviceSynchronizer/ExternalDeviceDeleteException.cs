using System;
using System.Runtime.Serialization;

namespace IoTHubDeviceSynchronizer
{
    [Serializable]
    internal class ExternalDeviceDeleteException : Exception
    {
        public ExternalDeviceDeleteException()
        {
        }

        public ExternalDeviceDeleteException(string message) : base(message)
        {
        }

        public ExternalDeviceDeleteException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected ExternalDeviceDeleteException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}