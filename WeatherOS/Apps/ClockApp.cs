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

        private int _tickCounter = 0;
        private string _statusNotification = "";

        public void Draw(TuiDesktop desktop)
        {
            var win = desktop.CreateWindow("WEATHEROS REAL-TIME DIGITAL CLOCK  |  NETWORK & RTC ENGINE", WindowSize.Medium);

            // Tick internal timer
            TimeService.Tick(100);

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

            _tickCounter++;

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

            string colonTop = (_tickCounter % 2 == 0) ? "  *  " : "     ";
            string colonMid = (_tickCounter % 2 == 0) ? "  *  " : "     ";
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

            string syncStatusText = TimeService.IsNetworkSynced ? "[Network Synced: OK]" : "[Hardware RTC / Manual Base]";
            if (!string.IsNullOrEmpty(_statusNotification))
            {
                syncStatusText = _statusNotification;
            }
            win.WriteRow(11, $"  Timezone: UTC{tzLabel} [+/-]  |  Format: {formatLabel} [Tab]  |  Sync: {syncStatusText}", ConsoleColor.DarkCyan, 2, fillRemaining: true);

            win.WriteFooter("[Tab] 12h/24h  |  [+/-] Timezone  |  [H/J/M/K] Adjust Time  |  [N] NTP Sync  |  [ESC] Exit", ConsoleColor.Yellow);
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                _statusNotification = "";
                desktop.SetCurrentApp("launcher");
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.Tab || keyEvent.Key == ConsoleKeyEx.Spacebar)
            {
                TimeService.Is24HourFormat = !TimeService.Is24HourFormat;
                desktop.SaveSettings();
                _statusNotification = $"Format: {(TimeService.Is24HourFormat ? "24-Hour" : "12-Hour")}";
                return;
            }

            if (keyEvent.KeyChar == '+' || keyEvent.KeyChar == '=' || keyEvent.Key == ConsoleKeyEx.RightArrow || keyEvent.Key == ConsoleKeyEx.UpArrow)
            {
                if (TimeService.TimezoneOffset < 14)
                {
                    TimeService.TimezoneOffset++;
                    desktop.SaveSettings();
                    int tz = TimeService.TimezoneOffset;
                    _statusNotification = $"Timezone: UTC{(tz >= 0 ? "+" + tz : tz.ToString())}";
                }
                return;
            }

            if (keyEvent.KeyChar == '-' || keyEvent.KeyChar == '_' || keyEvent.Key == ConsoleKeyEx.LeftArrow || keyEvent.Key == ConsoleKeyEx.DownArrow)
            {
                if (TimeService.TimezoneOffset > -12)
                {
                    TimeService.TimezoneOffset--;
                    desktop.SaveSettings();
                    int tz = TimeService.TimezoneOffset;
                    _statusNotification = $"Timezone: UTC{(tz >= 0 ? "+" + tz : tz.ToString())}";
                }
                return;
            }

            // Manual time calibration hotkeys (useful when RTC battery is depleted and network is unavailable)
            if (keyEvent.KeyChar == 'h' || keyEvent.KeyChar == 'H')
            {
                TimeService.AdjustHours(1);
                _statusNotification = "Time adjusted: +1 Hour";
                return;
            }

            if (keyEvent.KeyChar == 'j' || keyEvent.KeyChar == 'J')
            {
                TimeService.AdjustHours(-1);
                _statusNotification = "Time adjusted: -1 Hour";
                return;
            }

            if (keyEvent.KeyChar == 'm' || keyEvent.KeyChar == 'M')
            {
                TimeService.AdjustMinutes(1);
                _statusNotification = "Time adjusted: +1 Minute";
                return;
            }

            if (keyEvent.KeyChar == 'k' || keyEvent.KeyChar == 'K')
            {
                TimeService.AdjustMinutes(-1);
                _statusNotification = "Time adjusted: -1 Minute";
                return;
            }

            // Network Time Sync on demand
            if (keyEvent.KeyChar == 'n' || keyEvent.KeyChar == 'N')
            {
                _statusNotification = "Contacting NTP server (UDP 123)...";
                desktop.DrawStatusMessage("Contacting NTP Atomic Server...");
                bool ok = TimeService.SyncFromNtp();
                if (ok)
                {
                    _statusNotification = "NTP Sync Success! Clock synchronized.";
                    desktop.DrawStatusMessage("NTP Sync Success: Clock is now atomic accurate.");
                }
                else
                {
                    _statusNotification = "NTP UDP blocked. Trying HTTP Date header...";
                    desktop.DrawStatusMessage("Querying HTTP gateway for Date header...");
                    desktop.WeatherService.FetchOpenWeather(desktop.DefaultCity, desktop.ApiKey);
                    if (TimeService.IsNetworkSynced)
                    {
                        _statusNotification = "HTTP Sync Success! Clock synchronized.";
                    }
                    else
                    {
                        _statusNotification = "Network Sync Timeout. Using Manual Calibration.";
                    }
                }
                return;
            }

            if (keyEvent.KeyChar == 's' || keyEvent.KeyChar == 'S')
            {
                desktop.SetCurrentApp("settings");
                return;
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
