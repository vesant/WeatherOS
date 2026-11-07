using System;
using Cosmos.System;
using WeatherOS.Graphics;
using WeatherOS.Services;

namespace WeatherOS.Apps
{
    public class SettingsApp : IApp
    {
        public string Id => "settings";
        public string Name => "Settings";
        public bool IsRealTime => false;

        private int _settingsIndex = 0;
        private bool _inClockSubmenu = false;
        private int _clockSubmenuIndex = 0;

        private const int MainSettingsCount = 7;
        private const int ClockSubmenuCount = 5;

        private TextEditorState _cityEditor = new TextEditorState { AllowMultiline = false, MaxLength = 25 };
        private TextEditorState _apiKeyEditor = new TextEditorState { AllowMultiline = false, MaxLength = 40 };

        public SettingsApp()
        {
        }

        public void InitializeBuffers(string defaultCity, string customApiKey)
        {
            _cityEditor.Text = defaultCity ?? "Setubal";
            _cityEditor.CursorPos = _cityEditor.Text.Length;
            _cityEditor.ClearSelection();

            _apiKeyEditor.Text = customApiKey ?? "";
            _apiKeyEditor.CursorPos = _apiKeyEditor.Text.Length;
            _apiKeyEditor.ClearSelection();
        }

        public void RefreshBuffers(TuiDesktop desktop)
        {
            _cityEditor.Text = desktop.DefaultCity ?? "Setubal";
            _cityEditor.CursorPos = _cityEditor.Text.Length;
            _cityEditor.ClearSelection();

            _apiKeyEditor.Text = desktop.CustomApiKey ?? "";
            _apiKeyEditor.CursorPos = _apiKeyEditor.Text.Length;
            _apiKeyEditor.ClearSelection();
            _inClockSubmenu = false;
        }

        public void Draw(TuiDesktop desktop)
        {
            try
            {
                if (_inClockSubmenu)
                {
                    DrawClockSubmenu(desktop);
                }
                else
                {
                    DrawMainSettings(desktop);
                }
            }
            catch { }
        }

        private void DrawMainSettings(TuiDesktop desktop)
        {
            var win = desktop.CreateWindow("WEATHEROS CONTROL PANEL & SYSTEM CONFIGURATION", WindowSize.Large);

            string keyDisp = string.IsNullOrWhiteSpace(_apiKeyEditor.Text) ? "[Default System Key]" : _apiKeyEditor.Text;
            string cityDisp = string.IsNullOrWhiteSpace(_cityEditor.Text) ? "Setubal" : _cityEditor.Text;

            string[] opts = {
                "1. Open System Resource Monitor",
                "2. Desktop Color Theme: " + (desktop.IsDarkMode ? "Dark Terminal Mode" : "Classic Oceanic Cyan"),
                "3. Auto-Location Telemetry: " + (desktop.AutoLocation ? "ON (Auto-Detect via GeoIP)" : "OFF (Manual Default City)"),
                "4. Default Meteorological City: " + cityDisp + (_settingsIndex == 3 ? "_" : ""),
                "5. Detailed Network Debug Logs: " + (desktop.ShowDebugLogs ? "ON (Display Raw Frames & IP)" : "OFF (Secured & Masked)"),
                "6. Custom WeatherAPI Key: " + keyDisp + (_settingsIndex == 5 ? "_" : ""),
                "7. Clock & Time Configuration [Submenu ->]"
            };

            for (int i = 0; i < MainSettingsCount; i++)
            {
                win.WriteSelectionRow(i + 1, opts[i], i == _settingsIndex);
            }

            win.WriteSeparator(MainSettingsCount + 1);
            win.WriteRow(MainSettingsCount + 2, $"  System Time: {TimeService.GetFormattedTime()} (UTC{(TimeService.TimezoneOffset >= 0 ? "+" + TimeService.TimezoneOffset : TimeService.TimezoneOffset.ToString())})  |  Engine: {TimeService.SyncSource}", ConsoleColor.DarkCyan, 2, fillRemaining: true);

            win.WriteFooter("[F1] Main Commands Menu  |  [ESC] Exit & Save");
        }

        private void DrawClockSubmenu(TuiDesktop desktop)
        {
            var win = desktop.CreateWindow("SETTINGS > CLOCK & TIME CONFIGURATION", WindowSize.Large);

            int tz = TimeService.TimezoneOffset;
            string tzLabel = tz >= 0 ? $"+{tz}" : $"{tz}";
            string clockFormatDisp = TimeService.Is24HourFormat ? "24-Hour (00:00 - 23:59)" : "12-Hour (AM / PM)";
            string liveClockDisp = TimeService.GetFormattedTime();

            string[] clockOpts = {
                "1. Clock Display Format: " + clockFormatDisp + " [Enter/Space: Toggle]",
                "2. Timezone Offset: UTC" + tzLabel + " [Left/Right or +/- Keys]",
                "3. Manual Clock Calibration: " + liveClockDisp + " [H: +1h | J: -1h | M: +1m | K: -1m]",
                "4. Network Atomic Time Sync: " + (TimeService.IsNetworkSynced ? "[Synced: " + TimeService.SyncSource + "]" : "[Offline: Press Enter to Sync]"),
                "5. < Return to Main Settings"
            };

            for (int i = 0; i < ClockSubmenuCount; i++)
            {
                win.WriteSelectionRow(i + 1, clockOpts[i], i == _clockSubmenuIndex);
            }

            win.WriteSeparator(ClockSubmenuCount + 1);
            win.WriteRow(ClockSubmenuCount + 2, $"  Active Time Engine: {TimeService.SyncSource}  |  CMOS RTC Hardware: Ports 0x70/0x71", ConsoleColor.DarkCyan, 2, fillRemaining: true);

            win.WriteFooter("[F1] Main Commands Menu  |  [ESC] Back");
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            try
            {
                if (_inClockSubmenu)
                {
                    HandleClockSubmenuInput(desktop, keyEvent);
                }
                else
                {
                    HandleMainSettingsInput(desktop, keyEvent);
                }
            }
            catch { }
        }

        private void HandleMainSettingsInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                desktop.SaveSettings();
                desktop.SetCurrentApp("launcher");
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.Tab || keyEvent.Key == ConsoleKeyEx.DownArrow)
            {
                _settingsIndex++;
                if (_settingsIndex >= MainSettingsCount) _settingsIndex = 0;
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.UpArrow)
            {
                _settingsIndex--;
                if (_settingsIndex < 0) _settingsIndex = MainSettingsCount - 1;
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.Enter)
            {
                if (_settingsIndex == 0)
                {
                    desktop.SetCurrentApp("resources");
                }
                else if (_settingsIndex == 1)
                {
                    desktop.IsDarkMode = !desktop.IsDarkMode;
                    desktop.SaveSettings();
                }
                else if (_settingsIndex == 2)
                {
                    desktop.AutoLocation = !desktop.AutoLocation;
                    desktop.SaveSettings();
                }
                else if (_settingsIndex == 3)
                {
                    desktop.DefaultCity = string.IsNullOrWhiteSpace(_cityEditor.Text) ? "Setubal" : _cityEditor.Text.Trim();
                    desktop.SaveSettings();
                    desktop.WeatherService.FetchOpenWeather(desktop.DefaultCity, desktop.ApiKey);
                    desktop.DrawStatusMessage($"Saved Default City: {desktop.DefaultCity}");
                }
                else if (_settingsIndex == 4)
                {
                    desktop.ShowDebugLogs = !desktop.ShowDebugLogs;
                    desktop.WeatherService.SetDebugMode(desktop.ShowDebugLogs);
                    desktop.SaveSettings();
                    desktop.DrawStatusMessage(desktop.ShowDebugLogs ? "Debug: ON (IP Visible)" : "Debug: OFF (IP Secured)");
                }
                else if (_settingsIndex == 5)
                {
                    if (!string.IsNullOrWhiteSpace(_apiKeyEditor.Text))
                    {
                        desktop.ApiKey = _apiKeyEditor.Text.Trim();
                        desktop.CustomApiKey = _apiKeyEditor.Text.Trim();
                    }
                    else
                    {
                        desktop.ApiKey = desktop.GetDefaultApiKey();
                        desktop.CustomApiKey = "";
                    }
                    desktop.SaveSettings();
                    desktop.DrawStatusMessage("Saved API Key.");
                }
                else if (_settingsIndex == 6)
                {
                    _inClockSubmenu = true;
                    _clockSubmenuIndex = 0;
                }
            }
            else if (_settingsIndex == 3)
            {
                _cityEditor.HandleKey(keyEvent);
            }
            else if (_settingsIndex == 5)
            {
                _apiKeyEditor.HandleKey(keyEvent);
            }
        }

        private void HandleClockSubmenuInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                desktop.SaveSettings();
                _inClockSubmenu = false;
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.Tab || keyEvent.Key == ConsoleKeyEx.DownArrow)
            {
                _clockSubmenuIndex++;
                if (_clockSubmenuIndex >= ClockSubmenuCount) _clockSubmenuIndex = 0;
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.UpArrow)
            {
                _clockSubmenuIndex--;
                if (_clockSubmenuIndex < 0) _clockSubmenuIndex = ClockSubmenuCount - 1;
                return;
            }

            // Timezone adjust on Submenu Option 1
            if (_clockSubmenuIndex == 1)
            {
                if (keyEvent.Key == ConsoleKeyEx.LeftArrow || keyEvent.KeyChar == '-' || keyEvent.KeyChar == '_')
                {
                    if (TimeService.TimezoneOffset > -12)
                    {
                        TimeService.TimezoneOffset--;
                        desktop.SaveSettings();
                    }
                    return;
                }
                if (keyEvent.Key == ConsoleKeyEx.RightArrow || keyEvent.KeyChar == '+' || keyEvent.KeyChar == '=')
                {
                    if (TimeService.TimezoneOffset < 14)
                    {
                        TimeService.TimezoneOffset++;
                        desktop.SaveSettings();
                    }
                    return;
                }
            }

            // Manual Time calibration on Submenu Option 2
            if (_clockSubmenuIndex == 2)
            {
                if (keyEvent.KeyChar == 'h' || keyEvent.KeyChar == 'H')
                {
                    TimeService.AdjustHours(1);
                    desktop.DrawStatusMessage("Adjusted time: +1 Hour");
                    return;
                }
                if (keyEvent.KeyChar == 'j' || keyEvent.KeyChar == 'J')
                {
                    TimeService.AdjustHours(-1);
                    desktop.DrawStatusMessage("Adjusted time: -1 Hour");
                    return;
                }
                if (keyEvent.KeyChar == 'm' || keyEvent.KeyChar == 'M')
                {
                    TimeService.AdjustMinutes(1);
                    desktop.DrawStatusMessage("Adjusted time: +1 Minute");
                    return;
                }
                if (keyEvent.KeyChar == 'k' || keyEvent.KeyChar == 'K')
                {
                    TimeService.AdjustMinutes(-1);
                    desktop.DrawStatusMessage("Adjusted time: -1 Minute");
                    return;
                }
            }

            if (keyEvent.Key == ConsoleKeyEx.Enter || keyEvent.Key == ConsoleKeyEx.Spacebar)
            {
                if (_clockSubmenuIndex == 0)
                {
                    TimeService.Is24HourFormat = !TimeService.Is24HourFormat;
                    desktop.SaveSettings();
                    desktop.DrawStatusMessage($"Clock Format set to: {(TimeService.Is24HourFormat ? "24-Hour" : "12-Hour")}");
                }
                else if (_clockSubmenuIndex == 1)
                {
                    int nextTz = TimeService.TimezoneOffset + 1;
                    if (nextTz > 14) nextTz = -12;
                    TimeService.TimezoneOffset = nextTz;
                    desktop.SaveSettings();
                    desktop.DrawStatusMessage($"Timezone set to: UTC{(nextTz >= 0 ? "+" + nextTz : nextTz.ToString())}");
                }
                else if (_clockSubmenuIndex == 2)
                {
                    TimeService.AdjustHours(1);
                    desktop.DrawStatusMessage("Adjusted time: +1 Hour (Use [H/J/M/K] for precise adjustment)");
                }
                else if (_clockSubmenuIndex == 3)
                {
                    desktop.DrawStatusMessage("Querying Network Atomic Time (NTP / HTTP)...");
                    bool ok = TimeService.SyncFromNtp();
                    if (ok)
                    {
                        desktop.DrawStatusMessage("NTP Synchronization Success: Clock updated.");
                    }
                    else
                    {
                        desktop.WeatherService.FetchOpenWeather(desktop.DefaultCity, desktop.ApiKey);
                        if (TimeService.IsNetworkSynced)
                        {
                            desktop.DrawStatusMessage("HTTP Date Header Synchronization Success: Clock updated.");
                        }
                        else
                        {
                            desktop.DrawStatusMessage("Network Sync Failed. Hardware RTC / Manual fallback active.");
                        }
                    }
                }
                else if (_clockSubmenuIndex == 4)
                {
                    desktop.SaveSettings();
                    _inClockSubmenu = false;
                }
            }
        }
    }
}
