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
        public string AlertMessage;
        public string AlertLevel;
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
            AlertMessage = "";
            AlertLevel = "NONE";
            Timestamp = DateTime.UtcNow;
        }
    }

    public class WeatherService
    {
        private WeatherData _cachedReading;
        private Cosmos.System.Network.IPv4.UDP.UdpClient _udpClient;

        public WeatherService()
        {
            _cachedReading = new WeatherData(
                temp: 21.5f,
                humidity: 58.0f,
                pressure: 1014.2f,
                windSpeed: 12.8f,
                windDir: "NNW",
                condition: "Awaiting Telemetry...",
                loc: "UNKNOWN"
            );

            try { _udpClient = new Cosmos.System.Network.IPv4.UDP.UdpClient(6000); } catch { }
        }

        public WeatherData GetLatestReading() { return _cachedReading; }

        public string FetchAutoLocation()
        {
            try
            {
                System.Console.WriteLine("[net] raw_tcp: Auto-locating via IP (ip-api.com)...");
                var destIp = new Cosmos.System.Network.IPv4.Address(208, 95, 112, 1);
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
        public void SetDebugMode(bool debug) { _debugMode = debug; }

        public void FetchOpenWeather(string city, string apiKey)
        {
            try
            {
                System.Console.WriteLine("[net] raw_tcp: initializing bare-metal TCP handshake sequence...");
                
                // IP for api.weatherapi.com
                var destIp = new Cosmos.System.Network.IPv4.Address(79, 127, 134, 228); 
                var gatewayIp = new Cosmos.System.Network.IPv4.Address(10, 0, 2, 2);
                
                try {
                    using (var arpWake = new Cosmos.System.Network.IPv4.ICMPClient())
                    {
                        arpWake.Connect(gatewayIp);
                        arpWake.SendEcho();
                    }
                } catch { }
                
                string safeCity = city.Replace(" ", "%20");
                string request = $"GET /v1/forecast.json?key={apiKey}&q={safeCity}&days=1&aqi=no&alerts=yes HTTP/1.1\r\n" +
                                 "Host: api.weatherapi.com\r\nConnection: close\r\n\r\n";
                                 
                if (_debugMode) System.Console.WriteLine($"[net] raw_tcp: sending SYN to {destIp.ToString()} via gateway {gatewayIp.ToString()}");
                
                string response = RawTcpHttp.FetchGet(destIp, gatewayIp, request);

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
                    if (response.Contains("400 Bad Request") || response.Contains("error"))
                    {
                        _cachedReading.Location = "NOT FOUND";
                        _cachedReading.Condition = "City Invalid or API Error";
                        _cachedReading.TemperatureCelsius = 0;
                        _cachedReading.HumidityPercent = 0;
                        _cachedReading.AlertLevel = "NONE";
                        if (_debugMode) System.Console.WriteLine("[net] raw_tcp: HTTP Error (400 Bad Request / 401 Unauthorized / Invalid Key).");
                    }
                    else if (CustomIndexOf(response, "\"temp_c\":", 0) != -1)
                    {
                        ParseWeatherApiJson(response, city);
                        if (_debugMode) System.Console.WriteLine("[net] raw_tcp: payload received successfully. (status: 200 OK)");
                    }
                    else
                    {
                        if (_debugMode) System.Console.WriteLine("[net] raw_tcp: HTTP error or malformed response.");
                    }
                }
                else
                {
                    if (_debugMode) System.Console.WriteLine("[net] raw_tcp: error: connection completely timed out (No SYN-ACK).");
                }
            }
            catch (Exception ex)
            {
                if (_debugMode) System.Console.WriteLine($"[net] raw_tcp: critical failure: {ex.Message}");
            }
        }

        private void ParseWeatherApiJson(string json, string fallbackCity)
        {
            try
            {
                _cachedReading.TemperatureCelsius = ParseJsonFloat(json, "temp_c");
                _cachedReading.HumidityPercent = ParseJsonFloat(json, "humidity");
                _cachedReading.PressureHpa = ParseJsonFloat(json, "pressure_mb");
                _cachedReading.WindSpeedKmh = ParseJsonFloat(json, "wind_kph"); 
                
                // Condition text is inside "condition": {"text": "..."}
                string weatherCondition = ParseJsonString(json, "text");
                if (!string.IsNullOrWhiteSpace(weatherCondition)) _cachedReading.Condition = weatherCondition;
                
                string cityName = ParseJsonString(json, "name");
                string countryCode = ParseJsonString(json, "country");
                
                if (!string.IsNullOrWhiteSpace(cityName))
                    _cachedReading.Location = (!string.IsNullOrWhiteSpace(countryCode)) ? (cityName + ", " + countryCode).ToUpper() : cityName.ToUpper();
                else if (!string.IsNullOrWhiteSpace(fallbackCity))
                    _cachedReading.Location = fallbackCity.ToUpper();

                // Check for Official Weather Alerts first
                _cachedReading.AlertLevel = "NONE";
                _cachedReading.AlertMessage = "";

                int alertIdx = CustomIndexOf(json, "\"alert\":[", 0);
                if (alertIdx != -1)
                {
                    // Check if it's not empty
                    int closeBracket = CustomIndexOf(json, "]", alertIdx);
                    if (closeBracket > alertIdx + 5)
                    {
                        string headline = ParseJsonString(json.Substring(alertIdx), "headline");
                        string severity = ParseJsonString(json.Substring(alertIdx), "severity").ToUpper();
                        
                        if (!string.IsNullOrWhiteSpace(headline))
                        {
                            _cachedReading.AlertMessage = headline;
                            _cachedReading.AlertLevel = (severity == "EXTREME" || severity == "SEVERE" || headline.ToUpper().Contains("WARNING")) ? "RED" : "YELLOW";
                        }
                    }
                }

                // If no official alert, fall back to heuristic
                if (_cachedReading.AlertLevel == "NONE")
                {
                    string condUpper = (_cachedReading.Condition ?? "").ToUpper();
                    
                    if (condUpper.Contains("THUNDERSTORM") || condUpper.Contains("TORNADO") || condUpper.Contains("HURRICANE"))
                    {
                        _cachedReading.AlertLevel = "RED";
                        _cachedReading.AlertMessage = "SEVERE WEATHER WARNING: " + condUpper;
                    }
                    else if (_cachedReading.WindSpeedKmh > 70)
                    {
                        _cachedReading.AlertLevel = "RED";
                        _cachedReading.AlertMessage = "HIGH WIND WARNING (" + ((int)_cachedReading.WindSpeedKmh) + " km/h)";
                    }
                    else if (_cachedReading.WindSpeedKmh > 40)
                    {
                        _cachedReading.AlertLevel = "YELLOW";
                        _cachedReading.AlertMessage = "STRONG WIND ADVISORY (" + ((int)_cachedReading.WindSpeedKmh) + " km/h)";
                    }
                    else if (_cachedReading.TemperatureCelsius > 38)
                    {
                        _cachedReading.AlertLevel = "RED";
                        _cachedReading.AlertMessage = "EXTREME HEAT WARNING";
                    }
                    else if (_cachedReading.TemperatureCelsius > 32)
                    {
                        _cachedReading.AlertLevel = "YELLOW";
                        _cachedReading.AlertMessage = "HEAT ADVISORY";
                    }
                    else if (_cachedReading.TemperatureCelsius < -5)
                    {
                        _cachedReading.AlertLevel = "RED";
                        _cachedReading.AlertMessage = "EXTREME FREEZING WARNING";
                    }
                    else if (condUpper.Contains("HEAVY RAIN") || condUpper.Contains("EXTREME RAIN"))
                    {
                        _cachedReading.AlertLevel = "RED";
                        _cachedReading.AlertMessage = "FLASH FLOOD WARNING: " + condUpper;
                    }
                    else if (condUpper.Contains("SNOW") && _cachedReading.WindSpeedKmh > 30)
                    {
                        _cachedReading.AlertLevel = "RED";
                        _cachedReading.AlertMessage = "BLIZZARD WARNING";
                    }
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
