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
        public float PrecipitationMm;
        public int WeatherCode;
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
            PrecipitationMm = 0.0f;
            WeatherCode = 0;
            Timestamp = DateTime.UtcNow;
        }
    }

    public class WeatherService
    {
        private WeatherData _cachedReading;
        private Cosmos.System.Network.IPv4.UDP.UdpClient _udpClient;

        public static bool IsDebugModeActive = false;
        private bool _debugMode = false;
        public void SetDebugMode(bool debug)
        {
            _debugMode = debug;
            IsDebugModeActive = debug;
        }

        public WeatherService()
        {
            _cachedReading = new WeatherData(
                temp: 20.0f,
                humidity: 60.0f,
                pressure: 1015.0f,
                windSpeed: 12.0f,
                windDir: "NW",
                condition: "Awaiting Telemetry...",
                loc: "SETUBAL, PT"
            );

            try { _udpClient = new Cosmos.System.Network.IPv4.UDP.UdpClient(6000); } catch { }
        }

        public WeatherData GetLatestReading() { return _cachedReading; }

        public string FetchAutoLocation()
        {
            try
            {
                if (!WeatherOS.Graphics.TuiDesktop.IsGuiActive)
                    System.Console.WriteLine("[net] raw_tcp: Auto-locating via IP (wttr.in)...");
                
                // IP for wttr.in (Hetzner European Server)
                var destIp = new Cosmos.System.Network.IPv4.Address(5, 9, 243, 187);
                var gatewayIp = new Cosmos.System.Network.IPv4.Address(10, 0, 2, 2);
                
                string request = "GET /?format=j2 HTTP/1.1\r\nHost: wttr.in\r\nUser-Agent: curl/7.68.0\r\nConnection: close\r\n\r\n";
                string response = RawTcpHttp.FetchGet(destIp, gatewayIp, request);
                
                if (!string.IsNullOrWhiteSpace(response))
                {
                    string area = ParseSectionValue(response, "\"nearest_area\"");
                    if (!string.IsNullOrWhiteSpace(area) && !area.ToUpper().Contains("LAT "))
                    {
                        return area.Trim();
                    }
                }
            }
            catch { }
            return "";
        }

        public void FetchOpenWeather(string city, string apiKey)
        {
            string cleanCity = "";
            try
            {
                if (!WeatherOS.Graphics.TuiDesktop.IsGuiActive)
                    System.Console.WriteLine("[net] raw_tcp: initializing bare-metal TCP handshake sequence...");
                
                // IP for wttr.in (Hetzner European Server)
                var destIp = new Cosmos.System.Network.IPv4.Address(5, 9, 243, 187);
                var gatewayIp = new Cosmos.System.Network.IPv4.Address(10, 0, 2, 2);

                if (!string.IsNullOrEmpty(city))
                {
                    for (int i = 0; i < city.Length; i++)
                    {
                        char c = city[i];
                        if (c != '?' && c != '&' && c != '#' && c != '/' && c != '\\') cleanCity += c;
                    }
                }
                cleanCity = cleanCity.Trim();

                // Immediately register query city so UI displays it immediately instead of reverting
                if (!string.IsNullOrWhiteSpace(cleanCity))
                {
                    _cachedReading.Location = cleanCity.ToUpper();
                    _cachedReading.Condition = "Awaiting Telemetry...";
                }

                string queryCity = cleanCity;
                // Only qualify Setubal specifically to avoid Laguna Setubal in Argentina
                if (cleanCity.ToLower() == "setubal")
                {
                    queryCity = "Setubal,Portugal";
                }

                string request;
                if (string.IsNullOrWhiteSpace(queryCity))
                {
                    // Auto-locate via IP if city is empty
                    request = "GET /?format=j2 HTTP/1.1\r\nHost: wttr.in\r\nUser-Agent: curl/7.68.0\r\nConnection: close\r\n\r\n";
                }
                else
                {
                    string safeCity = queryCity.Replace(" ", "%20");
                    request = "GET /" + safeCity + "?format=j2 HTTP/1.1\r\nHost: wttr.in\r\nUser-Agent: curl/7.68.0\r\nConnection: close\r\n\r\n";
                }
                                 
                if (_debugMode && !WeatherOS.Graphics.TuiDesktop.IsGuiActive)
                    System.Console.WriteLine($"[net] raw_tcp: sending SYN to {destIp.ToString()} via Bare-Metal DHCP Gateway");
                
                string response = RawTcpHttp.FetchGet(destIp, gatewayIp, request);
                
                if (!string.IsNullOrWhiteSpace(response))
                {
                    if (response.Contains("400 Bad Request") || response.Contains("ERR00") || response.Contains("location not found"))
                    {
                        _cachedReading.Location = (!string.IsNullOrWhiteSpace(cleanCity) ? cleanCity.ToUpper() : "UNKNOWN") + " (NOT FOUND)";
                        _cachedReading.Condition = "City Invalid or Not Found";
                        _cachedReading.TemperatureCelsius = 0;
                        _cachedReading.HumidityPercent = 0;
                        _cachedReading.AlertLevel = "NONE";
                        if (_debugMode && !WeatherOS.Graphics.TuiDesktop.IsGuiActive)
                            System.Console.WriteLine("[net] raw_tcp: API returned error for city.");
                    }
                    else if (CustomIndexOf(response, "\"temp_C\":", 0) != -1 || CustomIndexOf(response, "\"temp_c\":", 0) != -1)
                    {
                        ParseWeatherApiJson(response, cleanCity);
                        if (_debugMode && !WeatherOS.Graphics.TuiDesktop.IsGuiActive)
                            System.Console.WriteLine("[net] raw_tcp: payload received successfully. (status: 200 OK)");
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(cleanCity))
                        {
                            _cachedReading.Location = cleanCity.ToUpper() + " (RESP ERROR)";
                            _cachedReading.Condition = "Malformed Server Response";
                        }
                        if (_debugMode && !WeatherOS.Graphics.TuiDesktop.IsGuiActive)
                            System.Console.WriteLine("[net] raw_tcp: HTTP error or malformed response.");
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(cleanCity))
                    {
                        _cachedReading.Location = cleanCity.ToUpper() + " (TIMEOUT)";
                        _cachedReading.Condition = "Network Timeout / No Signal";
                    }
                    if (!WeatherOS.Graphics.TuiDesktop.IsGuiActive)
                        System.Console.WriteLine("[net] Telemetry fetch timed out.");
                }
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrWhiteSpace(cleanCity))
                {
                    _cachedReading.Location = cleanCity.ToUpper() + " (ERROR)";
                    _cachedReading.Condition = "Network Failure";
                }
                if (_debugMode && !WeatherOS.Graphics.TuiDesktop.IsGuiActive)
                    System.Console.WriteLine($"[net] raw_tcp: critical failure: {ex.Message}");
            }
        }

        private void ParseWeatherApiJson(string json, string fallbackCity)
        {
            try
            {
                float temp = ParseJsonFloat(json, "temp_C");
                if (temp == 0f) temp = ParseJsonFloat(json, "temp_c");
                _cachedReading.TemperatureCelsius = temp;

                _cachedReading.HumidityPercent = ParseJsonFloat(json, "humidity");

                float pressure = ParseJsonFloat(json, "pressure");
                if (pressure == 0f) pressure = ParseJsonFloat(json, "pressure_mb");
                _cachedReading.PressureHpa = pressure;

                float wind = ParseJsonFloat(json, "windspeedKmph");
                if (wind == 0f) wind = ParseJsonFloat(json, "wind_kph");
                _cachedReading.WindSpeedKmh = wind; 

                float precip = ParseJsonFloat(json, "precipMM");
                if (precip == 0f) precip = ParseJsonFloat(json, "precip_mm");
                _cachedReading.PrecipitationMm = precip;

                float wCode = ParseJsonFloat(json, "weatherCode");
                _cachedReading.WeatherCode = (int)wCode;

                string windDir = ParseJsonString(json, "winddir16Point");
                if (string.IsNullOrWhiteSpace(windDir)) windDir = ParseJsonString(json, "wind_dir");
                if (!string.IsNullOrWhiteSpace(windDir)) _cachedReading.WindDirection = windDir.Trim();
                
                // Condition text is inside "weatherDesc": [{"value": "..."}] or "condition": {"text": "..."}
                string weatherCondition = ParseSectionValue(json, "\"weatherDesc\"");
                if (string.IsNullOrWhiteSpace(weatherCondition)) weatherCondition = ParseJsonString(json, "value");
                if (string.IsNullOrWhiteSpace(weatherCondition)) weatherCondition = ParseJsonString(json, "text");

                if (string.IsNullOrWhiteSpace(weatherCondition) && _cachedReading.WeatherCode > 0)
                {
                    weatherCondition = GetConditionFromWeatherCode(_cachedReading.WeatherCode);
                }

                if (!string.IsNullOrWhiteSpace(weatherCondition))
                {
                    _cachedReading.Condition = weatherCondition.Trim();
                }
                else if (string.IsNullOrWhiteSpace(_cachedReading.Condition) || _cachedReading.Condition.Contains("Awaiting"))
                {
                    _cachedReading.Condition = "Telemetry Received";
                }
                
                // Extract nearest area and country from JSON
                string area = ParseSectionValue(json, "\"nearest_area\"");
                string country = "";
                int nearIdx = CustomIndexOf(json, "\"nearest_area\"", 0);
                if (nearIdx != -1)
                {
                    int ctryIdx = CustomIndexOf(json, "\"country\":", nearIdx);
                    if (ctryIdx != -1 && ctryIdx < nearIdx + 500)
                    {
                        country = ParseSectionValue(SafeSubstring(json, ctryIdx, json.Length - ctryIdx), "\"country\"");
                        if (string.IsNullOrEmpty(country))
                        {
                            int valIdx = CustomIndexOf(json, "\"value\":", ctryIdx);
                            if (valIdx != -1 && valIdx < ctryIdx + 100)
                            {
                                valIdx += 8;
                                while (valIdx < json.Length && (json[valIdx] == ' ' || json[valIdx] == '\"')) valIdx++;
                                int valEnd = valIdx;
                                while (valEnd < json.Length && json[valEnd] != '\"' && json[valEnd] != '\r' && json[valEnd] != '\n') valEnd++;
                                if (valEnd > valIdx && valEnd <= json.Length) country = SafeSubstring(json, valIdx, valEnd - valIdx).Trim();
                            }
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(area) && !area.ToUpper().Contains("LAT "))
                {
                    string countrySuffix = "";
                    if (!string.IsNullOrEmpty(country))
                    {
                        string cLower = country.ToLower();
                        if (cLower == "portugal") countrySuffix = ", PT";
                        else if (cLower == "spain") countrySuffix = ", ES";
                        else if (cLower == "estonia") countrySuffix = ", EE";
                        else countrySuffix = ", " + country;
                    }
                    _cachedReading.Location = (area + countrySuffix).ToUpper();
                }
                else if (!string.IsNullOrWhiteSpace(fallbackCity))
                {
                    string suf = fallbackCity.ToLower().Contains("setubal") ? ", PT" : "";
                    _cachedReading.Location = (fallbackCity + suf).ToUpper();
                }
                else
                {
                    _cachedReading.Location = "SETUBAL, PT";
                }

                // Comprehensive Meteorological Weather Warnings & Hazard Detection
                _cachedReading.AlertLevel = "NONE";
                _cachedReading.AlertMessage = "All clear. No severe weather hazards reported.";

                string condUpper = (_cachedReading.Condition ?? "").ToUpper();
                int code = _cachedReading.WeatherCode;
                float precipMm = _cachedReading.PrecipitationMm;
                float press = _cachedReading.PressureHpa;
                float windSpd = _cachedReading.WindSpeedKmh;
                float tempC = _cachedReading.TemperatureCelsius;

                bool isThunder = condUpper.Contains("THUNDER") || condUpper.Contains("LIGHTNING") ||
                                 condUpper.Contains("TROVOADA") || condUpper.Contains("TEMPESTADE") ||
                                 condUpper.Contains("TORMENTA") || condUpper.Contains("TORNADO") ||
                                 condUpper.Contains("HURRICANE") || condUpper.Contains("CYCLONE") ||
                                 code == 200 || code == 386 || code == 389 || code == 392 || code == 395;

                bool isHeavyRain = condUpper.Contains("HEAVY RAIN") || condUpper.Contains("TORRENTIAL") ||
                                   condUpper.Contains("EXTREME RAIN") || condUpper.Contains("DOWNPOUR") ||
                                   condUpper.Contains("FLOOD") || condUpper.Contains("CHUVA FORTE") ||
                                   condUpper.Contains("CHUVA TORRENCIAL") ||
                                   code == 359 || code == 308 || code == 305 || precipMm >= 5.0f;

                bool isRain = condUpper.Contains("RAIN") || condUpper.Contains("SHOWER") ||
                              condUpper.Contains("DRIZZLE") || condUpper.Contains("AGUACEIRO") ||
                              condUpper.Contains("CHUVA") || condUpper.Contains("PRECIPITATION") ||
                              condUpper.Contains("SLEET") ||
                              code == 353 || code == 356 || code == 293 || code == 296 ||
                              code == 302 || code == 299 || code == 263 || code == 266 ||
                              code == 176 || code == 311 || code == 314 || precipMm > 0.0f;

                bool isBlizzard = condUpper.Contains("BLIZZARD") || condUpper.Contains("NEVASCA") ||
                                  code == 230 || (condUpper.Contains("SNOW") && windSpd > 25.0f);

                bool isSnow = condUpper.Contains("SNOW") || condUpper.Contains("NEVE") ||
                              condUpper.Contains("FLURR") ||
                              code == 227 || code == 323 || code == 326 || code == 329 ||
                              code == 332 || code == 335 || code == 338 || code == 350 ||
                              code == 368 || code == 371;

                bool isFog = condUpper.Contains("FOG") || condUpper.Contains("MIST") ||
                             condUpper.Contains("NEVOEIRO") || condUpper.Contains("NEBLINA") ||
                             code == 248 || code == 260 || code == 143;

                if (isThunder)
                {
                    _cachedReading.AlertLevel = "RED";
                    _cachedReading.AlertMessage = "THUNDERSTORM & RAIN WARNING: " + condUpper;
                }
                else if (isHeavyRain)
                {
                    _cachedReading.AlertLevel = "RED";
                    _cachedReading.AlertMessage = "HEAVY RAIN & FLASH FLOOD WARNING: " + condUpper;
                }
                else if (isBlizzard)
                {
                    _cachedReading.AlertLevel = "RED";
                    _cachedReading.AlertMessage = "BLIZZARD & DRIFTING SNOW WARNING";
                }
                else if (windSpd > 70.0f)
                {
                    _cachedReading.AlertLevel = "RED";
                    _cachedReading.AlertMessage = "HIGH WIND / GALE WARNING (" + ((int)windSpd) + " km/h)";
                }
                else if (tempC > 38.0f)
                {
                    _cachedReading.AlertLevel = "RED";
                    _cachedReading.AlertMessage = "EXTREME HEAT WARNING (" + ((int)tempC) + " C)";
                }
                else if (tempC < -8.0f)
                {
                    _cachedReading.AlertLevel = "RED";
                    _cachedReading.AlertMessage = "EXTREME FREEZING WARNING (" + ((int)tempC) + " C)";
                }
                else if (isRain && press > 0.0f && press <= 1008.0f)
                {
                    // Convective low-pressure rain and thunderstorm advisory (e.g. Baltic depression front in Parnu)
                    _cachedReading.AlertLevel = "YELLOW";
                    _cachedReading.AlertMessage = "RAIN & THUNDERSTORM ADVISORY: " + condUpper;
                }
                else if (isRain && windSpd >= 35.0f)
                {
                    _cachedReading.AlertLevel = "YELLOW";
                    _cachedReading.AlertMessage = "RAIN & SQUALL ADVISORY: " + condUpper + " (" + ((int)windSpd) + " km/h)";
                }
                else if (isRain)
                {
                    _cachedReading.AlertLevel = "YELLOW";
                    _cachedReading.AlertMessage = "RAIN & PRECIPITATION ADVISORY: " + condUpper;
                }
                else if (isSnow)
                {
                    _cachedReading.AlertLevel = "YELLOW";
                    _cachedReading.AlertMessage = "WINTER WEATHER & SNOW ADVISORY: " + condUpper;
                }
                else if (windSpd > 40.0f)
                {
                    _cachedReading.AlertLevel = "YELLOW";
                    _cachedReading.AlertMessage = "STRONG WIND ADVISORY (" + ((int)windSpd) + " km/h)";
                }
                else if (tempC > 32.0f)
                {
                    _cachedReading.AlertLevel = "YELLOW";
                    _cachedReading.AlertMessage = "HEAT ADVISORY (" + ((int)tempC) + " C)";
                }
                else if (tempC <= 1.0f)
                {
                    _cachedReading.AlertLevel = "YELLOW";
                    _cachedReading.AlertMessage = "FROST & SUB-ZERO ADVISORY (" + ((int)tempC) + " C)";
                }
                else if (isFog)
                {
                    _cachedReading.AlertLevel = "YELLOW";
                    _cachedReading.AlertMessage = "DENSE FOG & REDUCED VISIBILITY ADVISORY";
                }
                else
                {
                    _cachedReading.AlertLevel = "NONE";
                    _cachedReading.AlertMessage = "All clear. No severe weather hazards reported.";
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

        private static string SafeSubstring(string str, int start, int length)
        {
            if (string.IsNullOrEmpty(str)) return "";
            if (start < 0) start = 0;
            if (start >= str.Length) return "";
            if (length < 0) return "";
            if (start + length > str.Length) length = str.Length - start;
            return str.Substring(start, length);
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
            if (endIdx > json.Length) endIdx = json.Length;
            
            string valStr = SafeSubstring(json, idx, endIdx - idx).Trim();
            
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
            result += fraction;
            return isNegative ? -result : result;
        }

        private string ParseSectionValue(string json, string sectionMarker)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(sectionMarker)) return "";
            int secIdx = CustomIndexOf(json, sectionMarker, 0);
            if (secIdx == -1) return "";
            
            int valIdx = CustomIndexOf(json, "\"value\":", secIdx);
            if (valIdx == -1 || valIdx > secIdx + 400) return "";
            
            valIdx += 8;
            while (valIdx < json.Length && (json[valIdx] == ' ' || json[valIdx] == '\"' || json[valIdx] == '\r' || json[valIdx] == '\n')) valIdx++;
            
            int endIdx = valIdx;
            while (endIdx < json.Length && json[endIdx] != '\"' && json[endIdx] != '\r' && json[endIdx] != '\n') endIdx++;
            if (endIdx > valIdx && endIdx <= json.Length)
            {
                return SafeSubstring(json, valIdx, endIdx - valIdx).Trim();
            }
            return "";
        }

        private string ParseJsonString(string json, string key)
        {
            string search = "\"" + key + "\":";
            int idx = CustomIndexOf(json, search, 0);
            if (idx == -1) return "";
            
            idx += search.Length;
            while (idx < json.Length && (json[idx] == ' ' || json[idx] == '\"' || json[idx] == '[' || json[idx] == '{' || json[idx] == '\r' || json[idx] == '\n')) idx++;
            
            int endIdx = idx;
            while (endIdx < json.Length && json[endIdx] != '\"' && json[endIdx] != ',' && json[endIdx] != '}' && json[endIdx] != ']' && json[endIdx] != '\r' && json[endIdx] != '\n') endIdx++;
            if (endIdx <= idx || endIdx > json.Length) return "";
            
            return SafeSubstring(json, idx, endIdx - idx).Trim();
        }

        private void ParseTelemetryPacket(string packet)
        {
            string[] parts = packet.Split(';');
            foreach (var part in parts)
            {
                if (part.Length < 3) continue;
                
                string key = part.Substring(0, 2);
                string val = part.Substring(2);

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

        public float GetDewPointCelsius()
        {
            return _cachedReading.TemperatureCelsius - ((100.0f - _cachedReading.HumidityPercent) / 5.0f);
        }

        public static string GetConditionFromWeatherCode(int code)
        {
            switch (code)
            {
                case 113: return "Clear / Sunny";
                case 116: return "Partly Cloudy";
                case 119: return "Cloudy";
                case 122: return "Overcast";
                case 143: return "Mist";
                case 176: return "Patchy Rain";
                case 179: return "Patchy Snow";
                case 182: return "Patchy Sleet";
                case 185: return "Freezing Drizzle";
                case 200: return "Thundery Outbreaks";
                case 227: return "Blowing Snow";
                case 230: return "Blizzard";
                case 248: return "Fog";
                case 260: return "Freezing Fog";
                case 263: return "Patchy Light Drizzle";
                case 266: return "Light Drizzle";
                case 281: return "Freezing Drizzle";
                case 284: return "Heavy Freezing Drizzle";
                case 293: return "Patchy Light Rain";
                case 296: return "Light Rain";
                case 299: return "Moderate Rain at Times";
                case 302: return "Moderate Rain";
                case 305: return "Heavy Rain at Times";
                case 308: return "Heavy Rain";
                case 311: return "Light Freezing Rain";
                case 314: return "Heavy Freezing Rain";
                case 317: return "Light Sleet";
                case 320: return "Heavy Sleet";
                case 323: return "Patchy Light Snow";
                case 326: return "Light Snow";
                case 329: return "Patchy Moderate Snow";
                case 332: return "Moderate Snow";
                case 335: return "Patchy Heavy Snow";
                case 338: return "Heavy Snow";
                case 350: return "Ice Pellets";
                case 353: return "Light Rain Shower";
                case 356: return "Moderate / Heavy Rain Shower";
                case 359: return "Torrential Rain Shower";
                case 362: return "Light Sleet Showers";
                case 365: return "Moderate / Heavy Sleet Showers";
                case 368: return "Light Snow Showers";
                case 371: return "Heavy Snow Showers";
                case 374: return "Light Ice Pellet Showers";
                case 377: return "Heavy Ice Pellet Showers";
                case 386: return "Thunderstorm with Rain";
                case 389: return "Heavy Thunderstorm with Rain";
                case 392: return "Thunderstorm with Snow";
                case 395: return "Heavy Thunderstorm with Snow";
                default: return "";
            }
        }
    }
}
