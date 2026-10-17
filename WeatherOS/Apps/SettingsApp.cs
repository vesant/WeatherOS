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
        public bool IsRealTime => true;

        private int _settingsIndex = 0;
        private const int TotalSettingsCount = 10;
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

        public void Draw(TuiDesktop desktop)
        {
            var win = desktop.CreateWindow("WEATHEROS CONTROL PANEL & SYSTEM CONFIGURATION", WindowSize.Large);

            string keyDisp = string.IsNullOrWhiteSpace(_apiKeyEditor.Text) ? "[Default System Key]" : _apiKeyEditor.Text;
            string cityDisp = string.IsNullOrWhiteSpace(_cityEditor.Text) ? "Setubal" : _cityEditor.Text;

            int tz = TimeService.TimezoneOffset;
            string tzLabel = tz >= 0 ? $"+{tz}" : $"{tz}";
            string clockFormatDisp = TimeService.Is24HourFormat ? "24-Hour (00:00 - 23:59)" : "12-Hour (AM / PM)";
            string liveClockDisp = TimeService.GetFormattedTime();

            string[] opts = { 
                "1. Open System Resource Monitor", 
                "2. Desktop Color Theme: " + (desktop.IsDarkMode ? "Dark Terminal Mode" : "Classic Oceanic Cyan"), 
                "3. Auto-Location Telemetry: " + (desktop.AutoLocation ? "ON (Auto-Detect via GeoIP)" : "OFF (Manual Default City)"),
                "4. Default Meteorological City: " + cityDisp + (_settingsIndex == 3 ? "_" : ""),
                "5. Detailed Network Debug Logs: " + (desktop.ShowDebugLogs ? "ON (Display Raw Frames & IP)" : "OFF (Secured & Masked)"),
                "6. Custom WeatherAPI Key: " + keyDisp + (_settingsIndex == 5 ? "_" : ""),
                "7. Clock Display Format: " + clockFormatDisp,
                "8. Clock Timezone Offset: UTC" + tzLabel + " [Left/Right or +/- Keys]",
                "9. Manual Clock Calibration: " + liveClockDisp + " [H: +1h | J: -1h | M: +1m | K: -1m]",
                "10. Network Atomic Time Sync: " + (TimeService.IsNetworkSynced ? "[Synced: " + TimeService.SyncSource + "]" : "[Offline: Press Enter to Sync]")
            };
            
            for (int i = 0; i < TotalSettingsCount; i++)
            {
                win.WriteSelectionRow(i + 1, opts[i], i == _settingsIndex);
            }

            win.WriteSeparator(TotalSettingsCount + 1);
            win.WriteRow(TotalSettingsCount + 2, $"  Active Time Engine: {TimeService.SyncSource}  |  RTC Hardware: Ports 0x70/0x71", ConsoleColor.DarkCyan, 2, fillRemaining: true);

            win.WriteFooter("[Up/Dn] Select  |  [Enter] Toggle/Save  |  [Left/Right, +/-] Adjust  |  [ESC] Exit");
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
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
                if (_settingsIndex >= TotalSettingsCount) _settingsIndex = 0;
                return;
            }
            
            if (keyEvent.Key == ConsoleKeyEx.UpArrow)
            {
                _settingsIndex--;
                if (_settingsIndex < 0) _settingsIndex = TotalSettingsCount - 1;
                return;
            }

            // Timezone quick adjust on Option 7
            if (_settingsIndex == 7)
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

            // Manual Time quick adjust on Option 8
            if (_settingsIndex == 8)
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
                    TimeService.Is24HourFormat = !TimeService.Is24HourFormat;
                    desktop.SaveSettings();
                    desktop.DrawStatusMessage($"Clock Format set to: {(TimeService.Is24HourFormat ? "24-Hour" : "12-Hour")}");
                }
                else if (_settingsIndex == 7)
                {
                    // Cycle timezone +1 hour
                    int nextTz = TimeService.TimezoneOffset + 1;
                    if (nextTz > 14) nextTz = -12;
                    TimeService.TimezoneOffset = nextTz;
                    desktop.SaveSettings();
                    desktop.DrawStatusMessage($"Timezone set to: UTC{(nextTz >= 0 ? "+" + nextTz : nextTz.ToString())}");
                }
                else if (_settingsIndex == 8)
                {
                    TimeService.AdjustHours(1);
                    desktop.DrawStatusMessage("Adjusted time: +1 Hour (Use [H/J/M/K] for precise adjustment)");
                }
                else if (_settingsIndex == 9)
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
    }
}
