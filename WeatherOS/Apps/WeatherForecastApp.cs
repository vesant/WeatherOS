using System;
using Cosmos.System;
using WeatherOS.Graphics;
using WeatherOS.Services;

namespace WeatherOS.Apps
{
    public class WeatherForecastApp : IApp
    {
        public string Id => "forecast_7d";
        public string Name => "7-Day Weather Forecast";
        public bool IsRealTime => false;

        public void Draw(TuiDesktop desktop)
        {
            desktop.DrawWindow(2, 2, 76, 20, "7-DAY EXTENDED WEATHER & LUNAR FORECAST");
            ConsoleColor winBg = desktop.IsDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;

            var data = desktop.WeatherService.GetLatestReading();
            var days = data.ForecastDays;

            if (days == null || days.Length < 7)
            {
                System.Console.SetCursorPosition(6, 6);
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.Write("Awaiting 7-Day Telemetry from WeatherAPI.com...");
                System.Console.SetCursorPosition(6, 8);
                System.Console.ForegroundColor = ConsoleColor.White;
                System.Console.Write("Press [R] to request forecast or [C] to select another city.");
                return;
            }

            // Draw 7 cards across the 76-column window
            for (int i = 0; i < 7; i++)
            {
                var day = days[i];
                int colX = 4 + (i * 10);

                // 1. Day of week & date header
                System.Console.SetCursorPosition(colX, 4);
                if (i == 0)
                {
                    System.Console.ForegroundColor = ConsoleColor.Green;
                    System.Console.Write("  HOJE   ");
                }
                else if (i == 1)
                {
                    System.Console.ForegroundColor = ConsoleColor.Yellow;
                    System.Console.Write(" AMANHA  ");
                }
                else
                {
                    System.Console.ForegroundColor = ConsoleColor.White;
                    string dayHeader = FormatDayHeader(day.Date, day.DayOfWeek);
                    System.Console.Write(dayHeader.PadRight(9));
                }

                // Divider
                System.Console.SetCursorPosition(colX, 5);
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.Write("---------");

                // 2. Mini ASCII Weather Icon (3 lines high, 9 chars wide)
                DrawMiniWeatherIcon(day.Condition, colX, 6);

                // 3. Condition name (shortened)
                System.Console.SetCursorPosition(colX, 9);
                System.Console.ForegroundColor = ConsoleColor.White;
                string condShort = ShortenCondition(day.Condition);
                System.Console.Write(condShort.PadRight(9));

                // 4. Max & Min Temperatures
                System.Console.SetCursorPosition(colX, 10);
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.Write($"Max: {((int)day.MaxTempC)}C".PadRight(9));

                System.Console.SetCursorPosition(colX, 11);
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.Write($"Min: {((int)day.MinTempC)}C".PadRight(9));

                // 5. Rain Probability
                System.Console.SetCursorPosition(colX, 12);
                System.Console.ForegroundColor = day.ChanceOfRain > 40 ? ConsoleColor.Yellow : ConsoleColor.DarkCyan;
                System.Console.Write($"R: {day.ChanceOfRain}%".PadRight(9));

                // Divider
                System.Console.SetCursorPosition(colX, 13);
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.Write("---------");

                // 6. Mini Moon Icon (2 lines high)
                DrawMiniMoonIcon(day.MoonPhase, colX, 14);

                // 7. Moon Phase Name
                System.Console.SetCursorPosition(colX, 16);
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                string phaseShort = ShortenMoonPhase(day.MoonPhase);
                System.Console.Write(phaseShort.PadRight(9));

                // 8. Moon Illumination
                System.Console.SetCursorPosition(colX, 17);
                System.Console.ForegroundColor = ConsoleColor.DarkCyan;
                string illum = !string.IsNullOrEmpty(day.MoonIllumination) ? day.MoonIllumination : "";
                System.Console.Write(illum.PadRight(9));
            }

            // Bottom telemetry status bar
            System.Console.SetCursorPosition(4, 19);
            System.Console.BackgroundColor = ConsoleColor.DarkBlue;
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            string locInfo = $" Location: {data.Location}  |  Source: WeatherAPI.com CDN Telemetry ";
            if (locInfo.Length > 72) locInfo = locInfo.Substring(0, 72);
            System.Console.Write(locInfo.PadRight(72));

            System.Console.SetCursorPosition(4, 20);
            System.Console.BackgroundColor = winBg;
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.Write("[ESC] Return  |  [C] Change City  |  [W] Dashboard  |  [R] Refresh");
        }

        private static string FormatDayHeader(string dateStr, string dayOfWeek)
        {
            if (!string.IsNullOrEmpty(dayOfWeek))
            {
                string d = dayOfWeek.ToUpper();
                if (d.Contains("DOMINGO")) return " DOM ";
                if (d.Contains("SEGUNDA")) return " SEG ";
                if (d.Contains("TERCA"))   return " TER ";
                if (d.Contains("QUARTA"))  return " QUA ";
                if (d.Contains("QUINTA"))  return " QUI ";
                if (d.Contains("SEXTA"))   return " SEX ";
                if (d.Contains("SABADO"))  return " SAB ";
            }
            if (!string.IsNullOrEmpty(dateStr) && dateStr.Length >= 10)
            {
                return " " + dateStr.Substring(5, 5) + " ";
            }
            return " DAY ";
        }

        private static string ShortenCondition(string cond)
        {
            if (string.IsNullOrEmpty(cond)) return "Clear";
            string c = cond.ToLower();
            if (c.Contains("thunder") || c.Contains("trovoada")) return "Thunder";
            if (c.Contains("heavy rain") || c.Contains("chuva forte")) return "Hvy Rain";
            if (c.Contains("rain") || c.Contains("chuva")) return "Rain";
            if (c.Contains("drizzle") || c.Contains("chuvisco")) return "Drizzle";
            if (c.Contains("snow") || c.Contains("neve")) return "Snow";
            if (c.Contains("blizzard")) return "Blizzard";
            if (c.Contains("fog") || c.Contains("mist") || c.Contains("nevoeiro")) return "Fog";
            if (c.Contains("part") || c.Contains("parcial")) return "Part.Cld";
            if (c.Contains("overcast") || c.Contains("coberto")) return "Overcast";
            if (c.Contains("cloud") || c.Contains("nublado")) return "Cloudy";
            if (c.Contains("clear") || c.Contains("limpo")) return "Clear";
            if (c.Contains("sun") || c.Contains("sol")) return "Sunny";
            return cond.Length > 8 ? cond.Substring(0, 8) : cond;
        }

        private static string ShortenMoonPhase(string phase)
        {
            if (string.IsNullOrEmpty(phase)) return "Moon";
            string p = phase.ToLower();
            if (p.Contains("new")) return "New Moon";
            if (p.Contains("waxing crescent")) return "Wax.Cres";
            if (p.Contains("first quarter")) return "1st Qtr";
            if (p.Contains("waxing gibbous")) return "Wax.Gib";
            if (p.Contains("full")) return "Full Moon";
            if (p.Contains("waning gibbous")) return "Wan.Gib";
            if (p.Contains("last quarter") || p.Contains("third")) return "Last Qtr";
            if (p.Contains("waning crescent")) return "Wan.Cres";
            return phase.Length > 8 ? phase.Substring(0, 8) : phase;
        }

        private static void DrawMiniWeatherIcon(string condition, int left, int top)
        {
            string c = (condition ?? "").ToLower();
            if (c.Contains("thunder") || c.Contains("trovoada"))
            {
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("  .-^^-. ");
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("   /_/   ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("    /    ");
            }
            else if (c.Contains("rain") || c.Contains("drizzle") || c.Contains("shower") || c.Contains("chuva"))
            {
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("  .----. ");
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("  / / /  ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("   / /   ");
            }
            else if (c.Contains("snow") || c.Contains("neve") || c.Contains("blizzard"))
            {
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("  .----. ");
                System.Console.ForegroundColor = ConsoleColor.White;
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("  * * *  ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("   * *   ");
            }
            else if (c.Contains("fog") || c.Contains("mist") || c.Contains("nevoeiro"))
            {
                System.Console.ForegroundColor = ConsoleColor.Gray;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("  _ - _  ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("  - _ -  ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("  _ - _  ");
            }
            else if (c.Contains("part") || c.Contains("parcial"))
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("  \\ ( )  ");
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write(" .-(  ). ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("(___.__) ");
            }
            else if (c.Contains("cloud") || c.Contains("overcast") || c.Contains("nublado"))
            {
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("   .--.  ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write(" .-(  ). ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("(___.__) ");
            }
            else
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("  \\ | /  ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write(" --( )-- ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("  / | \\  ");
            }
        }

        private static void DrawMiniMoonIcon(string phase, int left, int top)
        {
            string p = (phase ?? "").ToLower();
            if (p.Contains("new"))
            {
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("  ( . )  ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("  `---'  ");
            }
            else if (p.Contains("waxing crescent"))
            {
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("  (  )   ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("   --)   ");
            }
            else if (p.Contains("first quarter"))
            {
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("  (|##)  ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("  `|##'  ");
            }
            else if (p.Contains("waxing gibbous"))
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("  (###)  ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("  `---'  ");
            }
            else if (p.Contains("full"))
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("  ( O )  ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("  `---'  ");
            }
            else if (p.Contains("waning gibbous"))
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("  (###)  ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("  `---'  ");
            }
            else if (p.Contains("last quarter") || p.Contains("third"))
            {
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("  (##|)  ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("  `##|'  ");
            }
            else
            {
                // Waning Crescent
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("   (  )  ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("   (--   ");
            }
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.R)
            {
                desktop.WeatherService.FetchOpenWeather(desktop.DefaultCity, desktop.ApiKey);
            }
            else if (keyEvent.Key == ConsoleKeyEx.C)
            {
                desktop.SetCurrentApp("change_city");
            }
            else if (keyEvent.Key == ConsoleKeyEx.W)
            {
                desktop.SetCurrentApp("weather");
            }
        }
    }
}
