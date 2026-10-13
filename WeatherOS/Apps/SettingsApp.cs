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
            var win = desktop.CreateWindow("CONTROL PANEL", WindowSize.Medium);

            string keyDisp = string.IsNullOrWhiteSpace(_apiKeyEditor.Text) ? "[Default]" : _apiKeyEditor.Text;
            string cityDisp = string.IsNullOrWhiteSpace(_cityEditor.Text) ? "Setubal" : _cityEditor.Text;

            string[] opts = { 
                "1. Open Resource Monitor", 
                "2. Theme: " + (desktop.IsDarkMode ? "Dark Mode" : "Light Mode"), 
                "3. Auto-Location: " + (desktop.AutoLocation ? "ON (Auto-Detect)" : "OFF (Manual Default)"),
                "4. Default City: " + cityDisp + (_settingsIndex == 3 ? "_" : ""),
                "5. Detailed Debug Logs: " + (desktop.ShowDebugLogs ? "ON (Show IP/Raw)" : "OFF (Secured/Masked)"),
                "6. Custom API Key: " + keyDisp + (_settingsIndex == 5 ? "_" : "")
            };
            
            for (int i = 0; i < 6; i++)
            {
                win.WriteSelectionRow(1 + (i * 2), opts[i], i == _settingsIndex);
            }

            win.WriteFooter("[Up/Dn] Move  |  [Enter] Toggle/Save  |  [ESC] Exit");
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                desktop.SetCurrentApp("launcher");
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.Tab || keyEvent.Key == ConsoleKeyEx.DownArrow)
            {
                _settingsIndex++;
                if (_settingsIndex > 5) _settingsIndex = 0;
            }
            else if (keyEvent.Key == ConsoleKeyEx.UpArrow)
            {
                _settingsIndex--;
                if (_settingsIndex < 0) _settingsIndex = 5;
            }
            else if (keyEvent.Key == ConsoleKeyEx.Enter)
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
