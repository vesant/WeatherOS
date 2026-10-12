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
            desktop.DrawWindow(2, 2, 76, 20, "7-DAY EXTENDED WEATHER FORECAST");
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
                    System.Console.Write("  TODAY  ");
                }
                else if (i == 1)
                {
                    System.Console.ForegroundColor = ConsoleColor.Yellow;
                    System.Console.Write(" TOMORROW");
                }
                else
                {
                    System.Console.ForegroundColor = ConsoleColor.White;
                    string dayHeader = FormatDayHeader(day.Date, day.DayOfWeek);
                    System.Console.Write(dayHeader.PadRight(9));
                }

                // Date line (e.g. "  12/10  ")
                System.Console.SetCursorPosition(colX, 5);
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                string dateStr = FormatDateLabel(day.Date);
                System.Console.Write(dateStr.PadRight(9));

                // Divider
                System.Console.SetCursorPosition(colX, 6);
                System.Console.ForegroundColor = ConsoleColor.DarkCyan;
                System.Console.Write("---------");

                // 2. Mini ASCII Weather Icon (3 lines high: y = 7, 8, 9)
                DrawMiniWeatherIcon(day.Condition, colX, 7);

                // 3. Condition name (shortened)
                System.Console.SetCursorPosition(colX, 10);
                System.Console.ForegroundColor = ConsoleColor.White;
                string condShort = ShortenCondition(day.Condition);
                System.Console.Write(condShort.PadRight(9));

                // Divider
                System.Console.SetCursorPosition(colX, 11);
                System.Console.ForegroundColor = ConsoleColor.DarkCyan;
                System.Console.Write("---------");

                // 4. Max & Min Temperatures
                System.Console.SetCursorPosition(colX, 12);
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.Write($"Max: {((int)day.MaxTempC)} C".PadRight(9));

                System.Console.SetCursorPosition(colX, 13);
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.Write($"Min: {((int)day.MinTempC)} C".PadRight(9));

                // 5. Rain Probability
                System.Console.SetCursorPosition(colX, 14);
                System.Console.ForegroundColor = day.ChanceOfRain > 40 ? ConsoleColor.Yellow : ConsoleColor.White;
                System.Console.Write($"Rain:{day.ChanceOfRain,3}%".PadRight(9));

                // 6. Precipitation outlook / summary badge
                System.Console.SetCursorPosition(colX, 15);
                if (day.ChanceOfRain >= 60)
                {
                    System.Console.ForegroundColor = ConsoleColor.Cyan;
                    System.Console.Write(" [WET]   ");
                }
                else if (day.MaxTempC >= 25)
                {
                    System.Console.ForegroundColor = ConsoleColor.Yellow;
                    System.Console.Write(" [WARM]  ");
                }
                else if (day.MinTempC <= 5)
                {
                    System.Console.ForegroundColor = ConsoleColor.Blue;
                    System.Console.Write(" [COLD]  ");
                }
                else
                {
                    System.Console.ForegroundColor = ConsoleColor.Green;
                    System.Console.Write(" [MILD]  ");
                }

                // Divider at bottom of card
                System.Console.SetCursorPosition(colX, 16);
                System.Console.ForegroundColor = ConsoleColor.DarkCyan;
                System.Console.Write("---------");

                // Clear row 17
                System.Console.SetCursorPosition(colX, 17);
                System.Console.Write("         ");
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

        private static string FormatDateLabel(string dateStr)
        {
            if (!string.IsNullOrEmpty(dateStr) && dateStr.Length >= 10)
            {
                string m = dateStr.Substring(5, 2);
                string d = dateStr.Substring(8, 2);
                return $"  {d}/{m}  ";
            }
            return "   ---   ";
        }

        private static string FormatDayHeader(string dateStr, string dayOfWeek)
        {
            if (!string.IsNullOrEmpty(dayOfWeek))
            {
                string d = dayOfWeek.ToUpper();
                if (d.Contains("SUN")) return "   SUN   ";
                if (d.Contains("MON")) return "   MON   ";
                if (d.Contains("TUE")) return "   TUE   ";
                if (d.Contains("WED")) return "   WED   ";
                if (d.Contains("THU")) return "   THU   ";
                if (d.Contains("FRI")) return "   FRI   ";
                if (d.Contains("SAT")) return "   SAT   ";
            }
            if (!string.IsNullOrEmpty(dateStr) && dateStr.Length >= 10)
            {
                return "  " + dateStr.Substring(5, 5) + "  ";
            }
            return "   DAY   ";
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

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                desktop.SetCurrentApp("launcher");
            }
            else if (keyEvent.Key == ConsoleKeyEx.R || keyEvent.KeyChar == 'r' || keyEvent.KeyChar == 'R')
            {
                desktop.WeatherService.FetchOpenWeather(desktop.DefaultCity, desktop.ApiKey);
            }
            else if (keyEvent.Key == ConsoleKeyEx.C || keyEvent.KeyChar == 'c' || keyEvent.KeyChar == 'C')
            {
                desktop.SetCurrentApp("change_city");
            }
            else if (keyEvent.Key == ConsoleKeyEx.W || keyEvent.KeyChar == 'w' || keyEvent.KeyChar == 'W')
            {
                desktop.SetCurrentApp("weather");
            }
        }
    }
}
