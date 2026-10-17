using System;

namespace WeatherOS.Services
{
    public struct ForecastDayData
    {
        public string Date;
        public string DayOfWeek;
        public float MaxTempC;
        public float MinTempC;
        public int ChanceOfRain;
        public string Condition;
        public string MoonPhase;
        public string MoonIllumination;
    }

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
        public string MoonPhase;
        public string MoonIllumination;
        public bool IsNight;
        public DateTime Timestamp;
        public ForecastDayData[] ForecastDays;

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
            MoonPhase = "Waxing Crescent";
            MoonIllumination = "15%";
            int hour = DateTime.UtcNow.Hour;
            IsNight = (hour < 6 || hour >= 20);
            Timestamp = DateTime.UtcNow;

            ForecastDays = new ForecastDayData[7];
            DateTime now = DateTime.UtcNow;
            for (int i = 0; i < 7; i++)
            {
                DateTime dayDt = now.AddDays(i);
                string dStr = dayDt.ToString("yyyy-MM-dd");
                ForecastDays[i] = new ForecastDayData
                {
                    Date = dStr,
                    DayOfWeek = i == 0 ? "TODAY" : (i == 1 ? "TOMORROW" : WeatherService.GetDayOfWeekName(dStr, i)),
                    MaxTempC = temp + (i % 3),
                    MinTempC = (temp - 6) + (i % 2),
                    ChanceOfRain = 10,
                    Condition = condition,
                    MoonPhase = "Waxing Crescent",
                    MoonIllumination = "15%"
                };
            }
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
                if (WeatherOS.Kernel.CustomNIC != null && !WeatherOS.Kernel.CustomNIC.IsPhyLinkUp())
                {
                    if (!WeatherOS.Graphics.TuiDesktop.IsGuiActive)
                        System.Console.WriteLine("[net] Ethernet cable disconnected (PHY link down).");
                    _cachedReading.Condition = "Cable Disconnected";
                    _cachedReading.Location = (!string.IsNullOrWhiteSpace(city) ? city.ToUpper() : "OFFLINE") + " [OFFLINE]";
                    return;
                }

                if (!WeatherOS.Graphics.TuiDesktop.IsGuiActive)
                    System.Console.WriteLine("[net] raw_tcp: initializing bare-metal TCP connection...");
                
                // Destination IPs: WeatherAPI Edge CDNs + Hetzner fallback for wttr.in
                var primaryIp = new Cosmos.System.Network.IPv4.Address(143, 244, 56, 52);    // WeatherAPI Anycast Edge
                var secondaryIp = new Cosmos.System.Network.IPv4.Address(143, 244, 56, 58);  // WeatherAPI Anycast Edge 2
                var wttrIp = new Cosmos.System.Network.IPv4.Address(5, 9, 243, 187);         // Fallback wttr.in
                var gatewayIp = RawTcpHttp.GetGatewayAddress();

                if (!string.IsNullOrEmpty(city))
                {
                    for (int i = 0; i < city.Length; i++)
                    {
                        char c = city[i];
                        if (c != '?' && c != '&' && c != '#' && c != '/' && c != '\\') cleanCity += c;
                    }
                }
                cleanCity = cleanCity.Trim();
                if (string.IsNullOrWhiteSpace(cleanCity)) cleanCity = "Setubal";

                string displayLocation;
                string safeCity = ResolveCityQuery(cleanCity, out displayLocation);

                // Immediately register query city so UI displays it immediately instead of reverting
                _cachedReading.Location = displayLocation;
                _cachedReading.Condition = "Awaiting Telemetry...";

                string effectiveKey = !string.IsNullOrWhiteSpace(apiKey) ? apiKey : "bc0cfac1c3144fc3bf0145134260810";

                string weatherApiRequest = "GET /v1/forecast.json?key=" + effectiveKey + "&q=" + safeCity + "&days=7 HTTP/1.1\r\nHost: api.weatherapi.com\r\nUser-Agent: WeatherOS/3.0\r\nConnection: close\r\n\r\n";

                if (_debugMode && !WeatherOS.Graphics.TuiDesktop.IsGuiActive)
                    System.Console.WriteLine($"[net] raw_tcp: querying WeatherAPI via {primaryIp.ToString()}...");

                // 1. Primary WeatherAPI Anycast CDN (143.244.56.52)
                string response = RawTcpHttp.FetchGet(primaryIp, gatewayIp, weatherApiRequest);

                // 2. Secondary WeatherAPI Edge CDN (143.244.56.58)
                if (string.IsNullOrWhiteSpace(response))
                {
                    if (_debugMode && !WeatherOS.Graphics.TuiDesktop.IsGuiActive)
                        System.Console.WriteLine($"[net] raw_tcp: falling back to secondary WeatherAPI edge ({secondaryIp.ToString()})...");
                    response = RawTcpHttp.FetchGet(secondaryIp, gatewayIp, weatherApiRequest);
                }

                // 3. Direct fallback to wttr.in if WeatherAPI was unreachable
                if (string.IsNullOrWhiteSpace(response))
                {
                    if (_debugMode && !WeatherOS.Graphics.TuiDesktop.IsGuiActive)
                        System.Console.WriteLine($"[net] raw_tcp: falling back to wttr.in ({wttrIp.ToString()})...");
                    string wttrCity = cleanCity.ToLower() == "setubal" ? "Setubal,Portugal" : safeCity;
                    string wttrRequest = "GET /" + wttrCity + "?format=j2 HTTP/1.1\r\nHost: wttr.in\r\nUser-Agent: curl/7.68.0\r\nConnection: close\r\n\r\n";
                    response = RawTcpHttp.FetchGet(wttrIp, gatewayIp, wttrRequest);
                }

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
                    else
                    {
                        _cachedReading.Location = "SETUBAL, PT (TIMEOUT)";
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
                else
                {
                    _cachedReading.Location = "SETUBAL, PT (ERROR)";
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
                string weatherCondition = "";
                int curBlockIdx = CustomIndexOf(json, "\"current\":", 0);
                if (curBlockIdx != -1)
                {
                    int condBlockIdx = CustomIndexOf(json, "\"condition\":", curBlockIdx);
                    if (condBlockIdx != -1 && condBlockIdx < curBlockIdx + 500)
                    {
                        weatherCondition = ParseJsonStringFromIndex(json, "text", condBlockIdx);
                    }
                }
                if (string.IsNullOrWhiteSpace(weatherCondition)) weatherCondition = ParseSectionValue(json, "\"weatherDesc\"");
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

                // Astronomy and Moon Phases
                string phase = ParseJsonString(json, "moon_phase");
                if (!string.IsNullOrWhiteSpace(phase)) _cachedReading.MoonPhase = phase.Trim();

                string illum = ParseJsonString(json, "moon_illumination");
                if (!string.IsNullOrWhiteSpace(illum)) _cachedReading.MoonIllumination = illum.Trim() + "%";

                float isDay = ParseJsonFloat(json, "is_day");
                if (CustomIndexOf(json, "\"is_day\":", 0) != -1)
                {
                    _cachedReading.IsNight = (isDay == 0f);
                }
                else
                {
                    int curHour = DateTime.UtcNow.Hour;
                    _cachedReading.IsNight = (curHour < 7 || curHour >= 20) || 
                        (_cachedReading.Condition != null && (_cachedReading.Condition.ToLower().Contains("night") || _cachedReading.Condition.ToLower().Contains("noite")));
                }
                
                // Parse 7-day forecast array
                Parse7DayForecast(json);
                if (_cachedReading.ForecastDays != null && _cachedReading.ForecastDays.Length > 0)
                {
                    if (string.IsNullOrWhiteSpace(_cachedReading.MoonPhase) || _cachedReading.MoonPhase == "Waxing Crescent")
                    {
                        if (!string.IsNullOrWhiteSpace(_cachedReading.ForecastDays[0].MoonPhase))
                            _cachedReading.MoonPhase = _cachedReading.ForecastDays[0].MoonPhase;
                    }
                    if (string.IsNullOrWhiteSpace(_cachedReading.MoonIllumination) || _cachedReading.MoonIllumination == "15%")
                    {
                        if (!string.IsNullOrWhiteSpace(_cachedReading.ForecastDays[0].MoonIllumination))
                            _cachedReading.MoonIllumination = _cachedReading.ForecastDays[0].MoonIllumination;
                    }
                }

                // Extract location: WeatherAPI uses location.name & location.country
                int locIdx = CustomIndexOf(json, "\"location\":", 0);
                if (locIdx != -1)
                {
                    string locName = ParseJsonStringFromIndex(json, "name", locIdx);
                    string locCountry = ParseJsonStringFromIndex(json, "country", locIdx);
                    string displayCity = !string.IsNullOrWhiteSpace(fallbackCity) ? fallbackCity : locName;
                    if (displayCity.ToLower().Contains("setubal") || displayCity.ToLower().Contains("setúbal") ||
                        locName.ToLower().Contains("set") || locName.ToLower().Contains("torrao"))
                    {
                        displayCity = "Setubal";
                    }

                    if (!string.IsNullOrWhiteSpace(displayCity))
                    {
                        string cSuffix = "";
                        if (!displayCity.ToUpper().EndsWith(", PT") && !displayCity.ToUpper().EndsWith(" PT"))
                        {
                            if (!string.IsNullOrWhiteSpace(locCountry))
                            {
                                string cLow = locCountry.ToLower();
                                if (cLow == "portugal") cSuffix = ", PT";
                                else if (cLow == "spain") cSuffix = ", ES";
                                else cSuffix = ", " + locCountry;
                            }
                        }
                        _cachedReading.Location = (displayCity + cSuffix).ToUpper();
                    }
                }
                else
                {
                    // Fallback to wttr nearest_area
                    string area = ParseSectionValue(json, "\"nearest_area\"");
                    string country = "";
                    int nearIdx = CustomIndexOf(json, "\"nearest_area\"", 0);
                    if (nearIdx != -1)
                    {
                        int ctryIdx = CustomIndexOf(json, "\"country\":", nearIdx);
                        if (ctryIdx != -1 && ctryIdx < nearIdx + 500)
                        {
                            country = ParseSectionValue(SafeSubstring(json, ctryIdx, json.Length - ctryIdx), "\"country\"");
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

        private float ParseJsonFloatFromIndex(string json, string key, int startIndex)
        {
            if (string.IsNullOrEmpty(json) || startIndex < 0 || startIndex >= json.Length) return 0f;
            string search = "\"" + key + "\":";
            int idx = CustomIndexOf(json, search, startIndex);
            if (idx == -1) return 0f;
            idx += search.Length;
            int endIdx = idx;
            while (endIdx < json.Length)
            {
                char c = json[endIdx];
                if (c == ',' || c == '}' || c == ']' || c == '\r' || c == '\n') break;
                endIdx++;
            }
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
                    else { fraction += digit / divisor; divisor *= 10f; }
                }
            }
            result += fraction;
            return isNegative ? -result : result;
        }

        private string ParseJsonStringFromIndex(string json, string key, int startIndex)
        {
            if (string.IsNullOrEmpty(json) || startIndex < 0 || startIndex >= json.Length) return "";
            string search = "\"" + key + "\":";
            int idx = CustomIndexOf(json, search, startIndex);
            if (idx == -1) return "";
            idx += search.Length;
            while (idx < json.Length && (json[idx] == ' ' || json[idx] == '\"' || json[idx] == '[' || json[idx] == '{' || json[idx] == '\r' || json[idx] == '\n')) idx++;
            int endIdx = idx;
            while (endIdx < json.Length && json[endIdx] != '\"' && json[endIdx] != ',' && json[endIdx] != '}' && json[endIdx] != ']' && json[endIdx] != '\r' && json[endIdx] != '\n') endIdx++;
            if (endIdx <= idx || endIdx > json.Length) return "";
            return SafeSubstring(json, idx, endIdx - idx).Trim();
        }

        public static string GetDayOfWeekName(string dateStr, int dayIndex)
        {
            if (dayIndex == 0) return "TODAY";
            if (dayIndex == 1) return "TOMORROW";
            if (!string.IsNullOrEmpty(dateStr) && dateStr.Length >= 10)
            {
                try
                {
                    int year = int.Parse(dateStr.Substring(0, 4));
                    int month = int.Parse(dateStr.Substring(5, 2));
                    int day = int.Parse(dateStr.Substring(8, 2));
                    var dt = new DateTime(year, month, day);
                    switch (dt.DayOfWeek)
                    {
                        case DayOfWeek.Sunday: return "SUNDAY";
                        case DayOfWeek.Monday: return "MONDAY";
                        case DayOfWeek.Tuesday: return "TUESDAY";
                        case DayOfWeek.Wednesday: return "WEDNESDAY";
                        case DayOfWeek.Thursday: return "THURSDAY";
                        case DayOfWeek.Friday: return "FRIDAY";
                        case DayOfWeek.Saturday: return "SATURDAY";
                    }
                }
                catch { }
            }
            return $"DAY +{dayIndex}";
        }

        private void Parse7DayForecast(string json)
        {
            try
            {
                if (_cachedReading.ForecastDays == null || _cachedReading.ForecastDays.Length != 7)
                {
                    _cachedReading.ForecastDays = new ForecastDayData[7];
                }

                int fcIdx = CustomIndexOf(json, "\"forecastday\":", 0);
                if (fcIdx == -1) fcIdx = CustomIndexOf(json, "\"weather\":", 0);
                if (fcIdx == -1) return;

                int searchFrom = fcIdx;
                int parsedDays = 0;
                for (int day = 0; day < 7; day++)
                {
                    int dateIdx = CustomIndexOf(json, "\"date\":\"", searchFrom);
                    if (dateIdx == -1) dateIdx = CustomIndexOf(json, "\"date\": \"", searchFrom);
                    if (dateIdx == -1) break;

                    string dateStr = ParseJsonStringFromIndex(json, "date", dateIdx - 1);
                    float maxT = ParseJsonFloatFromIndex(json, "maxtemp_c", dateIdx);
                    if (maxT == 0f) maxT = ParseJsonFloatFromIndex(json, "maxtempC", dateIdx);

                    float minT = ParseJsonFloatFromIndex(json, "mintemp_c", dateIdx);
                    if (minT == 0f) minT = ParseJsonFloatFromIndex(json, "mintempC", dateIdx);

                    float rainChance = ParseJsonFloatFromIndex(json, "daily_chance_of_rain", dateIdx);
                    if (rainChance == 0f) rainChance = ParseJsonFloatFromIndex(json, "chanceofrain", dateIdx);

                    // condition inside day
                    int dayBlockIdx = CustomIndexOf(json, "\"day\":", dateIdx);
                    string condText = "";
                    if (dayBlockIdx != -1)
                    {
                        condText = ParseJsonStringFromIndex(json, "text", dayBlockIdx);
                    }
                    if (string.IsNullOrEmpty(condText))
                    {
                        int hourlyIdx = CustomIndexOf(json, "\"hourly\":", dateIdx);
                        if (hourlyIdx != -1)
                        {
                            condText = ParseSectionValue(SafeSubstring(json, hourlyIdx, 600), "\"weatherDesc\"");
                        }
                    }
                    if (string.IsNullOrEmpty(condText)) condText = "Clear";

                    // astronomy inside day
                    int astroBlockIdx = CustomIndexOf(json, "\"astro\":", dateIdx);
                    if (astroBlockIdx == -1) astroBlockIdx = CustomIndexOf(json, "\"astronomy\":", dateIdx);
                    string moonPhase = "";
                    string moonIllum = "";
                    if (astroBlockIdx != -1)
                    {
                        moonPhase = ParseJsonStringFromIndex(json, "moon_phase", astroBlockIdx);
                        moonIllum = ParseJsonStringFromIndex(json, "moon_illumination", astroBlockIdx);
                        if (!string.IsNullOrEmpty(moonIllum) && !moonIllum.EndsWith("%")) moonIllum += "%";
                    }
                    if (string.IsNullOrEmpty(moonPhase)) moonPhase = "Waxing Crescent";

                    string dayOfWeek = GetDayOfWeekName(dateStr, day);

                    _cachedReading.ForecastDays[day] = new ForecastDayData
                    {
                        Date = dateStr,
                        DayOfWeek = dayOfWeek,
                        MaxTempC = maxT,
                        MinTempC = minT,
                        ChanceOfRain = (int)rainChance,
                        Condition = condText,
                        MoonPhase = moonPhase,
                        MoonIllumination = moonIllum
                    };

                    parsedDays++;
                    searchFrom = dateIdx + 20;
                }

                // Extrapolate remaining days if API provided less than 7 (e.g. 3-day forecast)
                if (parsedDays > 0 && parsedDays < 7)
                {
                    var lastValid = _cachedReading.ForecastDays[parsedDays - 1];
                    DateTime baseDate = DateTime.UtcNow;
                    if (!string.IsNullOrEmpty(lastValid.Date) && lastValid.Date.Length >= 10)
                    {
                        try
                        {
                            int y = int.Parse(lastValid.Date.Substring(0, 4));
                            int m = int.Parse(lastValid.Date.Substring(5, 2));
                            int d = int.Parse(lastValid.Date.Substring(8, 2));
                            baseDate = new DateTime(y, m, d);
                        }
                        catch { }
                    }

                    for (int day = parsedDays; day < 7; day++)
                    {
                        int dayOffset = day - (parsedDays - 1);
                        DateTime projectedDate = baseDate.AddDays(dayOffset);
                        string projDateStr = projectedDate.ToString("yyyy-MM-dd");
                        string projDayOfWeek = GetDayOfWeekName(projDateStr, day);

                        var prev = _cachedReading.ForecastDays[day - 1];
                        float maxT = prev.MaxTempC + ((day % 2 == 0) ? -1f : 0.5f);
                        float minT = prev.MinTempC + ((day % 2 == 0) ? -0.5f : 0.5f);
                        int rainChance = Math.Max(0, Math.Min(100, prev.ChanceOfRain + ((day % 2 == 0) ? -3 : 4)));
                        string cond = prev.Condition;

                        string mPhase = prev.MoonPhase;
                        string mIllum = prev.MoonIllumination;
                        AdvanceMoonPhase(ref mPhase, ref mIllum);

                        _cachedReading.ForecastDays[day] = new ForecastDayData
                        {
                            Date = projDateStr,
                            DayOfWeek = projDayOfWeek,
                            MaxTempC = maxT,
                            MinTempC = minT,
                            ChanceOfRain = rainChance,
                            Condition = cond,
                            MoonPhase = mPhase,
                            MoonIllumination = mIllum
                        };
                    }
                }
            }
            catch { }
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

        private static void AdvanceMoonPhase(ref string moonPhase, ref string moonIllum)
        {
            int currentIllum = 15;
            if (!string.IsNullOrEmpty(moonIllum))
            {
                string digits = "";
                for (int i = 0; i < moonIllum.Length; i++)
                {
                    if (moonIllum[i] >= '0' && moonIllum[i] <= '9') digits += moonIllum[i];
                }
                if (digits.Length > 0) int.TryParse(digits, out currentIllum);
            }

            currentIllum = Math.Min(100, currentIllum + 11);
            moonIllum = currentIllum.ToString() + "%";

            if (currentIllum < 10) moonPhase = "New Moon";
            else if (currentIllum < 45) moonPhase = "Waxing Crescent";
            else if (currentIllum < 55) moonPhase = "First Quarter";
            else if (currentIllum < 90) moonPhase = "Waxing Gibbous";
            else moonPhase = "Full Moon";
        }

        public static string ResolveCityQuery(string city, out string normalizedDisplayName)
        {
            string lower = (city ?? "").ToLower().Trim();

            // Clean common suffixes from previous queries or prompts
            if (lower.EndsWith(", pt")) lower = lower.Substring(0, lower.Length - 4).Trim();
            else if (lower.EndsWith(" pt")) lower = lower.Substring(0, lower.Length - 3).Trim();
            else if (lower.EndsWith(", portugal")) lower = lower.Substring(0, lower.Length - 10).Trim();

            if (lower == "setubal" || lower == "setúbal")
            {
                normalizedDisplayName = "SETUBAL, PT";
                return "38.5244,-8.8882"; // Precise coordinates for coastal Setúbal
            }
            if (lower == "lisbon" || lower == "lisboa")
            {
                normalizedDisplayName = "LISBOA, PT";
                return "38.7223,-9.1393";
            }
            if (lower == "porto" || lower == "oporto")
            {
                normalizedDisplayName = "PORTO, PT";
                return "41.1579,-8.6291";
            }
            if (lower == "braga")
            {
                normalizedDisplayName = "BRAGA, PT";
                return "41.5454,-8.4265";
            }
            if (lower == "coimbra")
            {
                normalizedDisplayName = "COIMBRA, PT";
                return "40.2033,-8.4103";
            }
            if (lower == "faro")
            {
                normalizedDisplayName = "FARO, PT";
                return "37.0194,-7.9304";
            }
            if (lower == "aveiro")
            {
                normalizedDisplayName = "AVEIRO, PT";
                return "40.6405,-8.6538";
            }
            if (lower == "evora" || lower == "évora")
            {
                normalizedDisplayName = "EVORA, PT";
                return "38.5714,-7.9135";
            }
            if (lower == "funchal")
            {
                normalizedDisplayName = "FUNCHAL, PT";
                return "32.6669,-16.9241";
            }
            if (lower == "ponta delgada")
            {
                normalizedDisplayName = "PONTA DELGADA, PT";
                return "37.7412,-25.6756";
            }
            if (lower == "leiria")
            {
                normalizedDisplayName = "LEIRIA, PT";
                return "39.7438,-8.8078";
            }
            if (lower == "santarem" || lower == "santarém")
            {
                normalizedDisplayName = "SANTAREM, PT";
                return "39.2362,-8.6865";
            }
            if (lower == "viana do castelo")
            {
                normalizedDisplayName = "VIANA DO CASTELO, PT";
                return "41.6918,-8.8344";
            }
            if (lower == "braganca" || lower == "bragança")
            {
                normalizedDisplayName = "BRAGANCA, PT";
                return "41.8058,-6.7572";
            }
            if (lower == "guarda")
            {
                normalizedDisplayName = "GUARDA, PT";
                return "40.5373,-7.2658";
            }
            if (lower == "castelo branco")
            {
                normalizedDisplayName = "CASTELO BRANCO, PT";
                return "39.8222,-7.4932";
            }
            if (lower == "portalegre")
            {
                normalizedDisplayName = "PORTALEGRE, PT";
                return "39.2938,-7.4312";
            }
            if (lower == "beja")
            {
                normalizedDisplayName = "BEJA, PT";
                return "38.0151,-7.8632";
            }

            normalizedDisplayName = city.ToUpper();
            return city.Replace(" ", "%20");
        }
    }
}
