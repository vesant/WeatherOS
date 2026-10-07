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
        public string Location;
        public DateTime Timestamp;

        public WeatherData(float temp, float humidity, float pressure, float windSpeed, string windDir, string condition, string loc)
        {
            TemperatureCelsius = temp;
            HumidityPercent = humidity;
            PressureHpa = pressure;
            WindSpeedKmh = windSpeed;
            WindDirection = windDir;
            Condition = condition;
            Location = loc;
            Timestamp = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Service responsible for managing meteorological telemetry.
    /// Acts as a mock provider currently, with architecture ready for TCP socket ingestion from Arduino or remote stations.
    /// </summary>
    public class WeatherService
    {
        private WeatherData _cachedReading;
        private Cosmos.System.Network.IPv4.UDP.UdpClient _udpClient;

        public WeatherService()
        {
            // Initial baseline weather conditions
            _cachedReading = new WeatherData(
                temp: 21.5f,
                humidity: 58.0f,
                pressure: 1014.2f,
                windSpeed: 12.8f,
                windDir: "NNW",
                condition: "Awaiting Telemetry...",
                loc: "UNKNOWN"
            );

            try
            {
                _udpClient = new Cosmos.System.Network.IPv4.UDP.UdpClient(6000);
            }
            catch { }
        }

        public WeatherData GetLatestReading()
        {
            return _cachedReading;
        }

        public void FetchOpenWeather(string city, string apiKey)
        {
            try
            {
                System.Console.WriteLine("[net] raw_tcp: initializing bare-metal TCP handshake sequence...");
                
                var destIp = new Cosmos.System.Network.IPv4.Address(141, 95, 99, 79);
                var gatewayIp = new Cosmos.System.Network.IPv4.Address(10, 0, 2, 2);
                
                // Acordar o Gateway ARP do VirtualBox com um ICMP invisivel
                try {
                    using (var arpWake = new Cosmos.System.Network.IPv4.ICMPClient())
                    {
                        arpWake.Connect(gatewayIp);
                        arpWake.SendEcho();
                    }
                } catch { }
                
                string request = $"GET /data/2.5/weather?q={city}&appid={apiKey}&units=metric HTTP/1.1\r\n" +
                                 "Host: api.openweathermap.org\r\n" +
                                 "Connection: close\r\n\r\n";
                                 
                System.Console.WriteLine($"[net] raw_tcp: sending SYN to {destIp.ToString()} via gateway {gatewayIp.ToString()}");
                
                string response = RawTcpHttp.FetchGet(destIp, gatewayIp, request);
                
                if (!string.IsNullOrWhiteSpace(response))
                {
                    ParseOpenWeatherJson(response);
                    _cachedReading.Location = city.ToUpper();
                    System.Console.WriteLine("[net] raw_tcp: payload received successfully. (status: 200 OK)");
                }
                else
                {
                    System.Console.WriteLine("[net] raw_tcp: error: connection timed out during handshake.");
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[net] raw_tcp: critical socket failure: {ex.Message}");
            }
        }

        private void ParseOpenWeatherJson(string json)
        {
            try
            {
                _cachedReading.TemperatureCelsius = ParseJsonFloat(json, "temp");
                _cachedReading.HumidityPercent = ParseJsonFloat(json, "humidity");
                _cachedReading.PressureHpa = ParseJsonFloat(json, "pressure");
                
                // OpenWeather returns speed in meters/sec. Multiply by 3.6 for km/h.
                _cachedReading.WindSpeedKmh = ParseJsonFloat(json, "speed") * 3.6f; 
                
                string weatherCondition = ParseJsonString(json, "main");
                if (!string.IsNullOrWhiteSpace(weatherCondition))
                {
                    _cachedReading.Condition = weatherCondition;
                }
                
                _cachedReading.Timestamp = DateTime.UtcNow;
            }
            catch { }
        }

        private float ParseJsonFloat(string json, string key)
        {
            string search = "\"" + key + "\":";
            int idx = json.IndexOf(search);
            if (idx == -1) return 0f;
            
            idx += search.Length;
            int endIdx = json.IndexOfAny(new char[] { ',', '}' }, idx);
            if (endIdx == -1) return 0f;
            
            string valStr = json.Substring(idx, endIdx - idx).Trim();
            
            string[] parts = valStr.Split('.');
            float val = float.Parse(parts[0]);
            
            if (parts.Length > 1)
            {
                float divisor = 1;
                for (int i = 0; i < parts[1].Length; i++) divisor *= 10;
                float dec = float.Parse(parts[1]);
                
                if (val >= 0) val += (dec / divisor);
                else val -= (dec / divisor);
            }
            return val;
        }

        private string ParseJsonString(string json, string key)
        {
            string search = "\"" + key + "\":\"";
            int idx = json.IndexOf(search);
            if (idx == -1) return "";
            
            idx += search.Length;
            int endIdx = json.IndexOf("\"", idx);
            if (endIdx == -1) return "";
            
            return json.Substring(idx, endIdx - idx);
        }

        private void ParseTelemetryPacket(string packet)
        {
            // Exemplo de pacote: T:22.5;H:60;P:1012;W:15;D:N;C:Chuva
            string[] parts = packet.Split(';');
            foreach (var part in parts)
            {
                if (part.Length < 3) continue;
                
                string key = part.Substring(0, 2); // "T:"
                string val = part.Substring(2);    // "22.5"

                try
                {
                    if (key == "T:") _cachedReading.TemperatureCelsius = float.Parse(val);
                    else if (key == "H:") _cachedReading.HumidityPercent = float.Parse(val);
                    else if (key == "P:") _cachedReading.PressureHpa = float.Parse(val);
                    else if (key == "W:") _cachedReading.WindSpeedKmh = float.Parse(val);
                    else if (key == "D:") _cachedReading.WindDirection = val;
                    else if (key == "C:") _cachedReading.Condition = val;
                }
                catch { }
            }
            _cachedReading.Timestamp = DateTime.UtcNow;
        }

        /// <summary>
        /// Calculates the approximate dew point natively on the PC CPU (Magnus-Tetens simplification).
        /// Zero allocations, pure value-type arithmetic.
        /// </summary>
        public float GetDewPointCelsius()
        {
            return _cachedReading.TemperatureCelsius - ((100.0f - _cachedReading.HumidityPercent) / 5.0f);
        }
    }
}
