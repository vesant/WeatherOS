using System;

namespace WeatherOS.Services
{
    public class ForecastDayData
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

            string mPhase, mIllum;
            WeatherService.ComputeCurrentLunarState(out mPhase, out mIllum);
            MoonPhase = mPhase;
            MoonIllumination = mIllum;

            int cy, cm, cd, hour, cmin, csec;
            TimeService.GetCurrentLocalTime(out cy, out cm, out cd, out hour, out cmin, out csec);
            IsNight = (hour < 6 || hour >= 20);
            Timestamp = default;

            ForecastDays = WeatherService.BuildSynoptic7DayForecast(temp, condition, loc);
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

        public static void ComputeCurrentLunarState(out string phaseName, out string illuminationStr)
        {
            long nowEpoch = TimeService.GetCurrentUtcEpochSeconds();
            long refNewMoonEpoch = 1704974220L; // 2024-01-11 11:57:00 UTC
            long diffSec = nowEpoch - refNewMoonEpoch;
            if (diffSec < 0) diffSec = 0;
            long cycleSec = diffSec % 2551443L; // 29.53059 days in seconds
            float synodicDays = (float)cycleSec / 86400.0f;

            float halfCycle = 14.7653f;
            float distFromFull = synodicDays - halfCycle;
            if (distFromFull < 0f) distFromFull = -distFromFull;
            int illumPct = (int)(100.0f * (1.0f - (distFromFull / halfCycle)));
            if (illumPct < 0) illumPct = 0;
            if (illumPct > 100) illumPct = 100;
            illuminationStr = illumPct.ToString() + "%";

            if (synodicDays < 1.84f) phaseName = "New Moon";
            else if (synodicDays < 5.53f) phaseName = "Waxing Crescent";
            else if (synodicDays < 9.22f) phaseName = "First Quarter";
            else if (synodicDays < 12.91f) phaseName = "Waxing Gibbous";
            else if (synodicDays < 16.61f) phaseName = "Full Moon";
            else if (synodicDays < 20.30f) phaseName = "Waning Gibbous";
            else if (synodicDays < 23.99f) phaseName = "Last Quarter";
            else if (synodicDays < 27.68f) phaseName = "Waning Crescent";
            else phaseName = "New Moon";
        }

        public static ForecastDayData[] BuildSynoptic7DayForecast(float baseTemp, string baseCondition, string loc)
        {
            var days = new ForecastDayData[7];

            string cleanLoc = string.IsNullOrWhiteSpace(loc) ? "SETUBAL, PT" : loc.ToUpper();
            string startCond = baseCondition;
            float startTemp = baseTemp;

            if (string.IsNullOrWhiteSpace(startCond) || startCond.Contains("Awaiting") || startCond.Contains("Offline"))
            {
                float bT, bH, bP, bW;
                string bDir, bCond;
                if (GetRegionalSynopticBaseline(cleanLoc, out bT, out bH, out bP, out bW, out bDir, out bCond))
                {
                    startCond = bCond;
                    if (startTemp <= 0.1f) startTemp = bT;
                }
                else
                {
                    startCond = "Partly Cloudy";
                    if (startTemp <= 0.1f) startTemp = 16.0f;
                }
            }

            int citySeed = 0;
            for (int k = 0; k < cleanLoc.Length; k++)
            {
                citySeed = (citySeed * 31 + cleanLoc[k]) & 0x7FFF;
            }

            string cLower = startCond.ToLower();
            string[] progConds;
            int[] tempDeltas;

            if (cLower.Contains("thunder") || cLower.Contains("trovoada"))
            {
                progConds = new string[] {
                    "Thunderstorm", "Moderate Rain", "Partly Cloudy", "Clear Sky",
                    "Partly Cloudy", "Light Rain", "Mainly Clear"
                };
                tempDeltas = new int[] { 0, -2, 1, 2, 1, -1, 2 };
            }
            else if (cLower.Contains("rain") || cLower.Contains("shower") || cLower.Contains("drizzle"))
            {
                progConds = new string[] {
                    startCond, "Partly Cloudy", "Clear Sky", "Mainly Clear",
                    "Cloudy", "Light Rain", "Partly Cloudy"
                };
                tempDeltas = new int[] { 0, 1, 3, 2, 0, -1, 1 };
            }
            else if (cLower.Contains("snow") || cLower.Contains("blizzard"))
            {
                progConds = new string[] {
                    startCond, "Cloudy", "Partly Cloudy", "Clear Sky",
                    "Partly Cloudy", "Slight Snow Fall", "Mainly Clear"
                };
                tempDeltas = new int[] { 0, 1, 2, 1, 0, -2, 1 };
            }
            else if (cLower.Contains("cloud") || cLower.Contains("overcast") || cLower.Contains("fog") || cLower.Contains("mist"))
            {
                progConds = new string[] {
                    startCond, "Partly Cloudy", "Clear Sky", "Mainly Clear",
                    "Partly Cloudy", "Light Rain", "Clear Sky"
                };
                tempDeltas = new int[] { 0, 1, 2, 2, 0, -2, 1 };
            }
            else
            {
                progConds = new string[] {
                    "Clear Sky", "Mainly Clear", "Partly Cloudy", "Clear Sky",
                    "Partly Cloudy", "Cloudy", "Clear Sky"
                };
                tempDeltas = new int[] { 0, 1, 0, 2, -1, -2, 1 };
            }

            int y, m, d, h, min, sec;
            TimeService.GetCurrentLocalTime(out y, out m, out d, out h, out min, out sec);

            string curPhase, curIllum;
            ComputeCurrentLunarState(out curPhase, out curIllum);

            for (int i = 0; i < 7; i++)
            {
                string mStr = (m < 10) ? ("0" + m) : m.ToString();
                string dStr = (d < 10) ? ("0" + d) : d.ToString();
                string dateStr = y.ToString() + "-" + mStr + "-" + dStr;

                string dowName;
                if (i == 0) dowName = "TODAY";
                else if (i == 1) dowName = "TOMORROW";
                else dowName = TimeService.GetDayOfWeekName(y, m, d).ToUpper();

                int idx = (i == 0) ? 0 : (1 + ((i - 1 + (citySeed % 3)) % 6));
                string dCond = (i == 0) ? startCond : progConds[idx];

                float dMax = startTemp + tempDeltas[idx] + ((i == 0) ? 1.0f : ((citySeed + i) % 2));
                float spread = 5.0f + ((citySeed + i) % 4);
                float dMin = dMax - spread;

                int dRain = EstimateRainProbability(dCond, i, citySeed);

                days[i] = new ForecastDayData
                {
                    Date = dateStr,
                    DayOfWeek = dowName,
                    MaxTempC = dMax,
                    MinTempC = dMin,
                    ChanceOfRain = dRain,
                    Condition = dCond,
                    MoonPhase = curPhase,
                    MoonIllumination = curIllum
                };

                AdvanceMoonPhase(ref curPhase, ref curIllum);

                d++;
                int maxDays = TimeService.GetDaysInMonth(y, m);
                if (d > maxDays)
                {
                    d = 1;
                    m++;
                    if (m > 12) { m = 1; y++; }
                }
            }

            return days;
        }

        private static int EstimateRainProbability(string cond, int dayIdx, int seed)
        {
            string c = (cond ?? "").ToLower();
            int jitter = ((seed + dayIdx * 7) % 11) - 5; // -5..+5%
            int baseProb = 10;
            if (c.Contains("thunder")) baseProb = 80;
            else if (c.Contains("heavy rain") || c.Contains("violent")) baseProb = 85;
            else if (c.Contains("moderate rain") || c == "rain") baseProb = 65;
            else if (c.Contains("light rain") || c.Contains("slight rain") || c.Contains("shower") || c.Contains("drizzle")) baseProb = 50;
            else if (c.Contains("snow") || c.Contains("blizzard")) baseProb = 60;
            else if (c.Contains("overcast")) baseProb = 30;
            else if (c.Contains("cloud") && !c.Contains("part")) baseProb = 25;
            else if (c.Contains("part")) baseProb = 15;
            else if (c.Contains("mainly clear")) baseProb = 10;
            else baseProb = 5;

            int res = baseProb + jitter;
            if (res < 5) res = 5;
            if (res > 95) res = 95;
            return (res / 5) * 5;
        }

        public void EnsureForecastReady(string defaultCity, string apiKey)
        {
            string target = (!string.IsNullOrWhiteSpace(_cachedReading.Location) && _cachedReading.Location != "UNKNOWN")
                ? _cachedReading.Location
                : defaultCity;

            bool needsFetch = (_cachedReading.Condition != null && _cachedReading.Condition.Contains("Awaiting")) ||
                              _cachedReading.ForecastDays == null ||
                              _cachedReading.ForecastDays.Length < 7 ||
                              _cachedReading.ForecastDays[0] == null ||
                              string.IsNullOrEmpty(_cachedReading.ForecastDays[0].Date) ||
                              (_cachedReading.ForecastDays[0].Condition != null && _cachedReading.ForecastDays[0].Condition.Contains("Awaiting"));

            if (needsFetch)
            {
                FetchOpenWeather(target, apiKey);
            }

            if (_cachedReading.ForecastDays == null ||
                _cachedReading.ForecastDays.Length < 7 ||
                _cachedReading.ForecastDays[0] == null ||
                (_cachedReading.ForecastDays[0].Condition != null && _cachedReading.ForecastDays[0].Condition.Contains("Awaiting")))
            {
                _cachedReading.ForecastDays = BuildSynoptic7DayForecast(
                    _cachedReading.TemperatureCelsius,
                    _cachedReading.Condition,
                    _cachedReading.Location);
            }
        }

        public void ApplySynopticFallback(string displayLocation, float tempC, float humidity, float pressureHpa, float windKmh, string windDir, string condition)
        {
            _cachedReading = new WeatherData(
                temp: tempC,
                humidity: humidity,
                pressure: pressureHpa,
                windSpeed: windKmh,
                windDir: string.IsNullOrWhiteSpace(windDir) ? "NW" : windDir,
                condition: string.IsNullOrWhiteSpace(condition) ? "Clear" : condition,
                loc: string.IsNullOrWhiteSpace(displayLocation) ? "LISBON, PT" : displayLocation.ToUpper()
            );
            EvaluateWeatherAlerts();
        }

        public static bool GetRegionalSynopticBaseline(string cityOrDisplay, out float tempC, out float humidity, out float pressure, out float windKmh, out string windDir, out string condition)
        {
            string s = (cityOrDisplay ?? "").ToUpper();
            tempC = 15f; humidity = 67f; pressure = 1018f; windKmh = 10f; windDir = "NW"; condition = "Clear";

            if (s.Contains("SETUBAL") || s.Contains("ALMADA") || s.Contains("BARREIRO") || s.Contains("SEIXAL") || s.Contains("SESIMBRA"))
            { tempC = 16f; humidity = 65f; pressure = 1018f; windKmh = 12f; windDir = "NW"; condition = "Clear"; return true; }
            if (s.Contains("LISBON") || s.Contains("LISBOA") || s.Contains("CASCAIS") || s.Contains("SINTRA") || s.Contains("38.72"))
            { tempC = 15f; humidity = 67f; pressure = 1018f; windKmh = 10f; windDir = "NW"; condition = "Clear"; return true; }
            if (s.Contains("PORTO") || s.Contains("BRAGA") || s.Contains("GAIA") || s.Contains("MATOSINHOS") || s.Contains("GUIMARAES"))
            { tempC = 14f; humidity = 74f; pressure = 1016f; windKmh = 14f; windDir = "WNW"; condition = "Partly Cloudy"; return true; }
            if (s.Contains("FARO") || s.Contains("PORTIMAO") || s.Contains("LAGOS") || s.Contains("BEJA") || s.Contains("EVORA"))
            { tempC = 18f; humidity = 58f; pressure = 1019f; windKmh = 11f; windDir = "W"; condition = "Clear"; return true; }
            if (s.Contains("MADRID") || s.Contains("BARCELONA") || s.Contains("40.42"))
            { tempC = 16f; humidity = 49f; pressure = 1019f; windKmh = 11f; windDir = "NE"; condition = "Clear"; return true; }
            if (s.Contains("LONDON") || s.Contains("LONDRES") || s.Contains("51.51"))
            { tempC = 14f; humidity = 76f; pressure = 1014f; windKmh = 18f; windDir = "SW"; condition = "Partly Cloudy"; return true; }
            if (s.Contains("PARIS") || s.Contains("48.86"))
            { tempC = 13f; humidity = 67f; pressure = 1016f; windKmh = 12f; windDir = "W"; condition = "Partly Cloudy"; return true; }
            if (s.Contains("BERLIN") || s.Contains("52.52"))
            { tempC = 9f; humidity = 91f; pressure = 1011f; windKmh = 15f; windDir = "WNW"; condition = "Cloudy"; return true; }
            if (s.Contains("OSLO") || s.Contains("59.91"))
            { tempC = 6f; humidity = 88f; pressure = 1013f; windKmh = 14f; windDir = "N"; condition = "Cloudy"; return true; }
            if (s.Contains("ROME") || s.Contains("ROMA") || s.Contains("41.90"))
            { tempC = 14f; humidity = 76f; pressure = 1017f; windKmh = 10f; windDir = "NW"; condition = "Clear"; return true; }
            if (s.Contains("VIENNA") || s.Contains("VIENA") || s.Contains("48.21"))
            { tempC = 11f; humidity = 79f; pressure = 1015f; windKmh = 12f; windDir = "W"; condition = "Partly Cloudy"; return true; }
            if (s.Contains("WARSAW") || s.Contains("VARSOVIA") || s.Contains("52.23"))
            { tempC = 10f; humidity = 92f; pressure = 1012f; windKmh = 14f; windDir = "W"; condition = "Cloudy"; return true; }
            if (s.Contains("ATHENS") || s.Contains("ATENAS") || s.Contains("37.98"))
            { tempC = 15f; humidity = 80f; pressure = 1018f; windKmh = 12f; windDir = "NNE"; condition = "Partly Cloudy"; return true; }
            if (s.Contains("ANKARA") || s.Contains("ANCARA") || s.Contains("39.93"))
            { tempC = 11f; humidity = 93f; pressure = 1016f; windKmh = 11f; windDir = "NE"; condition = "Partly Cloudy"; return true; }
            if (s.Contains("MOSCOW") || s.Contains("MOSCOVO") || s.Contains("55.75"))
            { tempC = 5f; humidity = 53f; pressure = 1009f; windKmh = 16f; windDir = "NW"; condition = "Partly Cloudy"; return true; }
            if (s.Contains("RABAT") || s.Contains("34.02"))
            { tempC = 19f; humidity = 81f; pressure = 1019f; windKmh = 12f; windDir = "N"; condition = "Partly Cloudy"; return true; }
            if (s.Contains("ALGIERS") || s.Contains("ARGEL") || s.Contains("36.75"))
            { tempC = 20f; humidity = 60f; pressure = 1018f; windKmh = 11f; windDir = "NE"; condition = "Clear"; return true; }
            if (s.Contains("CAIRO") || s.Contains("30.04"))
            { tempC = 23f; humidity = 50f; pressure = 1017f; windKmh = 14f; windDir = "N"; condition = "Clear"; return true; }
            if (s.Contains("RIYADH") || s.Contains("24.71"))
            { tempC = 26f; humidity = 22f; pressure = 1015f; windKmh = 14f; windDir = "NNE"; condition = "Clear"; return true; }
            if (s.Contains("DELHI") || s.Contains("28.61"))
            { tempC = 27f; humidity = 48f; pressure = 1014f; windKmh = 10f; windDir = "WNW"; condition = "Clear"; return true; }
            if (s.Contains("BEIJING") || s.Contains("PEQUIM") || s.Contains("39.90"))
            { tempC = 12f; humidity = 45f; pressure = 1018f; windKmh = 15f; windDir = "NNW"; condition = "Partly Cloudy"; return true; }
            if (s.Contains("BANGKOK") || s.Contains("13.76"))
            { tempC = 29f; humidity = 78f; pressure = 1011f; windKmh = 12f; windDir = "SW"; condition = "Thunderstorm"; return true; }
            if (s.Contains("SINGAPORE") || s.Contains("1.35"))
            { tempC = 28f; humidity = 82f; pressure = 1010f; windKmh = 14f; windDir = "SE"; condition = "Rain"; return true; }
            if (s.Contains("JAKARTA") || s.Contains("-6.21"))
            { tempC = 29f; humidity = 79f; pressure = 1010f; windKmh = 12f; windDir = "E"; condition = "Rain"; return true; }
            if (s.Contains("SEOUL") || s.Contains("37.57"))
            { tempC = 11f; humidity = 58f; pressure = 1019f; windKmh = 12f; windDir = "NW"; condition = "Clear"; return true; }
            if (s.Contains("TOKYO") || s.Contains("TOQUIO") || s.Contains("35.68"))
            { tempC = 15f; humidity = 60f; pressure = 1020f; windKmh = 14f; windDir = "NNE"; condition = "Clear"; return true; }
            if (s.Contains("OTTAWA") || s.Contains("OTAVA") || s.Contains("45.42"))
            { tempC = 9f; humidity = 68f; pressure = 1016f; windKmh = 16f; windDir = "W"; condition = "Cloudy"; return true; }
            if (s.Contains("NEW YORK") || s.Contains("NOVA IORQUE") || s.Contains("40.71"))
            { tempC = 16f; humidity = 59f; pressure = 1015f; windKmh = 14f; windDir = "NW"; condition = "Partly Cloudy"; return true; }
            if (s.Contains("WASHINGTON") || s.Contains("38.91"))
            { tempC = 17f; humidity = 61f; pressure = 1016f; windKmh = 12f; windDir = "NW"; condition = "Partly Cloudy"; return true; }
            if (s.Contains("MEXICO") || s.Contains("19.43"))
            { tempC = 21f; humidity = 52f; pressure = 1018f; windKmh = 10f; windDir = "NE"; condition = "Clear"; return true; }
            if (s.Contains("HAVANA") || s.Contains("23.11"))
            { tempC = 27f; humidity = 76f; pressure = 1014f; windKmh = 18f; windDir = "ENE"; condition = "Rain"; return true; }
            if (s.Contains("BOGOTA") || s.Contains("4.71"))
            { tempC = 16f; humidity = 81f; pressure = 1015f; windKmh = 10f; windDir = "E"; condition = "Rain"; return true; }
            if (s.Contains("LIMA") || s.Contains("-12.05"))
            { tempC = 19f; humidity = 78f; pressure = 1014f; windKmh = 12f; windDir = "S"; condition = "Cloudy"; return true; }
            if (s.Contains("BRASILIA") || s.Contains("-15.79"))
            { tempC = 25f; humidity = 64f; pressure = 1015f; windKmh = 12f; windDir = "E"; condition = "Thunderstorm"; return true; }
            if (s.Contains("SANTIAGO") || s.Contains("-33.45"))
            { tempC = 22f; humidity = 55f; pressure = 1016f; windKmh = 11f; windDir = "SW"; condition = "Clear"; return true; }
            if (s.Contains("BUENOS AIRES") || s.Contains("-34.60"))
            { tempC = 20f; humidity = 67f; pressure = 1016f; windKmh = 16f; windDir = "E"; condition = "Partly Cloudy"; return true; }
            if (s.Contains("PORT MORESBY") || s.Contains("-9.44"))
            { tempC = 29f; humidity = 82f; pressure = 1011f; windKmh = 15f; windDir = "SE"; condition = "Thunderstorm"; return true; }
            if (s.Contains("SYDNEY") || s.Contains("-33.87"))
            { tempC = 18f; humidity = 65f; pressure = 1020f; windKmh = 16f; windDir = "ENE"; condition = "Partly Cloudy"; return true; }
            if (s.Contains("CANBERRA") || s.Contains("-35.28"))
            { tempC = 12f; humidity = 91f; pressure = 1022f; windKmh = 12f; windDir = "NW"; condition = "Clear"; return true; }
            if (s.Contains("MELBOURNE") || s.Contains("-37.81"))
            { tempC = 13f; humidity = 72f; pressure = 1021f; windKmh = 15f; windDir = "SW"; condition = "Cloudy"; return true; }
            if (s.Contains("SUVA") || s.Contains("-18.14"))
            { tempC = 26f; humidity = 78f; pressure = 1013f; windKmh = 18f; windDir = "ESE"; condition = "Rain"; return true; }
            if (s.Contains("WELLINGTON") || s.Contains("-41.29"))
            { tempC = 14f; humidity = 76f; pressure = 1014f; windKmh = 22f; windDir = "NNW"; condition = "Cloudy"; return true; }
            if (s.Contains("DAKAR") || s.Contains("14.69"))
            { tempC = 29f; humidity = 74f; pressure = 1012f; windKmh = 18f; windDir = "NNW"; condition = "Partly Cloudy"; return true; }
            if (s.Contains("ABUJA") || s.Contains("9.08"))
            { tempC = 28f; humidity = 78f; pressure = 1012f; windKmh = 10f; windDir = "SW"; condition = "Thunderstorm"; return true; }
            if (s.Contains("ADDIS ABABA") || s.Contains("9.03"))
            { tempC = 19f; humidity = 58f; pressure = 1018f; windKmh = 12f; windDir = "E"; condition = "Partly Cloudy"; return true; }
            if (s.Contains("NAIROBI") || s.Contains("-1.29"))
            { tempC = 22f; humidity = 62f; pressure = 1016f; windKmh = 11f; windDir = "ENE"; condition = "Cloudy"; return true; }
            if (s.Contains("LUANDA") || s.Contains("-8.84"))
            { tempC = 26f; humidity = 77f; pressure = 1014f; windKmh = 14f; windDir = "SW"; condition = "Partly Cloudy"; return true; }
            if (s.Contains("CAPE TOWN") || s.Contains("-33.92"))
            { tempC = 18f; humidity = 65f; pressure = 1017f; windKmh = 16f; windDir = "SSE"; condition = "Clear"; return true; }

            return false;
        }

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

                string req = "GET /v1/forecast?latitude=" + sLat + "&longitude=" + sLon + "&current=temperature_2m,relative_humidity_2m,weather_code,wind_speed_10m&timezone=auto HTTP/1.0\r\nHost: api.open-meteo.com\r\nConnection: close\r\n\r\n";
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
                        string openMeteoRequest = "GET /v1/forecast?latitude=" + lat + "&longitude=" + lon + "&current=temperature_2m,relative_humidity_2m,precipitation,weather_code,surface_pressure,wind_speed_10m,wind_direction_10m&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max&timezone=auto HTTP/1.0\r\nHost: api.open-meteo.com\r\nConnection: close\r\n\r\n";
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

                // 2. Primary WeatherAPI Anycast CDN (143.244.56.52) fallback (use current.json ~880B so single-segment TCP response never overflows RX ring!)
                if (string.IsNullOrWhiteSpace(response))
                {
                    string effectiveKey = !string.IsNullOrWhiteSpace(apiKey) ? apiKey : "bc0cfac1c3144fc3bf0145134260810";
                    string weatherApiRequest = "GET /v1/current.json?key=" + effectiveKey + "&q=" + safeCity + " HTTP/1.1\r\nHost: api.weatherapi.com\r\nUser-Agent: WeatherOS/3.0\r\nConnection: close\r\n\r\n";

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
                        _cachedReading.Location = (!string.IsNullOrWhiteSpace(displayLocation) ? displayLocation : cleanCity.ToUpper()) + " (NOT FOUND)";
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
                        ParseWeatherApiJson(response, displayLocation);
                        if (_debugMode && !WeatherOS.Graphics.TuiDesktop.IsGuiActive)
                            System.Console.WriteLine("[net] raw_tcp: payload received successfully. (status: 200 OK)");
                    }
                    else
                    {
                        float fbT, fbH, fbP, fbW; string fbDir, fbCond;
                        GetRegionalSynopticBaseline(displayLocation, out fbT, out fbH, out fbP, out fbW, out fbDir, out fbCond);
                        ApplySynopticFallback(displayLocation, fbT, fbH, fbP, fbW, fbDir, fbCond);
                        if (_debugMode && !WeatherOS.Graphics.TuiDesktop.IsGuiActive)
                            System.Console.WriteLine("[net] raw_tcp: HTTP error or malformed response; using synoptic baseline.");
                    }
                }
                else
                {
                    float fbT, fbH, fbP, fbW; string fbDir, fbCond;
                    GetRegionalSynopticBaseline(displayLocation, out fbT, out fbH, out fbP, out fbW, out fbDir, out fbCond);
                    ApplySynopticFallback(displayLocation, fbT, fbH, fbP, fbW, fbDir, fbCond);
                    if (!WeatherOS.Graphics.TuiDesktop.IsGuiActive)
                        System.Console.WriteLine("[net] Telemetry fetch timed out; using regional synoptic baseline.");
                }
            }
            catch (Exception ex)
            {
                string fbDisp;
                ResolveCityQuery(cleanCity, out fbDisp);
                float fbT, fbH, fbP, fbW; string fbDir, fbCond;
                GetRegionalSynopticBaseline(fbDisp, out fbT, out fbH, out fbP, out fbW, out fbDir, out fbCond);
                ApplySynopticFallback(fbDisp, fbT, fbH, fbP, fbW, fbDir, fbCond);
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
                    int cy, cm, cd, curHour, cmin, csec;
                    TimeService.GetCurrentLocalTime(out cy, out cm, out cd, out curHour, out cmin, out csec);
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
                        if (!displayCity.Contains(",") && !displayCity.ToUpper().EndsWith(" PT"))
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

                int cy, cm, cd, curHour, cmin, csec;
                TimeService.GetCurrentLocalTime(out cy, out cm, out cd, out curHour, out cmin, out csec);
                _cachedReading.IsNight = (curHour < 7 || curHour >= 20);

                // Pure integer/float lunar phase & illumination calculation
                string calcPhase, calcIllum;
                ComputeCurrentLunarState(out calcPhase, out calcIllum);
                _cachedReading.MoonPhase = calcPhase;
                _cachedReading.MoonIllumination = calcIllum;

                // Parse 7-day daily forecast arrays directly from Open-Meteo starting at "daily":
                int dailyIdx = CustomIndexOf(json, "\"daily\":", 0);
                bool parsedDailyOk = false;
                if (dailyIdx != -1)
                {
                    string[] dates = ParseJsonStringArray(json, "time", 7, dailyIdx);
                    float[] wCodes = ParseJsonFloatArray(json, "weather_code", 7, dailyIdx);
                    float[] maxTemps = ParseJsonFloatArray(json, "temperature_2m_max", 7, dailyIdx);
                    float[] minTemps = ParseJsonFloatArray(json, "temperature_2m_min", 7, dailyIdx);
                    float[] rainProbs = ParseJsonFloatArray(json, "precipitation_probability_max", 7, dailyIdx);

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
                        parsedDailyOk = true;
                    }
                }

                if (!parsedDailyOk)
                {
                    _cachedReading.ForecastDays = BuildSynoptic7DayForecast(
                        _cachedReading.TemperatureCelsius,
                        _cachedReading.Condition,
                        _cachedReading.Location);
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

            _cachedReading.Timestamp = default;
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

        private string[] ParseJsonStringArray(string json, string key, int maxCount, int startIndex = 0)
        {
            var list = new System.Collections.Generic.List<string>();
            string search = "\"" + key + "\":[";
            int idx = CustomIndexOf(json, search, startIndex);
            if (idx == -1)
            {
                search = "\"" + key + "\": [";
                idx = CustomIndexOf(json, search, startIndex);
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

        private float[] ParseJsonFloatArray(string json, string key, int maxCount, int startIndex = 0)
        {
            var list = new System.Collections.Generic.List<float>();
            string search = "\"" + key + "\":[";
            int idx = CustomIndexOf(json, search, startIndex);
            if (idx == -1)
            {
                search = "\"" + key + "\": [";
                idx = CustomIndexOf(json, search, startIndex);
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
            if (!string.IsNullOrEmpty(dateStr) && dateStr.Length >= 10)
            {
                int y, m, d;
                if (int.TryParse(dateStr.Substring(0, 4), out y) &&
                    int.TryParse(dateStr.Substring(5, 2), out m) &&
                    int.TryParse(dateStr.Substring(8, 2), out d))
                {
                    return TimeService.GetDayOfWeekName(y, m, d).ToUpper();
                }
            }
            string[] dayNames = new string[] { "TODAY", "TOMORROW", "WEDNESDAY", "THURSDAY", "FRIDAY", "SATURDAY", "SUNDAY" };
            if (dayIndex >= 0 && dayIndex < dayNames.Length) return dayNames[dayIndex];
            return "DAY +" + dayIndex;
        }

        private void Parse7DayForecast(string json)
        {
            try
            {
                int fcIdx = CustomIndexOf(json, "\"forecastday\":", 0);
                if (fcIdx == -1) fcIdx = CustomIndexOf(json, "\"weather\":", 0);
                if (fcIdx == -1)
                {
                    // current.json has no forecastday array; generate synoptic 7-day progression from live reading
                    _cachedReading.ForecastDays = BuildSynoptic7DayForecast(
                        _cachedReading.TemperatureCelsius,
                        _cachedReading.Condition,
                        _cachedReading.Location);
                    return;
                }

                if (_cachedReading.ForecastDays == null || _cachedReading.ForecastDays.Length != 7)
                {
                    _cachedReading.ForecastDays = new ForecastDayData[7];
                }

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
            _cachedReading.Timestamp = default;
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
            if (lower == "lisbon" || lower == "lisboa" || lower.StartsWith("38.72"))
            {
                normalizedDisplayName = "LISBON, PT";
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
                normalizedDisplayName = "ROME, IT";
                return "41.9028,12.4964";
            }
            if (lower == "vienna" || lower == "viena")
            {
                normalizedDisplayName = "VIENNA, AT";
                return "48.2082,16.3738";
            }
            if (lower == "warsaw" || lower == "varsovia" || lower == "varsóvia")
            {
                normalizedDisplayName = "WARSAW, PL";
                return "52.2297,21.0122";
            }
            if (lower == "athens" || lower == "atenas")
            {
                normalizedDisplayName = "ATHENS, GR";
                return "37.9838,23.7275";
            }
            if (lower == "ankara" || lower == "ancara")
            {
                normalizedDisplayName = "ANKARA, TR";
                return "39.9334,32.8597";
            }
            if (lower == "oslo")
            {
                normalizedDisplayName = "OSLO, NO";
                return "59.9139,10.7522";
            }
            if (lower == "moscow" || lower == "moscovo" || lower == "moscou")
            {
                normalizedDisplayName = "MOSCOW, RU";
                return "55.7558,37.6173";
            }
            if (lower == "tokyo" || lower == "toquio" || lower == "tóquio")
            {
                normalizedDisplayName = "TOKYO, JP";
                return "35.6762,139.6503";
            }
            if (lower == "beijing" || lower == "pequim")
            {
                normalizedDisplayName = "BEIJING, CN";
                return "39.9042,116.4074";
            }
            if (lower == "ottawa" || lower == "otava")
            {
                normalizedDisplayName = "OTTAWA, CA";
                return "45.4215,-75.6972";
            }
            if (lower == "new york" || lower == "nova iorque")
            {
                normalizedDisplayName = "NEW YORK, US";
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
            if (lower == "algiers" || lower == "argel")
            {
                normalizedDisplayName = "ALGIERS, DZ";
                return "36.7538,3.0588";
            }
            if (lower == "riyadh")
            {
                normalizedDisplayName = "RIYADH, SA";
                return "24.7136,46.6753";
            }
            if (lower == "new delhi" || lower == "delhi")
            {
                normalizedDisplayName = "NEW DELHI, IN";
                return "28.6139,77.2090";
            }
            if (lower == "bangkok")
            {
                normalizedDisplayName = "BANGKOK, TH";
                return "13.7563,100.5018";
            }
            if (lower == "singapore")
            {
                normalizedDisplayName = "SINGAPORE, SG";
                return "1.3521,103.8198";
            }
            if (lower == "jakarta")
            {
                normalizedDisplayName = "JAKARTA, ID";
                return "-6.2088,106.8456";
            }
            if (lower == "seoul")
            {
                normalizedDisplayName = "SEOUL, KR";
                return "37.5665,126.9780";
            }
            if (lower == "mexico city" || lower == "mexico")
            {
                normalizedDisplayName = "MEXICO CITY, MX";
                return "19.4326,-99.1332";
            }
            if (lower == "havana")
            {
                normalizedDisplayName = "HAVANA, CU";
                return "23.1136,-82.3666";
            }
            if (lower == "bogota")
            {
                normalizedDisplayName = "BOGOTA, CO";
                return "4.7110,-74.0721";
            }
            if (lower == "lima")
            {
                normalizedDisplayName = "LIMA, PE";
                return "-12.0464,-77.0428";
            }
            if (lower == "santiago")
            {
                normalizedDisplayName = "SANTIAGO, CL";
                return "-33.4489,-70.6693";
            }
            if (lower == "port moresby")
            {
                normalizedDisplayName = "PORT MORESBY, PG";
                return "-9.4438,147.1803";
            }
            if (lower == "canberra")
            {
                normalizedDisplayName = "CANBERRA, AU";
                return "-35.2809,149.1300";
            }
            if (lower == "melbourne")
            {
                normalizedDisplayName = "MELBOURNE, AU";
                return "-37.8136,144.9631";
            }
            if (lower == "suva")
            {
                normalizedDisplayName = "SUVA, FJ";
                return "-18.1416,178.4419";
            }
            if (lower == "wellington")
            {
                normalizedDisplayName = "WELLINGTON, NZ";
                return "-41.2865,174.7762";
            }
            if (lower == "dakar")
            {
                normalizedDisplayName = "DAKAR, SN";
                return "14.6928,-17.4467";
            }
            if (lower == "abuja")
            {
                normalizedDisplayName = "ABUJA, NG";
                return "9.0765,7.3986";
            }
            if (lower == "addis ababa")
            {
                normalizedDisplayName = "ADDIS ABABA, ET";
                return "9.0320,38.7469";
            }
            if (lower == "nairobi")
            {
                normalizedDisplayName = "NAIROBI, KE";
                return "-1.2921,36.8219";
            }
            if (lower == "luanda")
            {
                normalizedDisplayName = "LUANDA, AO";
                return "-8.8390,13.2894";
            }
            if (lower == "cape town")
            {
                normalizedDisplayName = "CAPE TOWN, ZA";
                return "-33.9249,18.4241";
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
