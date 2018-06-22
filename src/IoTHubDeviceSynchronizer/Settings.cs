using System;
using System.Collections.Generic;
using System.Text;

namespace IoTHubDeviceSynchronizer
{
    internal class Settings
    {
        static readonly Settings singleton = new Settings();
        internal static Settings Instance { get { return singleton; } }

        /// <summary>
        /// Indicates the device twin check interval in seconds
        /// This check happens to verify if the all properties of the external system have been placed in the device twin
        /// Default: 30 seconds
        /// </summary>
        public int TwinCheckIntervalInSeconds { get; set; }

        /// <summary>
        /// Indicates the device twin check maximum interval in seconds
        /// This check happens to verify if the all properties of the external system have been placed in the device twin
        /// Default: 5 minutes
        /// </summary>
        public int TwinCheckMaxIntervalInSeconds { get; set; }

        /// <summary>
        /// Indicates the device twin check maximum retry counts
        /// This check happens to verify if the all properties of the external system have been placed in the device twin
        /// Default: 100
        /// </summary>
        public int TwinCheckMaxRetryCount { get; set; }

        /// <summary>
        /// Indicates the device twin check retry timeout, in other words, how long it will keep trying at most
        /// This check happens to verify if the all properties of the external system have been placed in the device twin
        /// Default: 2 days
        /// </summary>
        public int TwinCheckRetryTimeoutInMinutes { get; set; }

        /// <summary>
        /// Indicates if we should use iot hub jobs to export devices
        /// </summary>
        public bool UseJobToExportIoTHubDevices { get; set; }

        /// <summary>
        /// The threshold in which device synchronization will use iot hub jobs
        /// </summary>
        public int DevicesChangeJobThreshold { get; set; }

        /// <summary>
        /// Connection String to IoT Hub
        /// </summary>
        public string IoTHubConnectionString { get; set; }

        /// <summary>
        /// Storage account connection string (for IoT Hub jobs)
        /// </summary>
        public string StorageAccountConnectionString { get; set; }


        /// <summary>
        /// External system
        /// Example: actility
        /// </summary>
        public string ExternalSystemName { get; set; }


        /// <summary>
        /// Enables/disables IoT Hub façade for create/delete operations
        /// Default: true (enabled)
        /// </summary>
        public bool IoTHubFacadeEnabled { get; internal set; }

        /// <summary>
        /// Enables/disables soft deletes for IoT Hub Façade delete operation. 
        /// Soft delete will disable the device instead of deleting it
        /// Default: false (disabled)
        /// </summary>
        public bool IotHubFacadeUseSoftDelete { get; internal set; }

        /// <summary>
        /// Internal time (in seconds) between each IoT Hub Export jobs completion check
        /// Default: 60 seconds
        /// </summary>
        public double RetryIntervalForIoTHubExportJobInSeconds { get; internal set; }

        /// <summary>
        /// Maximum amount of attempts to wait for IoT Hub Export job to complete
        /// Default: 5
        /// </summary>
        public int RetryAttemptsForIoTHubExportJob { get; internal set; }


        /// <summary>
        /// Internal time (in seconds) between each IoT Hub Import jobs completion check
        /// Default: 300 (5 minutes)
        /// </summary>
        public double RetryIntervalForIoTHubImportJobInSeconds { get; internal set; }

        /// <summary>
        /// Maximum amount of attempts to wait for IoT Hub Import job to complete
        /// Default: 5
        /// </summary>
        public int RetryAttemptsForIoTHubImportJob { get; internal set; }

        /// <summary>
        /// Enables/disables synchronization to IoT Hub.
        /// It is better to actually disable the function so that timer triggers won't execute
        /// Default: true
        /// </summary>
        public bool IoTHubSynchronizerEnabled { get; internal set; }

        protected Settings()
        {
            this.UseJobToExportIoTHubDevices = (Environment.GetEnvironmentVariable("useJobToExportIoTHubDevices")?.ToLowerInvariant() ?? "true") == "true";
            this.DevicesChangeJobThreshold = GetIntegerEnvironmentVariable("devicesChangeJobThreshold", 100, 0);
            
            this.TwinCheckIntervalInSeconds = GetIntegerEnvironmentVariable("twinCheckIntervalInSeconds", 30, 10);
            this.TwinCheckMaxIntervalInSeconds = GetIntegerEnvironmentVariable("twinCheckMaxIntervalInSeconds", 60 * 5, 60);
            this.TwinCheckMaxRetryCount = GetIntegerEnvironmentVariable("twinCheckMaxRetryCount", 100, 1);
            this.TwinCheckRetryTimeoutInMinutes = GetIntegerEnvironmentVariable("twinCheckRetryTimeoutInMinutes", 60 * 24 * 2, 1);

            this.IoTHubConnectionString = Environment.GetEnvironmentVariable("iothub");

            this.StorageAccountConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            if (string.Compare(this.StorageAccountConnectionString, "usedevelopmentstorage=true", true) == 0)
            {
                this.StorageAccountConnectionString = Environment.GetEnvironmentVariable("storage");
            }

            this.ExternalSystemName = Environment.GetEnvironmentVariable("externalSystemName");
            if (string.IsNullOrEmpty(this.ExternalSystemName))
                this.ExternalSystemName = "actility";

            this.IoTHubFacadeEnabled = (Environment.GetEnvironmentVariable("iothubfacadeEnabled")?.ToLowerInvariant() ?? "true") == "true";
            this.IotHubFacadeUseSoftDelete = (Environment.GetEnvironmentVariable("iotHubFacadeUseSoftDelete")?.ToLowerInvariant() ?? "false") == "true";


            this.RetryIntervalForIoTHubExportJobInSeconds = GetIntegerEnvironmentVariable("retryIntervalForIoTHubExportJobInSeconds", 60, 10);
            this.RetryAttemptsForIoTHubExportJob = GetIntegerEnvironmentVariable("retryAttemptsForIoTHubExportJob", 5, 1);
            this.RetryIntervalForIoTHubImportJobInSeconds = GetIntegerEnvironmentVariable("retryIntervalForIoTHubImportJobInSeconds", 60 * 5, 1);
            this.RetryAttemptsForIoTHubImportJob = GetIntegerEnvironmentVariable("retryAttemptsForIoTHubImportJob", 5, 1);

            this.IoTHubSynchronizerEnabled = (Environment.GetEnvironmentVariable("ioTHubSynchronizerEnabled")?.ToLowerInvariant() ?? "true") == "true";
        }

        private static int GetIntegerEnvironmentVariable(string variable, int defaultValue, int minValue)
        {
            if (!int.TryParse(Environment.GetEnvironmentVariable(variable), out int value))
                value = defaultValue;

            return Math.Max(minValue, value);
        }
    }
}
