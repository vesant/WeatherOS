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

        public string FetchAutoLocation()
        {
            try
            {
                System.Console.WriteLine("[net] raw_tcp: Auto-locating via IP (ip-api.com)...");
                var destIp = new Cosmos.System.Network.IPv4.Address(208, 95, 112, 1); // ip-api.com IP (approximate/dns mock)
                var gatewayIp = new Cosmos.System.Network.IPv4.Address(10, 0, 2, 2);
                
                string request = "GET /json/ HTTP/1.1\r\nHost: ip-api.com\r\nConnection: close\r\n\r\n";
                string response = RawTcpHttp.FetchGet(destIp, gatewayIp, request);
                
                if (string.IsNullOrWhiteSpace(response))
                {
                    System.Threading.Thread.Sleep(1000);
                    response = RawTcpHttp.FetchGet(destIp, gatewayIp, request);
                }

                if (!string.IsNullOrWhiteSpace(response))
                {
                    string city = ParseJsonString(response, "city");
                    if (!string.IsNullOrWhiteSpace(city)) return city;
                }
            }
            catch { }
            return "";
        }

        private bool _debugMode = false;

        public void SetDebugMode(bool debug)
        {
            _debugMode = debug;
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
                
                string safeCity = city.Replace(" ", "%20");
                
                string request = $"GET /data/2.5/weather?q={safeCity}&appid={apiKey}&units=metric HTTP/1.1\r\n" +
                                 "Host: api.openweathermap.org\r\n" +
                                 "Connection: close\r\n\r\n";
                                 
                if (_debugMode) System.Console.WriteLine($"[net] raw_tcp: sending SYN to {destIp.ToString()} via gateway {gatewayIp.ToString()}");
                
                string response = RawTcpHttp.FetchGet(destIp, gatewayIp, request);

                // Retry logic for VirtualBox ARP drops
                if (string.IsNullOrWhiteSpace(response))
                {
                    if (_debugMode) System.Console.WriteLine("[net] raw_tcp: First attempt timed out (ARP missing?). Retrying in 1s...");
                    System.Threading.Thread.Sleep(1000);
                    try {
                        using (var arpWake = new Cosmos.System.Network.IPv4.ICMPClient())
                        {
                            arpWake.Connect(gatewayIp);
                            arpWake.SendEcho();
                        }
                    } catch { }
                    response = RawTcpHttp.FetchGet(destIp, gatewayIp, request);
                }
                
                if (!string.IsNullOrWhiteSpace(response))
                {
                    if (response.Contains("404 Not Found"))
                    {
                        _cachedReading.Location = "NOT FOUND";
                        _cachedReading.Condition = "City Invalid";
                        _cachedReading.TemperatureCelsius = 0;
                        _cachedReading.HumidityPercent = 0;
                        if (_debugMode) System.Console.WriteLine("[net] raw_tcp: error 404 - city not found on openweathermap.");
                    }
                    else if (CustomIndexOf(response, "\"temp\":", 0) != -1)
                    {
                        ParseOpenWeatherJson(response, city);
                        if (_debugMode) System.Console.WriteLine("[net] raw_tcp: payload received successfully. (status: 200 OK)");
                    }
                    else
                    {
                        if (_debugMode) System.Console.WriteLine("[net] raw_tcp: HTTP error or malformed response.");
                        if (_debugMode) System.Console.WriteLine("[net] raw_tcp: --- RESPONSE DUMP START ---");
                        if (_debugMode)
                        {
                            if (response.Length > 200) System.Console.WriteLine(response.Substring(0, 200) + "...");
                            else System.Console.WriteLine(response);
                        }
                        if (_debugMode) System.Console.WriteLine("[net] raw_tcp: --- RESPONSE DUMP END ---");
                    }
                }
                else
                {
                    if (_debugMode) System.Console.WriteLine("[net] raw_tcp: error: connection timed out during handshake.");
                }
            }
            catch (Exception ex)
            {
                if (_debugMode) System.Console.WriteLine($"[net] raw_tcp: critical socket failure: {ex.Message}");
            }
        }

        private void ParseOpenWeatherJson(string json, string fallbackCity)
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
                
                string cityName = ParseJsonString(json, "name");
                string countryCode = ParseJsonString(json, "country");
                
                if (!string.IsNullOrWhiteSpace(cityName))
                {
                    if (!string.IsNullOrWhiteSpace(countryCode))
                        _cachedReading.Location = (cityName + ", " + countryCode).ToUpper();
                    else
                        _cachedReading.Location = cityName.ToUpper();
                }
                else if (!string.IsNullOrWhiteSpace(fallbackCity))
                {
                    _cachedReading.Location = fallbackCity.ToUpper();
                }
                
                _cachedReading.Timestamp = DateTime.UtcNow;
            }
            catch { }
        }

        private int CustomIndexOf(string source, string search, int startIndex = 0)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(search)) return -1;
            if (startIndex < 0 || startIndex >= source.Length) return -1;
            int searchLen = search.Length;
            int maxIdx = source.Length - searchLen;
            for (int i = startIndex; i <= maxIdx; i++)
            {
                bool match = true;
                for (int j = 0; j < searchLen; j++)
                {
                    if (source[i + j] != search[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }

        private float ParseJsonFloat(string json, string key)
        {
            string search = "\"" + key + "\":";
            int idx = CustomIndexOf(json, search, 0);
            if (idx == -1) return 0f;
            
            idx += search.Length;
            
            int endIdx = idx;
            while (endIdx < json.Length)
            {
                char c = json[endIdx];
                if (c == ',' || c == '}') break;
                endIdx++;
            }
            if (endIdx >= json.Length) return 0f;
            
            string valStr = json.Substring(idx, endIdx - idx).Trim();
            
            float result = 0f;
            float fraction = 0f;
            float divisor = 10f;
            bool isNegative = false;
            bool inFraction = false;
            
            for (int i = 0; i < valStr.Length; i++)
            {
                char c = valStr[i];
                if (c == '-') isNegative = true;
                else if (c == '.') inFraction = true;
                else if (c >= '0' && c <= '9')
                {
                    int digit = c - '0';
                    if (!inFraction) result = (result * 10f) + digit;
                    else
                    {
                        fraction += digit / divisor;
                        divisor *= 10f;
                    }
                }
            }
            return isNegative ? -result : result;
        }

        private string ParseJsonString(string json, string key)
        {
            string search = "\"" + key + "\":";
            int idx = CustomIndexOf(json, search, 0);
            if (idx == -1) return "";
            
            idx += search.Length;
            while (idx < json.Length && (json[idx] == ' ' || json[idx] == '\"')) idx++;
            
            int endIdx = idx;
            while (endIdx < json.Length && json[endIdx] != '\"' && json[endIdx] != ',' && json[endIdx] != '}') endIdx++;
            if (endIdx >= json.Length) return "";
            
            return json.Substring(idx, endIdx - idx).Trim();
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
