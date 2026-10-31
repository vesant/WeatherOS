using System;
using Cosmos.System;
using WeatherOS.Graphics;
using WeatherOS.Services;

namespace WeatherOS.Apps
{
    public class ClockApp : IApp
    {
        public string Id => "clock";
        public string Name => "Digital Clock";
        public bool IsRealTime => true;

        public void Draw(TuiDesktop desktop)
        {
            var win = desktop.CreateWindow("WEATHEROS REAL-TIME DIGITAL CLOCK", WindowSize.Medium);

            // Read current local time from TimeService
            int year, month, day, hour, minute, second;
            TimeService.GetCurrentLocalTime(out year, out month, out day, out hour, out minute, out second);

            string amPm = "";
            int displayHourFormatted = hour;
            if (!TimeService.Is24HourFormat)
            {
                amPm = hour >= 12 ? " PM" : " AM";
                displayHourFormatted = hour % 12;
                if (displayHourFormatted == 0) displayHourFormatted = 12;
            }

            string hStr = displayHourFormatted.ToString().PadLeft(2, '0');
            string mStr = minute.ToString().PadLeft(2, '0');
            string sStr = second.ToString().PadLeft(2, '0');

            // Header info
            int tz = TimeService.TimezoneOffset;
            string tzLabel = tz >= 0 ? $"+{tz}" : $"{tz}";
            string formatLabel = TimeService.Is24HourFormat ? "24-Hour" : "12-Hour";
            win.WriteRow(0, $"CLOCK ENGINE : {TimeService.SyncSource}  |  UTC{tzLabel}  |  {formatLabel}", ConsoleColor.Cyan, 2, fillRemaining: true);
            win.WriteSeparator(1);

            // Large Digital Clock Display (3 rows of big ASCII digits)
            string[] bigH = GetBigDigits(hStr);
            string[] bigM = GetBigDigits(mStr);
            string[] bigS = GetBigDigits(sStr);

            string colonTop = (second % 2 == 0) ? "  *  " : "     ";
            string colonMid = (second % 2 == 0) ? "  *  " : "     ";
            string colonBtm = "     ";

            win.WriteRow(3, $"       {bigH[0]}{colonTop}{bigM[0]}{colonTop}{bigS[0]} {amPm}", ConsoleColor.Yellow, 1);
            win.WriteRow(4, $"       {bigH[1]}{colonMid}{bigM[1]}{colonMid}{bigS[1]}", ConsoleColor.Yellow, 1);
            win.WriteRow(5, $"       {bigH[2]}{colonBtm}{bigM[2]}{colonBtm}{bigS[2]}", ConsoleColor.Yellow, 1);

            win.WriteSeparator(7);

            // Calendar & Date details
            string dateStr = TimeService.GetFormattedDate();
            win.WriteRow(8, $"  Calendar Date : {dateStr}", ConsoleColor.White, 2, fillRemaining: true);

            // Seconds progression visual bar (60 seconds)
            int barWidth = Math.Max(20, win.InnerWidth - 18);
            int filled = (second * barWidth) / 60;
            string secBar = "[" + new string('=', filled) + ">" + new string('-', Math.Max(0, barWidth - filled - 1)) + $"] {second:D2}s";
            win.WriteRow(9, $"  Seconds Tick  : {secBar}", ConsoleColor.Green, 2, fillRemaining: true);

            win.WriteSeparator(10);

            string syncStatusText = TimeService.IsNetworkSynced ? "[Network Synced: OK]" : "[Hardware RTC Active]";
            win.WriteRow(11, $"  Timezone: UTC{tzLabel}  |  Format: {formatLabel}  |  Status: {syncStatusText}", ConsoleColor.DarkCyan, 2, fillRemaining: true);

            win.WriteFooter("[ESC] Return to Desktop", ConsoleColor.Yellow);
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                desktop.SetCurrentApp("launcher");
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
    }
}
