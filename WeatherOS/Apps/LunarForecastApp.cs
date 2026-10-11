using System;
using Cosmos.System;
using WeatherOS.Graphics;
using WeatherOS.Services;

namespace WeatherOS.Apps
{
    public class LunarForecastApp : IApp
    {
        public string Id => "lunar_forecast";
        public string Name => "Lunar Observatory";
        public bool IsRealTime => false;

        private int _selectedDay = 0; // 0 to 6

        public void Draw(TuiDesktop desktop)
        {
            desktop.DrawWindow(3, 2, 74, 21, "LUNAR OBSERVATORY & ASTRONOMICAL MOON PHASES");
            ConsoleColor winBg = desktop.IsDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;

            var data = desktop.WeatherService.GetLatestReading();
            var days = data.ForecastDays;

            string moonPhase = "Waxing Crescent";
            string moonIllum = "15%";
            string dateStr = DateTime.UtcNow.ToString("yyyy-MM-dd");
            string dayHeader = "TODAY";

            if (days != null && _selectedDay >= 0 && _selectedDay < days.Length)
            {
                var curDay = days[_selectedDay];
                if (!string.IsNullOrEmpty(curDay.MoonPhase)) moonPhase = curDay.MoonPhase;
                if (!string.IsNullOrEmpty(curDay.MoonIllumination)) moonIllum = curDay.MoonIllumination;
                if (!string.IsNullOrEmpty(curDay.Date)) dateStr = curDay.Date;
                dayHeader = curDay.DayOfWeek;
            }
            else if (!string.IsNullOrEmpty(data.MoonPhase))
            {
                moonPhase = data.MoonPhase;
                if (!string.IsNullOrEmpty(data.MoonIllumination)) moonIllum = data.MoonIllumination;
            }

            // Top day navigation bar
            System.Console.SetCursorPosition(5, 4);
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            string topNav = $"[<] DAY {_selectedDay + 1}/7: {dayHeader} [>]  |  Date: {dateStr}  |  {data.Location}";
            if (topNav.Length > 70) topNav = topNav.Substring(0, 70);
            System.Console.Write(topNav.PadRight(70));

            System.Console.SetCursorPosition(5, 5);
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.Write("----------------------------------------------------------------------");

            // Left: Draw High-Detail 8-Line ASCII Moon
            DrawLargeAsciiMoon(moonPhase, 6, 7);

            // Phase name directly under moon art
            System.Console.SetCursorPosition(6, 15);
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            string centerPhase = $"[{moonPhase.ToUpper()}]";
            int pad = (20 - centerPhase.Length) / 2;
            if (pad < 0) pad = 0;
            System.Console.Write(new string(' ', pad) + centerPhase);

            // Right: Detailed Telemetry Panel
            int infoX = 29;
            System.Console.SetCursorPosition(infoX, 7);
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.Write("LUNAR TELEMETRY & OBSERVATION CONDITIONS:");

            System.Console.SetCursorPosition(infoX, 9);
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.Write($"Phase Type   : {moonPhase}");

            System.Console.SetCursorPosition(infoX, 10);
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.Write($"Illumination : {moonIllum}");

            // Illumination percentage bar
            int illumPct = ParseIllumInt(moonIllum);
            desktop.DrawBar(infoX, 11, "Light Level", illumPct, ConsoleColor.Yellow);

            // Cycle & Stargazing notes
            string cycleAge = GetCycleAge(moonPhase);
            System.Console.SetCursorPosition(infoX, 13);
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.Write($"Lunar Age    : {cycleAge}");

            string tide = GetTideType(moonPhase);
            System.Console.SetCursorPosition(infoX, 14);
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.Write($"Tidal Impact : {tide}");

            string stargaze = GetStargazeRating(illumPct);
            System.Console.SetCursorPosition(infoX, 15);
            System.Console.ForegroundColor = illumPct < 30 ? ConsoleColor.Green : (illumPct < 70 ? ConsoleColor.Yellow : ConsoleColor.Red);
            System.Console.Write($"Stargazing   : {stargaze}");

            // Bottom: 7-day lunar cycle progression strip
            System.Console.SetCursorPosition(5, 17);
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.Write("----------------------------------------------------------------------");

            System.Console.SetCursorPosition(5, 18);
            for (int d = 0; d < 7; d++)
            {
                int stripX = 5 + (d * 10);
                System.Console.SetCursorPosition(stripX, 18);

                if (d == _selectedDay)
                {
                    System.Console.BackgroundColor = ConsoleColor.White;
                    System.Console.ForegroundColor = ConsoleColor.Black;
                }
                else
                {
                    System.Console.BackgroundColor = winBg;
                    System.Console.ForegroundColor = ConsoleColor.Gray;
                }

                string dName = d == 0 ? "TODAY" : (d == 1 ? "TMRW" : $"DAY+{d}");
                System.Console.Write($" {dName.PadRight(7)}");

                System.Console.BackgroundColor = winBg;
                System.Console.SetCursorPosition(stripX, 19);
                if (days != null && d < days.Length)
                {
                    System.Console.ForegroundColor = ConsoleColor.Cyan;
                    string miniP = ShortPhase(days[d].MoonPhase);
                    System.Console.Write($" {miniP.PadRight(7)}");
                }
            }

            // Bottom controls
            System.Console.BackgroundColor = winBg;
            System.Console.SetCursorPosition(5, 21);
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.Write("[< / >] Prev/Next Day  |  [1-7] Select Day  |  [R] Refresh  |  [ESC] Exit");
        }

        private static int ParseIllumInt(string illumStr)
        {
            if (string.IsNullOrEmpty(illumStr)) return 15;
            int val = 0;
            for (int i = 0; i < illumStr.Length; i++)
            {
                char c = illumStr[i];
                if (c >= '0' && c <= '9') val = (val * 10) + (c - '0');
            }
            if (val > 100) val = 100;
            return val > 0 ? val : 15;
        }

        private static string ShortPhase(string phase)
        {
            if (string.IsNullOrEmpty(phase)) return "Waxing";
            string p = phase.ToLower();
            if (p.Contains("new")) return "New";
            if (p.Contains("full")) return "Full";
            if (p.Contains("first")) return "1st Qtr";
            if (p.Contains("last") || p.Contains("third")) return "LastQtr";
            if (p.Contains("waxing") && p.Contains("crescent")) return "Wax.Cres";
            if (p.Contains("waning") && p.Contains("crescent")) return "Wan.Cres";
            if (p.Contains("waxing") && p.Contains("gibbous")) return "Wax.Gib";
            if (p.Contains("waning") && p.Contains("gibbous")) return "Wan.Gib";
            return "Moon";
        }

        private static string GetCycleAge(string phase)
        {
            if (string.IsNullOrEmpty(phase)) return "Day 4 / 29.5";
            string p = phase.ToLower();
            if (p.Contains("new")) return "Day 0 / 29.5 (New Moon)";
            if (p.Contains("waxing") && p.Contains("crescent")) return "Day 4 / 29.5 (Early Cycle)";
            if (p.Contains("first")) return "Day 7 / 29.5 (First Quarter)";
            if (p.Contains("waxing") && p.Contains("gibbous")) return "Day 11 / 29.5 (Waxing)";
            if (p.Contains("full")) return "Day 14.8 / 29.5 (Peak Full)";
            if (p.Contains("waning") && p.Contains("gibbous")) return "Day 18 / 29.5 (Waning)";
            if (p.Contains("last") || p.Contains("third")) return "Day 22 / 29.5 (Last Quarter)";
            if (p.Contains("waning") && p.Contains("crescent")) return "Day 26 / 29.5 (Late Cycle)";
            return "Day 4 / 29.5";
        }

        private static string GetTideType(string phase)
        {
            if (string.IsNullOrEmpty(phase)) return "Neap Tide (Moderate)";
            string p = phase.ToLower();
            if (p.Contains("new") || p.Contains("full")) return "Spring Tide (High Amplitude)";
            if (p.Contains("first") || p.Contains("last") || p.Contains("third")) return "Neap Tide (Low Amplitude)";
            return "Moderate Tidal Flow";
        }

        private static string GetStargazeRating(int illum)
        {
            if (illum < 20) return "Excellent (Dark Skies)";
            if (illum < 50) return "Good (Low Light Pollution)";
            if (illum < 80) return "Fair (Moderate Moonlight)";
            return "Poor (Bright Lunar Glare)";
        }

        private static void DrawLargeAsciiMoon(string phase, int left, int top)
        {
            string p = (phase ?? "").ToLower();
            System.Console.ForegroundColor = ConsoleColor.White;

            if (p.Contains("new"))
            {
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.SetCursorPosition(left, top + 0); System.Console.Write("     .--------.     ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("   .'          '.   ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("  /   .------.   \\  ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write(" |   |  DARK  |   | ");
                System.Console.SetCursorPosition(left, top + 4); System.Console.Write(" |   |  NEW   |   | ");
                System.Console.SetCursorPosition(left, top + 5); System.Console.Write("  \\   '------'   /  ");
                System.Console.SetCursorPosition(left, top + 6); System.Console.Write("   '.          .'   ");
                System.Console.SetCursorPosition(left, top + 7); System.Console.Write("     '--------'     ");
            }
            else if (p.Contains("full"))
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.SetCursorPosition(left, top + 0); System.Console.Write("     .--------.     ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("   .'@@@@@@@@@@'.   ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("  /@@@  @@@@@@@@@\\  ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write(" |@@@@@@   @@@@@@| ");
                System.Console.SetCursorPosition(left, top + 4); System.Console.Write(" |@@@@@@@@@  @@@@| ");
                System.Console.SetCursorPosition(left, top + 5); System.Console.Write("  \\@@@@@@@@@@@@@/  ");
                System.Console.SetCursorPosition(left, top + 6); System.Console.Write("   '.@@@@@@@@@@.'   ");
                System.Console.SetCursorPosition(left, top + 7); System.Console.Write("     '--------'     ");
            }
            else if (p.Contains("first"))
            {
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.SetCursorPosition(left, top + 0); System.Console.Write("     .--------.     ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("   .'    |#####'.   ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("  /      |######\\  ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write(" |       |######| ");
                System.Console.SetCursorPosition(left, top + 4); System.Console.Write(" |       |######| ");
                System.Console.SetCursorPosition(left, top + 5); System.Console.Write("  \\      |######/  ");
                System.Console.SetCursorPosition(left, top + 6); System.Console.Write("   '.    |#####.'   ");
                System.Console.SetCursorPosition(left, top + 7); System.Console.Write("     '--------'     ");
            }
            else if (p.Contains("last") || p.Contains("third"))
            {
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.SetCursorPosition(left, top + 0); System.Console.Write("     .--------.     ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("   .'#####|    '.   ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("  /######|      \\  ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write(" |#######|       | ");
                System.Console.SetCursorPosition(left, top + 4); System.Console.Write(" |#######|       | ");
                System.Console.SetCursorPosition(left, top + 5); System.Console.Write("  \\######|      /  ");
                System.Console.SetCursorPosition(left, top + 6); System.Console.Write("   '.#####|    .'   ");
                System.Console.SetCursorPosition(left, top + 7); System.Console.Write("     '--------'     ");
            }
            else if (p.Contains("waxing") && p.Contains("gibbous"))
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.SetCursorPosition(left, top + 0); System.Console.Write("     .--------.     ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("   .'  (#######'.   ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("  /   (#########\\  ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write(" |   (###########| ");
                System.Console.SetCursorPosition(left, top + 4); System.Console.Write(" |   (###########| ");
                System.Console.SetCursorPosition(left, top + 5); System.Console.Write("  \\   (#########/  ");
                System.Console.SetCursorPosition(left, top + 6); System.Console.Write("   '.  (#######.'   ");
                System.Console.SetCursorPosition(left, top + 7); System.Console.Write("     '--------'     ");
            }
            else if (p.Contains("waning") && p.Contains("gibbous"))
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.SetCursorPosition(left, top + 0); System.Console.Write("     .--------.     ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("   .'#######)  '.   ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("  /#########)    \\  ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write(" |###########)   | ");
                System.Console.SetCursorPosition(left, top + 4); System.Console.Write(" |###########)   | ");
                System.Console.SetCursorPosition(left, top + 5); System.Console.Write("  \\#########)    /  ");
                System.Console.SetCursorPosition(left, top + 6); System.Console.Write("   '.#######)  .'   ");
                System.Console.SetCursorPosition(left, top + 7); System.Console.Write("     '--------'     ");
            }
            else if (p.Contains("waning") && p.Contains("crescent"))
            {
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.SetCursorPosition(left, top + 0); System.Console.Write("     .--------.     ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("   .'::        '.   ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("  / ::::         \\  ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write(" | ::::::        | ");
                System.Console.SetCursorPosition(left, top + 4); System.Console.Write(" | ::::::        | ");
                System.Console.SetCursorPosition(left, top + 5); System.Console.Write("  \\ ::::         /  ");
                System.Console.SetCursorPosition(left, top + 6); System.Console.Write("   '.::        .'   ");
                System.Console.SetCursorPosition(left, top + 7); System.Console.Write("     '--------'     ");
            }
            else // Default Waxing Crescent
            {
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.SetCursorPosition(left, top + 0); System.Console.Write("     .--------.     ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("   .'        ::'.   ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("  /         :::: \\  ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write(" |         ::::::| ");
                System.Console.SetCursorPosition(left, top + 4); System.Console.Write(" |         ::::::| ");
                System.Console.SetCursorPosition(left, top + 5); System.Console.Write("  \\         :::: /  ");
                System.Console.SetCursorPosition(left, top + 6); System.Console.Write("   '.        ::.'   ");
                System.Console.SetCursorPosition(left, top + 7); System.Console.Write("     '--------'     ");
            }
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.LeftArrow)
            {
                _selectedDay--;
                if (_selectedDay < 0) _selectedDay = 6;
            }
            else if (keyEvent.Key == ConsoleKeyEx.RightArrow)
            {
                _selectedDay++;
                if (_selectedDay > 6) _selectedDay = 0;
            }
            else if (keyEvent.Key >= ConsoleKeyEx.D1 && keyEvent.Key <= ConsoleKeyEx.D7)
            {
                _selectedDay = (int)keyEvent.Key - (int)ConsoleKeyEx.D1;
            }
            else if (keyEvent.Key >= ConsoleKeyEx.Num1 && keyEvent.Key <= ConsoleKeyEx.Num7)
            {
                _selectedDay = (int)keyEvent.Key - (int)ConsoleKeyEx.Num1;
            }
            else if (keyEvent.Key == ConsoleKeyEx.R)
            {
                var cur = desktop.WeatherService.GetLatestReading();
                string city = cur.Location.Split(',')[0].Trim();
                desktop.WeatherService.FetchOpenWeather(city, desktop.ApiKey);
            }
            else if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                desktop.SetCurrentApp("launcher");
            }
        }
    }
}
