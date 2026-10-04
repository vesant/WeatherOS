using System;

namespace WeatherOS.Services
{
    /// <summary>
    /// Represents structured meteorological observation data.
    /// Uses value-type semantics or reusable structures to prevent Garbage Collector overhead in Cosmos.
    /// </summary>
    public struct WeatherData
    {
        public float TemperatureCelsius;
        public float HumidityPercent;
        public float PressureHpa;
        public float WindSpeedKmh;
        public string WindDirection;
        public string Condition;
        public DateTime Timestamp;

        public WeatherData(float temp, float humidity, float pressure, float windSpeed, string windDir, string condition)
        {
            TemperatureCelsius = temp;
            HumidityPercent = humidity;
            PressureHpa = pressure;
            WindSpeedKmh = windSpeed;
            WindDirection = windDir;
            Condition = condition;
            Timestamp = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Service responsible for managing meteorological telemetry.
    /// Acts as a mock provider currently, with architecture ready for TCP socket ingestion from Arduino or remote stations.
    /// </summary>
    public class WeatherService
    {
        // Pre-allocated weather reading instance to avoid repeated GC heap allocations
        private WeatherData _cachedReading;
        private int _simulationCycle = 0;

        public WeatherService()
        {
            // Initial baseline weather conditions (e.g., Lisbon or standard meteorology station)
            _cachedReading = new WeatherData(
                temp: 21.5f,
                humidity: 58.0f,
                pressure: 1014.2f,
                windSpeed: 12.8f,
                windDir: "NNW",
                condition: "Clear Sky / Sunny"
            );
        }

        /// <summary>
        /// Retrieves the latest meteorological reading.
        /// Simulates slight environmental fluctuations without allocating new objects on the heap.
        /// </summary>
        public WeatherData GetLatestReading()
        {
            _simulationCycle = (_simulationCycle + 1) % 10;

            // Micro-variations around baseline to simulate active telemetry without floating-point heavy operations
            _cachedReading.TemperatureCelsius = 21.0f + (_simulationCycle * 0.2f);
            _cachedReading.HumidityPercent = 58.0f + ((_simulationCycle % 5) * 0.5f);
            _cachedReading.PressureHpa = 1013.8f + ((_simulationCycle % 3) * 0.3f);
            _cachedReading.WindSpeedKmh = 12.0f + (_simulationCycle * 0.4f);

            return _cachedReading;
        }

        /// <summary>
        /// Placeholder for future network-based sensor reading over raw TCP sockets.
        /// NOTE: Cosmos does not reliably support HttpClient or complex TLS stacks.
        /// When streaming data over Wi-Fi/Ethernet from the Arduino Uno Q, use raw TCP sockets or UDP datagrams.
        /// </summary>
        public bool TryPollRemoteTcpTelemetry(string hostIp, int port)
        {
            /*
             * Architectural Roadmap for Cosmos TCP Client:
             * 
             * try
             * {
             *     using (var client = new Cosmos.System.Network.IPv4.TCP.TcpClient(port))
             *     {
             *         var serverIp = Cosmos.System.Network.IPv4.Address.Parse(hostIp);
             *         client.Connect(serverIp, port);
             *         
             *         // Send telemetry query command
             *         byte[] query = System.Text.Encoding.ASCII.GetBytes("GET_WEATHER\n");
             *         client.Send(query);
             *         
             *         // Receive telemetry packet
             *         byte[] buffer = new byte[256];
             *         int bytesRead = client.Receive(ref buffer);
             *         // Parse CSV/JSON buffer into _cachedReading
             *         return true;
             *     }
             * }
             * catch
             * {
             *     return false;
             * }
             */
            return false;
        }
    }
}
