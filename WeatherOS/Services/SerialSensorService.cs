using System;

namespace WeatherOS.Services
{
    public class SerialSensorService
    {
        public bool IsInitialized { get; private set; }

        public bool Initialize()
        {
            // Gen3 placeholder
            IsInitialized = false;
            return false;
        }

        public bool TryReadTelemetryPacket(out string packet)
        {
            packet = string.Empty;
            return false;
        }
    }
}
