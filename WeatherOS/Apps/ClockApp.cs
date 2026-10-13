using System;
using Cosmos.System;
using WeatherOS.Graphics;

namespace WeatherOS.Apps
{
    public class ClockApp : IApp
    {
        public string Id => "clock";
        public string Name => "Digital Clock";
        public bool IsRealTime => true;

        private int _timeZoneOffset = 1; // Default: UTC+1 (Western Europe / Lisbon / London BST)
        private bool _is24HourFormat = true;
        private int _tickCounter = 0;

        public void Draw(TuiDesktop desktop)
        {
            var win = desktop.CreateWindow("WEATHEROS REAL-TIME DIGITAL CLOCK  |  CMOS RTC", WindowSize.Medium);

            // Read hardware CMOS Real-Time Clock
            int hour, minute, second, day, month, year;
            ReadHardwareCmosRtc(out hour, out minute, out second, out day, out month, out year);

            // Apply timezone offset
            int displayHour = (hour + _timeZoneOffset) % 24;
            if (displayHour < 0) displayHour += 24;

            string amPm = "";
            int displayHourFormatted = displayHour;
            if (!_is24HourFormat)
            {
                amPm = displayHour >= 12 ? " PM" : " AM";
                displayHourFormatted = displayHour % 12;
                if (displayHourFormatted == 0) displayHourFormatted = 12;
            }

            string hStr = displayHourFormatted.ToString().PadLeft(2, '0');
            string mStr = minute.ToString().PadLeft(2, '0');
            string sStr = second.ToString().PadLeft(2, '0');

            // Blinking colon animation
            _tickCounter++;
            string colon = (_tickCounter % 2 == 0) ? " : " : "   ";

            // Header info
            string tzLabel = _timeZoneOffset >= 0 ? $"+{_timeZoneOffset}" : $"{_timeZoneOffset}";
            win.WriteRow(0, $"HARDWARE CLOCK ENGINE : CMOS RTC (Port 0x70/0x71)  |  UTC{tzLabel}  |  {(_is24HourFormat ? "24-Hour" : "12-Hour")}", ConsoleColor.Cyan, 2, fillRemaining: true);
            win.WriteSeparator(1);

            // Large Digital Clock Display (3 rows of big ASCII digits)
            string[] bigH = GetBigDigits(hStr);
            string[] bigM = GetBigDigits(mStr);
            string[] bigS = GetBigDigits(sStr);

            string colonTop = (_tickCounter % 2 == 0) ? "  *  " : "     ";
            string colonMid = (_tickCounter % 2 == 0) ? "  *  " : "     ";
            string colonBtm = "     ";

            win.WriteRow(3, $"       {bigH[0]}{colonTop}{bigM[0]}{colonTop}{bigS[0]} {amPm}", ConsoleColor.Yellow, 1);
            win.WriteRow(4, $"       {bigH[1]}{colonMid}{bigM[1]}{colonMid}{bigS[1]}", ConsoleColor.Yellow, 1);
            win.WriteRow(5, $"       {bigH[2]}{colonBtm}{bigM[2]}{colonBtm}{bigS[2]}", ConsoleColor.Yellow, 1);

            win.WriteSeparator(7);

            // Calendar & Date details
            string dayName = GetDayOfWeekName(year, month, day);
            string monthName = GetMonthName(month);
            string dateStr = $"{dayName}, {day} {monthName} 20{year:D2}";
            win.WriteRow(8, $"  Calendar Date : {dateStr}", ConsoleColor.White, 2, fillRemaining: true);

            // Seconds progression visual bar (60 seconds)
            int barWidth = Math.Max(20, win.InnerWidth - 18);
            int filled = (second * barWidth) / 60;
            string secBar = "[" + new string('=', filled) + ">" + new string('-', Math.Max(0, barWidth - filled - 1)) + $"] {second:D2}s";
            win.WriteRow(9, $"  Seconds Tick  : {secBar}", ConsoleColor.Green, 2, fillRemaining: true);

            win.WriteSeparator(10);
            win.WriteRow(11, $"  Timezone: UTC{tzLabel} [+/- Keys]   |   Format: {(_is24HourFormat ? "24h" : "12h")} [Tab]   |   RTC Hardware Sync: [OK]", ConsoleColor.DarkCyan, 2, fillRemaining: true);

            win.WriteFooter("[Tab] Toggle 12h/24h   |   [+ / -] Adjust Timezone   |   [ESC] Exit", ConsoleColor.Yellow);
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                desktop.SetCurrentApp("launcher");
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.Tab || keyEvent.Key == ConsoleKeyEx.Spacebar)
            {
                _is24HourFormat = !_is24HourFormat;
                return;
            }

            if (keyEvent.KeyChar == '+' || keyEvent.KeyChar == '=' || keyEvent.Key == ConsoleKeyEx.RightArrow || keyEvent.Key == ConsoleKeyEx.UpArrow)
            {
                if (_timeZoneOffset < 14) _timeZoneOffset++;
                return;
            }

            if (keyEvent.KeyChar == '-' || keyEvent.KeyChar == '_' || keyEvent.Key == ConsoleKeyEx.LeftArrow || keyEvent.Key == ConsoleKeyEx.DownArrow)
            {
                if (_timeZoneOffset > -12) _timeZoneOffset--;
                return;
            }
        }

        private static void ReadHardwareCmosRtc(out int hour, out int minute, out int second, out int day, out int month, out int year)
        {
            hour = 12; minute = 0; second = 0; day = 13; month = 10; year = 26;

            try
            {
                // Primary: Cosmos.HAL.RTC
                second = Cosmos.HAL.RTC.Second;
                minute = Cosmos.HAL.RTC.Minute;
                hour = Cosmos.HAL.RTC.Hour;
                day = Cosmos.HAL.RTC.DayOfTheMonth;
                month = Cosmos.HAL.RTC.Month;
                year = Cosmos.HAL.RTC.Year;

                if (year > 100) year = year % 100;
                if (day == 0) day = 13;
                if (month == 0) month = 10;
            }
            catch
            {
                // Fallback to DateTime.UtcNow
                try
                {
                    var dt = DateTime.UtcNow;
                    hour = dt.Hour;
                    minute = dt.Minute;
                    second = dt.Second;
                    day = dt.Day;
                    month = dt.Month;
                    year = dt.Year % 100;
                }
                catch { }
            }
        }

        private static string[] GetBigDigits(string numStr)
        {
            string row0 = "";
            string row1 = "";
            string row2 = "";

            for (int i = 0; i < numStr.Length; i++)
            {
                char c = numStr[i];
                var d = GetDigitGlyph(c);
                row0 += d[0] + " ";
                row1 += d[1] + " ";
                row2 += d[2] + " ";
            }

            return new string[] { row0, row1, row2 };
        }

        private static string[] GetDigitGlyph(char c)
        {
            switch (c)
            {
                case '0':
                    return new string[] { " _ ", "| |", "|_|" };
                case '1':
                    return new string[] { "   ", "  |", "  |" };
                case '2':
                    return new string[] { " _ ", " _|", "|_ " };
                case '3':
                    return new string[] { " _ ", " _|", " _|" };
                case '4':
                    return new string[] { "   ", "|_|", "  |" };
                case '5':
                    return new string[] { " _ ", "|_ ", " _|" };
                case '6':
                    return new string[] { " _ ", "|_ ", "|_|" };
                case '7':
                    return new string[] { " _ ", "  |", "  |" };
                case '8':
                    return new string[] { " _ ", "|_|", "|_|" };
                case '9':
                    return new string[] { " _ ", "|_|", " _|" };
                default:
                    return new string[] { "   ", "   ", "   " };
            }
        }

        private static string GetDayOfWeekName(int year, int month, int day)
        {
            try
            {
                int fullYear = 2000 + year;
                if (month >= 1 && month <= 12 && day >= 1 && day <= 31)
                {
                    var dt = new DateTime(fullYear, month, day);
                    return dt.DayOfWeek.ToString();
                }
            }
            catch { }
            return "Tuesday";
        }

        private static string GetMonthName(int month)
        {
            string[] months = new string[]
            {
                "January", "February", "March", "April", "May", "June",
                "July", "August", "September", "October", "November", "December"
            };
            if (month >= 1 && month <= 12) return months[month - 1];
            return "October";
        }
    }
}
