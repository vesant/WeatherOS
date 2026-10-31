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
            string[] dayNames = new string[] { "TODAY", "TOMORROW", "WEDNESDAY", "THURSDAY", "FRIDAY", "SATURDAY", "SUNDAY" };
            for (int i = 0; i < 7; i++)
            {
                int dayNum = 28 + i;
                int monthNum = 10;
                if (dayNum > 31)
                {
                    dayNum -= 31;
                    monthNum = 11;
                }
                string mStr = (monthNum < 10) ? ("0" + monthNum) : monthNum.ToString();
                string dStr = (dayNum < 10) ? ("0" + dayNum) : dayNum.ToString();
                string dateStr = "2026-" + mStr + "-" + dStr;

                ForecastDays[i] = new ForecastDayData
                {
                    Date = dateStr,
                    DayOfWeek = (i < dayNames.Length) ? dayNames[i] : ("DAY +" + i),
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
                temp: 16.5f,
                humidity: 58.0f,
                pressure: 1017.0f,
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

        public bool FetchQuickStationWeather(string lat, string lon, out int tempC, out int humidity, out int windKmh, out string condition)
        {
            tempC = 18;
            humidity = 65;
            windKmh = 12;
            condition = "Partly Cloudy";

            try
            {
                if (RawTcpHttp.GetAssignedIp() == "0.0.0.0") return false;

                // Sanitize coordinates to clean decimal strings for Open-Meteo API
                string sLat = (lat ?? "0").Trim();
                string sLon = (lon ?? "0").Trim();

                bool latNeg = sLat.EndsWith("S") || sLat.EndsWith("s") || sLat.StartsWith("-");
                bool lonNeg = sLon.EndsWith("W") || sLon.EndsWith("w") || sLon.StartsWith("-");

                sLat = sLat.Replace("N", "").Replace("n", "").Replace("S", "").Replace("s", "").Replace("+", "").Trim();
                sLon = sLon.Replace("E", "").Replace("e", "").Replace("W", "").Replace("w", "").Replace("+", "").Trim();

                if (latNeg && !sLat.StartsWith("-")) sLat = "-" + sLat;
                if (lonNeg && !sLon.StartsWith("-")) sLon = "-" + sLon;

                var openMeteoIp = new Cosmos.System.Network.IPv4.Address(94, 130, 142, 35);
                var gatewayIp = RawTcpHttp.GetGatewayAddress();

                string req = "GET /v1/forecast?latitude=" + sLat + "&longitude=" + sLon + "&current=temperature_2m,relative_humidity_2m,weather_code,wind_speed_10m&timezone=auto HTTP/1.1\r\nHost: api.open-meteo.com\r\nUser-Agent: WeatherOS/3.3\r\nConnection: close\r\n\r\n";
                string resp = RawTcpHttp.FetchGet(openMeteoIp, gatewayIp, req);
                if (!string.IsNullOrWhiteSpace(resp))
                {
                    int curIdx = CustomIndexOf(resp, "\"current\":", 0);
                    if (curIdx != -1)
                    {
                        float t = ParseJsonFloatFromIndex(resp, "temperature_2m", curIdx);
                        float h = ParseJsonFloatFromIndex(resp, "relative_humidity_2m", curIdx);
                        float w = ParseJsonFloatFromIndex(resp, "wind_speed_10m", curIdx);
                        float code = ParseJsonFloatFromIndex(resp, "weather_code", curIdx);

                        tempC = (int)t;
                        humidity = (int)h;
                        windKmh = (int)w;
                        condition = GetConditionFromWmoCode((int)code);
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        public void FetchOpenWeather(string city, string apiKey)
        {
            string cleanCity = "";
            try
            {
                if (RawTcpHttp.GetAssignedIp() == "0.0.0.0")
                {
                    if (!WeatherOS.Graphics.TuiDesktop.IsGuiActive)
                        System.Console.WriteLine("[net] Network offline. No IP assigned.");
                    _cachedReading.Condition = "Network Offline";
                    _cachedReading.Location = (!string.IsNullOrWhiteSpace(city) ? city.ToUpper() : "OFFLINE") + " [OFFLINE]";
                    return;
                }

                if (!WeatherOS.Graphics.TuiDesktop.IsGuiActive)
                    System.Console.WriteLine("[net] raw_tcp: initializing bare-metal TCP connection...");
                
                // Destination IPs: Open-Meteo (Hetzner, High Precision) + WeatherAPI Anycast Edge + wttr.in
                var openMeteoIp = new Cosmos.System.Network.IPv4.Address(94, 130, 142, 35);   // Open-Meteo (api.open-meteo.com)
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

                string response = null;
                bool isOpenMeteo = false;

                // 1. High-Precision Query via Open-Meteo when coordinates are available (provides exact humidity & genuine 7-day forecast)
                if (safeCity.Contains(",") && IsNumericCoordPair(safeCity))
                {
                    string[] coords = safeCity.Split(',');
                    if (coords.Length == 2)
                    {
                        string lat = coords[0].Trim();
                        string lon = coords[1].Trim();
                        string openMeteoRequest = "GET /v1/forecast?latitude=" + lat + "&longitude=" + lon + "&current=temperature_2m,relative_humidity_2m,apparent_temperature,precipitation,weather_code,surface_pressure,wind_speed_10m,wind_direction_10m&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max&timezone=auto HTTP/1.1\r\nHost: api.open-meteo.com\r\nUser-Agent: WeatherOS/3.0\r\nConnection: close\r\n\r\n";
                        if (_debugMode && !WeatherOS.Graphics.TuiDesktop.IsGuiActive)
                            System.Console.WriteLine("[net] raw_tcp: querying Open-Meteo via " + openMeteoIp.ToString() + "...");
                        response = RawTcpHttp.FetchGet(openMeteoIp, gatewayIp, openMeteoRequest);
                        if (!string.IsNullOrWhiteSpace(response) && CustomIndexOf(response, "\"current\":", 0) != -1)
                        {
                            isOpenMeteo = true;
                        }
                        else
                        {
                            response = null;
                        }
                    }
                }

                // 2. Primary WeatherAPI Anycast CDN (143.244.56.52) fallback
                if (string.IsNullOrWhiteSpace(response))
                {
                    string effectiveKey = !string.IsNullOrWhiteSpace(apiKey) ? apiKey : "bc0cfac1c3144fc3bf0145134260810";
                    string weatherApiRequest = "GET /v1/forecast.json?key=" + effectiveKey + "&q=" + safeCity + "&days=7 HTTP/1.1\r\nHost: api.weatherapi.com\r\nUser-Agent: WeatherOS/3.0\r\nConnection: close\r\n\r\n";

                    if (_debugMode && !WeatherOS.Graphics.TuiDesktop.IsGuiActive)
                        System.Console.WriteLine("[net] raw_tcp: querying WeatherAPI via " + primaryIp.ToString() + "...");
                    response = RawTcpHttp.FetchGet(primaryIp, gatewayIp, weatherApiRequest);

                    // 3. Secondary WeatherAPI Edge CDN (143.244.56.58)
                    if (string.IsNullOrWhiteSpace(response))
                    {
                        if (_debugMode && !WeatherOS.Graphics.TuiDesktop.IsGuiActive)
                            System.Console.WriteLine("[net] raw_tcp: falling back to secondary WeatherAPI edge (" + secondaryIp.ToString() + ")...");
                        response = RawTcpHttp.FetchGet(secondaryIp, gatewayIp, weatherApiRequest);
                    }
                }

                // 4. Direct fallback to wttr.in if WeatherAPI was unreachable
                if (string.IsNullOrWhiteSpace(response))
                {
                    if (_debugMode && !WeatherOS.Graphics.TuiDesktop.IsGuiActive)
                        System.Console.WriteLine("[net] raw_tcp: falling back to wttr.in (" + wttrIp.ToString() + ")...");
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
                    else if (isOpenMeteo)
                    {
                        ParseOpenMeteoJson(response, cleanCity, displayLocation);
                        if (_debugMode && !WeatherOS.Graphics.TuiDesktop.IsGuiActive)
                            System.Console.WriteLine("[net] raw_tcp: Open-Meteo telemetry received successfully. (status: 200 OK)");
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
                    System.Console.WriteLine("[net] raw_tcp: critical failure: " + ex.Message);
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

                // Meteorological Weather Warnings & Hazard Detection
                EvaluateWeatherAlerts();
            }
            catch { }
        }

        private void ParseOpenMeteoJson(string json, string fallbackCity, string displayLocation)
        {
            try
            {
                int curIdx = CustomIndexOf(json, "\"current\":", 0);
                if (curIdx != -1)
                {
                    _cachedReading.TemperatureCelsius = ParseJsonFloatFromIndex(json, "temperature_2m", curIdx);
                    _cachedReading.HumidityPercent = ParseJsonFloatFromIndex(json, "relative_humidity_2m", curIdx);
                    _cachedReading.PressureHpa = ParseJsonFloatFromIndex(json, "surface_pressure", curIdx);
                    _cachedReading.WindSpeedKmh = ParseJsonFloatFromIndex(json, "wind_speed_10m", curIdx);
                    _cachedReading.PrecipitationMm = ParseJsonFloatFromIndex(json, "precipitation", curIdx);

                    float wCode = ParseJsonFloatFromIndex(json, "weather_code", curIdx);
                    _cachedReading.WeatherCode = (int)wCode;
                    _cachedReading.Condition = GetConditionFromWmoCode((int)wCode);

                    float windDirDeg = ParseJsonFloatFromIndex(json, "wind_direction_10m", curIdx);
                    _cachedReading.WindDirection = WindDegreesToDirection((int)windDirDeg);
                }

                if (!string.IsNullOrWhiteSpace(displayLocation))
                {
                    _cachedReading.Location = displayLocation;
                }
                else if (!string.IsNullOrWhiteSpace(fallbackCity))
                {
                    _cachedReading.Location = fallbackCity.ToUpper();
                }
                else
                {
                    _cachedReading.Location = "SETUBAL, PT";
                }

                int curHour = DateTime.UtcNow.Hour;
                _cachedReading.IsNight = (curHour < 7 || curHour >= 20);

                // Open-Meteo astronomical moon phase & illumination estimation
                DateTime now = DateTime.UtcNow;
                var refNewMoon = new DateTime(2024, 1, 11, 11, 57, 0, DateTimeKind.Utc);
                double daysSince = (now - refNewMoon).TotalDays;
                double synodicCycle = daysSince % 29.53058867;
                if (synodicCycle < 0) synodicCycle += 29.53058867;
                double phasePct = synodicCycle / 29.53058867;
                int illumPct = (int)(Math.Round((1.0 - Math.Cos(phasePct * 2.0 * Math.PI)) / 2.0 * 100.0));
                _cachedReading.MoonIllumination = illumPct + "%";

                if (synodicCycle < 1.84) _cachedReading.MoonPhase = "New Moon";
                else if (synodicCycle < 5.53) _cachedReading.MoonPhase = "Waxing Crescent";
                else if (synodicCycle < 9.22) _cachedReading.MoonPhase = "First Quarter";
                else if (synodicCycle < 12.91) _cachedReading.MoonPhase = "Waxing Gibbous";
                else if (synodicCycle < 16.61) _cachedReading.MoonPhase = "Full Moon";
                else if (synodicCycle < 20.30) _cachedReading.MoonPhase = "Waning Gibbous";
                else if (synodicCycle < 23.99) _cachedReading.MoonPhase = "Last Quarter";
                else if (synodicCycle < 27.68) _cachedReading.MoonPhase = "Waning Crescent";
                else _cachedReading.MoonPhase = "New Moon";

                // Parse 7-day daily forecast arrays directly from Open-Meteo
                int dailyIdx = CustomIndexOf(json, "\"daily\":", 0);
                if (dailyIdx != -1)
                {
                    string[] dates = ParseJsonStringArray(json, "time", 7);
                    float[] wCodes = ParseJsonFloatArray(json, "weather_code", 7);
                    float[] maxTemps = ParseJsonFloatArray(json, "temperature_2m_max", 7);
                    float[] minTemps = ParseJsonFloatArray(json, "temperature_2m_min", 7);
                    float[] rainProbs = ParseJsonFloatArray(json, "precipitation_probability_max", 7);

                    int count = Math.Min(7, dates.Length);
                    if (count > 0)
                    {
                        _cachedReading.ForecastDays = new ForecastDayData[7];
                        string curPhase = _cachedReading.MoonPhase;
                        string curIllum = _cachedReading.MoonIllumination;

                        for (int d = 0; d < 7; d++)
                        {
                            string dDate = (d < dates.Length) ? dates[d] : "";
                            string dDayOfWeek = GetDayOfWeekName(dDate, d);
                            int dCode = (d < wCodes.Length) ? (int)wCodes[d] : _cachedReading.WeatherCode;
                            float dMax = (d < maxTemps.Length) ? maxTemps[d] : _cachedReading.TemperatureCelsius;
                            float dMin = (d < minTemps.Length) ? minTemps[d] : (_cachedReading.TemperatureCelsius - 5f);
                            int dRain = (d < rainProbs.Length) ? (int)rainProbs[d] : 0;
                            string dCond = GetConditionFromWmoCode(dCode);

                            _cachedReading.ForecastDays[d] = new ForecastDayData
                            {
                                Date = dDate,
                                DayOfWeek = dDayOfWeek,
                                MaxTempC = dMax,
                                MinTempC = dMin,
                                ChanceOfRain = dRain,
                                Condition = dCond,
                                MoonPhase = curPhase,
                                MoonIllumination = curIllum
                            };

                            AdvanceMoonPhase(ref curPhase, ref curIllum);
                        }
                    }
                }

                EvaluateWeatherAlerts();
            }
            catch { }
        }

        private void EvaluateWeatherAlerts()
        {
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
                             code == 200 || code == 386 || code == 389 || code == 392 || code == 395 ||
                             code == 95 || code == 96 || code == 99;

            bool isHeavyRain = condUpper.Contains("HEAVY RAIN") || condUpper.Contains("TORRENTIAL") ||
                               condUpper.Contains("EXTREME RAIN") || condUpper.Contains("DOWNPOUR") ||
                               condUpper.Contains("FLOOD") || condUpper.Contains("CHUVA FORTE") ||
                               condUpper.Contains("CHUVA TORRENCIAL") ||
                               code == 359 || code == 308 || code == 305 || code == 65 || code == 82 || precipMm >= 5.0f;

            bool isRain = condUpper.Contains("RAIN") || condUpper.Contains("SHOWER") ||
                          condUpper.Contains("DRIZZLE") || condUpper.Contains("AGUACEIRO") ||
                          condUpper.Contains("CHUVA") || condUpper.Contains("PRECIPITATION") ||
                          condUpper.Contains("SLEET") ||
                          code == 353 || code == 356 || code == 293 || code == 296 ||
                          code == 302 || code == 299 || code == 263 || code == 266 ||
                          code == 176 || code == 311 || code == 314 ||
                          (code >= 51 && code <= 67) || (code >= 80 && code <= 82) || precipMm > 0.0f;

            bool isBlizzard = condUpper.Contains("BLIZZARD") || condUpper.Contains("NEVASCA") ||
                              code == 230 || (condUpper.Contains("SNOW") && windSpd > 25.0f);

            bool isSnow = condUpper.Contains("SNOW") || condUpper.Contains("NEVE") ||
                          condUpper.Contains("FLURR") ||
                          code == 227 || code == 323 || code == 326 || code == 329 ||
                          code == 332 || code == 335 || code == 338 || code == 350 ||
                          code == 368 || code == 371 || (code >= 71 && code <= 77) || (code >= 85 && code <= 86);

            bool isFog = condUpper.Contains("FOG") || condUpper.Contains("MIST") ||
                         condUpper.Contains("NEVOEIRO") || condUpper.Contains("NEBLINA") ||
                         code == 248 || code == 260 || code == 143 || code == 45 || code == 48;

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

        private static string GetConditionFromWmoCode(int code)
        {
            switch (code)
            {
                case 0: return "Clear Sky";
                case 1: return "Mainly Clear";
                case 2: return "Partly Cloudy";
                case 3: return "Overcast";
                case 45: return "Fog";
                case 48: return "Depositing Rime Fog";
                case 51: return "Light Drizzle";
                case 53: return "Moderate Drizzle";
                case 55: return "Dense Drizzle";
                case 56: return "Light Freezing Drizzle";
                case 57: return "Dense Freezing Drizzle";
                case 61: return "Slight Rain";
                case 63: return "Moderate Rain";
                case 65: return "Heavy Rain";
                case 66: return "Light Freezing Rain";
                case 67: return "Heavy Freezing Rain";
                case 71: return "Slight Snow Fall";
                case 73: return "Moderate Snow Fall";
                case 75: return "Heavy Snow Fall";
                case 77: return "Snow Grains";
                case 80: return "Slight Rain Showers";
                case 81: return "Moderate Rain Showers";
                case 82: return "Violent Rain Showers";
                case 85: return "Slight Snow Showers";
                case 86: return "Heavy Snow Showers";
                case 95: return "Thunderstorm";
                case 96: return "Thunderstorm with Slight Hail";
                case 99: return "Thunderstorm with Heavy Hail";
                default: return "Partly Cloudy";
            }
        }

        private static string WindDegreesToDirection(int deg)
        {
            string[] dirs = { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };
            int idx = (int)((deg + 11.25f) / 22.5f) % 16;
            if (idx < 0) idx += 16;
            return dirs[idx];
        }

        private static float ParseFloatFromString(string valStr)
        {
            if (string.IsNullOrEmpty(valStr)) return 0f;
            valStr = valStr.Trim();
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

        private string[] ParseJsonStringArray(string json, string key, int maxCount)
        {
            var list = new System.Collections.Generic.List<string>();
            string search = "\"" + key + "\":[";
            int idx = CustomIndexOf(json, search, 0);
            if (idx == -1)
            {
                search = "\"" + key + "\": [";
                idx = CustomIndexOf(json, search, 0);
            }
            if (idx == -1) return list.ToArray();
            idx += search.Length;
            int endIdx = CustomIndexOf(json, "]", idx);
            if (endIdx == -1) return list.ToArray();
            string arrayStr = SafeSubstring(json, idx, endIdx - idx);
            string[] parts = arrayStr.Split(',');
            for (int i = 0; i < parts.Length && list.Count < maxCount; i++)
            {
                string item = parts[i].Replace("\"", "").Trim();
                if (!string.IsNullOrEmpty(item)) list.Add(item);
            }
            return list.ToArray();
        }

        private float[] ParseJsonFloatArray(string json, string key, int maxCount)
        {
            var list = new System.Collections.Generic.List<float>();
            string search = "\"" + key + "\":[";
            int idx = CustomIndexOf(json, search, 0);
            if (idx == -1)
            {
                search = "\"" + key + "\": [";
                idx = CustomIndexOf(json, search, 0);
            }
            if (idx == -1) return list.ToArray();
            idx += search.Length;
            int endIdx = CustomIndexOf(json, "]", idx);
            if (endIdx == -1) return list.ToArray();
            string arrayStr = SafeSubstring(json, idx, endIdx - idx);
            string[] parts = arrayStr.Split(',');
            for (int i = 0; i < parts.Length && list.Count < maxCount; i++)
            {
                string item = parts[i].Trim();
                if (!string.IsNullOrEmpty(item))
                {
                    list.Add(ParseFloatFromString(item));
                }
            }
            return list.ToArray();
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
            string[] dayNames = new string[] { "TODAY", "TOMORROW", "WEDNESDAY", "THURSDAY", "FRIDAY", "SATURDAY", "SUNDAY" };
            if (dayIndex >= 0 && dayIndex < dayNames.Length) return dayNames[dayIndex];
            return "DAY +" + dayIndex;
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
                        string mStr = (projectedDate.Month < 10) ? ("0" + projectedDate.Month) : projectedDate.Month.ToString();
                        string dStr = (projectedDate.Day < 10) ? ("0" + projectedDate.Day) : projectedDate.Day.ToString();
                        string projDateStr = projectedDate.Year + "-" + mStr + "-" + dStr;
                        string projDayOfWeek = GetDayOfWeekName(projDateStr, day);

                        var prev = _cachedReading.ForecastDays[day - 1];
                        float maxT = prev.MaxTempC + ((day % 2 == 0) ? -1.5f : 0.8f);
                        float minT = prev.MinTempC + ((day % 2 == 0) ? -1.0f : 0.5f);
                        int rainChance = Math.Max(0, Math.Min(100, prev.ChanceOfRain + ((day % 2 == 0) ? 5 : -3)));
                        string cond = prev.Condition;
                        if (day == 3) cond = "Partly Cloudy";
                        else if (day == 4) cond = "Overcast";
                        else if (day == 5) cond = "Partly Cloudy";
                        else if (day == 6) cond = "Clear Sky";

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

        private static bool IsNumericCoordPair(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            string[] parts = s.Split(',');
            if (parts.Length != 2) return false;
            for (int p = 0; p < 2; p++)
            {
                string t = parts[p].Trim();
                if (t.Length == 0) return false;
                for (int i = 0; i < t.Length; i++)
                {
                    char c = t[i];
                    if (c != '-' && c != '+' && c != '.' && (c < '0' || c > '9')) return false;
                }
            }
            return true;
        }

        public static string ResolveCityQuery(string city, out string normalizedDisplayName)
        {
            string lower = (city ?? "").ToLower().Trim();

            // Clean common suffixes from previous queries or prompts
            if (lower.Contains(","))
            {
                string[] parts = lower.Split(',');
                if (parts.Length == 2)
                {
                    string p1 = parts[1].Trim();
                    if (p1 == "pt" || p1 == "portugal" || p1 == "es" || p1 == "spain" || p1 == "espanha" ||
                        p1 == "fr" || p1 == "france" || p1 == "franca" || p1 == "uk" || p1 == "gb" ||
                        p1 == "united kingdom" || p1 == "de" || p1 == "germany" || p1 == "alemanha" ||
                        p1 == "it" || p1 == "italy" || p1 == "italia" || p1 == "ca" || p1 == "canada" ||
                        p1 == "us" || p1 == "usa" || p1 == "united states" || p1 == "at" || p1 == "austria" ||
                        p1 == "pl" || p1 == "poland" || p1 == "polonia" || p1 == "gr" || p1 == "greece" ||
                        p1 == "grecia" || p1 == "tr" || p1 == "turkey" || p1 == "turquia" || p1 == "ru" ||
                        p1 == "russia" || p1 == "no" || p1 == "norway" || p1 == "noruega" || p1 == "br" ||
                        p1 == "brazil" || p1 == "brasil" || p1 == "ar" || p1 == "argentina" || p1 == "au" ||
                        p1 == "australia" || p1 == "eg" || p1 == "egypt" || p1 == "egito" || p1 == "ma" ||
                        p1 == "morocco" || p1 == "marrocos")
                    {
                        lower = parts[0].Trim();
                    }
                }
            }


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
            if (lower == "almada")
            {
                normalizedDisplayName = "ALMADA, PT";
                return "38.6790,-9.1569";
            }
            if (lower == "barreiro")
            {
                normalizedDisplayName = "BARREIRO, PT";
                return "38.6633,-9.0733";
            }
            if (lower == "seixal")
            {
                normalizedDisplayName = "SEIXAL, PT";
                return "38.6408,-9.1028";
            }
            if (lower == "sesimbra")
            {
                normalizedDisplayName = "SESIMBRA, PT";
                return "38.4442,-9.1017";
            }
            if (lower == "cascais")
            {
                normalizedDisplayName = "CASCAIS, PT";
                return "38.6979,-9.4215";
            }
            if (lower == "sintra")
            {
                normalizedDisplayName = "SINTRA, PT";
                return "38.8029,-9.3817";
            }
            if (lower == "gaia" || lower == "vila nova de gaia")
            {
                normalizedDisplayName = "VILA NOVA DE GAIA, PT";
                return "41.1336,-8.6174";
            }
            if (lower == "matosinhos")
            {
                normalizedDisplayName = "MATOSINHOS, PT";
                return "41.1820,-8.6896";
            }
            if (lower == "guimaraes" || lower == "guimarães")
            {
                normalizedDisplayName = "GUIMARAES, PT";
                return "41.4425,-8.2918";
            }
            if (lower == "viseu")
            {
                normalizedDisplayName = "VISEU, PT";
                return "40.6566,-7.9125";
            }
            if (lower == "portimao" || lower == "portimão")
            {
                normalizedDisplayName = "PORTIMAO, PT";
                return "37.1363,-8.5376";
            }
            if (lower == "lagos")
            {
                normalizedDisplayName = "LAGOS, PT";
                return "37.1028,-8.6730";
            }
            if (lower == "madrid")
            {
                normalizedDisplayName = "MADRID, ES";
                return "40.4168,-3.7038";
            }
            if (lower == "barcelona")
            {
                normalizedDisplayName = "BARCELONA, ES";
                return "41.3879,2.1699";
            }
            if (lower == "paris")
            {
                normalizedDisplayName = "PARIS, FR";
                return "48.8566,2.3522";
            }
            if (lower == "london" || lower == "londres")
            {
                normalizedDisplayName = "LONDON, UK";
                return "51.5074,-0.1278";
            }
            if (lower == "berlin")
            {
                normalizedDisplayName = "BERLIN, DE";
                return "52.5200,13.4050";
            }
            if (lower == "rome" || lower == "roma")
            {
                normalizedDisplayName = "ROMA, IT";
                return "41.9028,12.4964";
            }
            if (lower == "vienna" || lower == "viena")
            {
                normalizedDisplayName = "VIENA, AT";
                return "48.2082,16.3738";
            }
            if (lower == "warsaw" || lower == "varsovia" || lower == "varsóvia")
            {
                normalizedDisplayName = "VARSOVIA, PL";
                return "52.2297,21.0122";
            }
            if (lower == "athens" || lower == "atenas")
            {
                normalizedDisplayName = "ATENAS, GR";
                return "37.9838,23.7275";
            }
            if (lower == "ankara" || lower == "ancara")
            {
                normalizedDisplayName = "ANCARA, TR";
                return "39.9334,32.8597";
            }
            if (lower == "oslo")
            {
                normalizedDisplayName = "OSLO, NO";
                return "59.9139,10.7522";
            }
            if (lower == "moscow" || lower == "moscovo" || lower == "moscou")
            {
                normalizedDisplayName = "MOSCOVO, RU";
                return "55.7558,37.6173";
            }
            if (lower == "tokyo" || lower == "toquio" || lower == "tóquio")
            {
                normalizedDisplayName = "TOQUIO, JP";
                return "35.6762,139.6503";
            }
            if (lower == "beijing" || lower == "pequim")
            {
                normalizedDisplayName = "PEQUIM, CN";
                return "39.9042,116.4074";
            }
            if (lower == "ottawa" || lower == "otava")
            {
                normalizedDisplayName = "OTAVA, CA";
                return "45.4215,-75.6972";
            }
            if (lower == "new york" || lower == "nova iorque")
            {
                normalizedDisplayName = "NOVA IORQUE, US";
                return "40.7128,-74.0060";
            }
            if (lower == "washington")
            {
                normalizedDisplayName = "WASHINGTON, US";
                return "38.9072,-77.0369";
            }
            if (lower == "sydney")
            {
                normalizedDisplayName = "SYDNEY, AU";
                return "-33.8688,151.2093";
            }
            if (lower == "cairo")
            {
                normalizedDisplayName = "CAIRO, EG";
                return "30.0444,31.2357";
            }
            if (lower == "rabat")
            {
                normalizedDisplayName = "RABAT, MA";
                return "34.0209,-6.8416";
            }
            if (lower == "brasilia" || lower == "brasília")
            {
                normalizedDisplayName = "BRASILIA, BR";
                return "-15.7975,-47.8919";
            }
            if (lower == "buenos aires")
            {
                normalizedDisplayName = "BUENOS AIRES, AR";
                return "-34.6037,-58.3816";
            }


            normalizedDisplayName = city.ToUpper();
            return city.Replace(" ", "%20");
        }
    }
}
