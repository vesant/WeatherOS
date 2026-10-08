using System;
using Cosmos.System;
using WeatherOS.Services;

namespace WeatherOS.Graphics
{
    public class TuiDesktop
    {
        private bool _isActive;
        private string _currentApp = "launcher";
        private WeatherService _weatherService;
        private string _apiKey = "";

        // Launcher State
        private int _selectedAppIndex = 0;
        private string[] _apps = { "Weather Dashboard", "Change City", "Digital Clock", "Calculator", "Notepad", "System Information", "Network Config", "Settings", "Reboot System", "Shutdown" };
        private string[] _appIds = { "weather", "change_city", "clock", "calculator", "notepad", "about", "ipconfig", "settings", "reboot", "halt" };

        // Settings State
        private int _settingsIndex = 0;
        private bool _isDarkMode = false;
        private bool _autoLocation = true;
        private bool _showDebugLogs = false;
        private string _customApiKey = "";

        // Input Buffers
        private string _cityInputBuffer = "";
        private string _calcInputBuffer = "";
        private string _calcResult = "";
        private string _notepadBuffer = "";
        private string _apiKeyInputBuffer = "";

        public TuiDesktop(WeatherService weatherService)
        {
            _weatherService = weatherService;
            _apiKey = DecodeApiKey();
        }

        private string DecodeApiKey()
        {
            string encrypted = "ce8f3:4:4g4957gc42229343cec3112g";
            char[] decoded = new char[encrypted.Length];
            for (int i = 0; i < encrypted.Length; i++) decoded[i] = (char)(encrypted[i] - 1);
            return new string(decoded);
        }

        public string[] GetAppNames() { return _apps; }
        public string[] GetAppIds() { return _appIds; }

        public void StartApp(string appId)
        {
            _currentApp = appId;
            _isActive = true;
            RunLoop();
        }

        public void Start()
        {
            if (_weatherService.GetLatestReading().Location == "UNKNOWN")
            {
                _currentApp = "change_city";
            }
            else
            {
                _currentApp = "launcher";
            }
            
            _isActive = true;
            RunLoop();
        }

        private void RunLoop()
        {
            while (_isActive)
            {
                DrawDesktop();

                if (_currentApp == "clock" || _currentApp == "resources")
                {
                    for (int i = 0; i < 10; i++)
                    {
                        if (System.Console.KeyAvailable)
                        {
                            var keyEvent = KeyboardManager.ReadKey();
                            ProcessKeyEvent(keyEvent);
                            break;
                        }
                        System.Threading.Thread.Sleep(100);
                    }
                }
                else
                {
                    var keyEvent = KeyboardManager.ReadKey();
                    ProcessKeyEvent(keyEvent);
                }
            }

            System.Console.Clear();
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine("[sys] tty1: Terminated graphical session.\n");
            System.Console.ResetColor();
        }

        private void ProcessKeyEvent(KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                if (_currentApp == "launcher") _isActive = false;
                else _currentApp = "launcher";
            }
            else
            {
                if (_currentApp == "launcher") HandleLauncherInput(keyEvent);
                else if (_currentApp == "settings") HandleSettingsInput(keyEvent);
                else if (_currentApp == "change_city") HandleChangeCityInput(keyEvent);
                else if (_currentApp == "calculator") HandleCalculatorInput(keyEvent);
                else if (_currentApp == "notepad") HandleNotepadInput(keyEvent);
            }
        }

        private void HandleLauncherInput(KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Tab || keyEvent.Key == ConsoleKeyEx.DownArrow)
            {
                _selectedAppIndex++;
                if (_selectedAppIndex >= _apps.Length) _selectedAppIndex = 0;
            }
            else if (keyEvent.Key == ConsoleKeyEx.UpArrow)
            {
                _selectedAppIndex--;
                if (_selectedAppIndex < 0) _selectedAppIndex = _apps.Length - 1;
            }
            else if (keyEvent.Key == ConsoleKeyEx.Enter)
            {
                string selected = _appIds[_selectedAppIndex];
                if (selected == "reboot") Cosmos.System.Power.Reboot();
                else if (selected == "halt") Cosmos.System.Power.Shutdown();
                else _currentApp = selected;
            }
        }

        private void HandleSettingsInput(KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Tab || keyEvent.Key == ConsoleKeyEx.DownArrow)
            {
                _settingsIndex++;
                if (_settingsIndex > 4) _settingsIndex = 0;
            }
            else if (keyEvent.Key == ConsoleKeyEx.UpArrow)
            {
                _settingsIndex--;
                if (_settingsIndex < 0) _settingsIndex = 4;
            }
            else if (keyEvent.Key == ConsoleKeyEx.Enter)
            {
                if (_settingsIndex == 0) _currentApp = "resources";
                else if (_settingsIndex == 1) _isDarkMode = !_isDarkMode;
                else if (_settingsIndex == 2) _autoLocation = !_autoLocation;
                else if (_settingsIndex == 3)
                {
                    _showDebugLogs = !_showDebugLogs;
                    _weatherService.SetDebugMode(_showDebugLogs);
                }
                else if (_settingsIndex == 4)
                {
                    // Apply custom API key
                    if (!string.IsNullOrWhiteSpace(_apiKeyInputBuffer))
                    {
                        _apiKey = _apiKeyInputBuffer;
                    }
                    else
                    {
                        _apiKey = DecodeApiKey(); // Reset to default
                    }
                }
            }
            else if (_settingsIndex == 4) // Typing in the custom API key field
            {
                if (keyEvent.Key == ConsoleKeyEx.Backspace)
                {
                    if (_apiKeyInputBuffer.Length > 0) _apiKeyInputBuffer = _apiKeyInputBuffer.Substring(0, _apiKeyInputBuffer.Length - 1);
                }
                else if (keyEvent.KeyChar >= 32 && keyEvent.KeyChar <= 126 && _apiKeyInputBuffer.Length < 32)
                {
                    _apiKeyInputBuffer += keyEvent.KeyChar;
                }
            }
        }

        private void HandleChangeCityInput(KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Enter)
            {
                if (!string.IsNullOrWhiteSpace(_cityInputBuffer))
                {
                    DrawStatusMessage("Fetching new data via TCP...");
                    _weatherService.FetchOpenWeather(_cityInputBuffer.Trim(), _apiKey);
                    _cityInputBuffer = "";
                    _currentApp = "weather";
                }
            }
            else if (keyEvent.Key == ConsoleKeyEx.Backspace)
            {
                if (_cityInputBuffer.Length > 0) _cityInputBuffer = _cityInputBuffer.Substring(0, _cityInputBuffer.Length - 1);
            }
            else if (keyEvent.KeyChar >= 32 && keyEvent.KeyChar <= 126 && _cityInputBuffer.Length < 20)
            {
                _cityInputBuffer += keyEvent.KeyChar;
            }
        }

        private void HandleCalculatorInput(KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Enter)
            {
                try
                {
                    string[] parts = _calcInputBuffer.Split(new char[] { '+', '-', '*', '/' });
                    if (parts.Length == 2)
                    {
                        int a = int.Parse(parts[0].Trim());
                        int b = int.Parse(parts[1].Trim());
                        if (_calcInputBuffer.Contains("+")) _calcResult = (a + b).ToString();
                        else if (_calcInputBuffer.Contains("-")) _calcResult = (a - b).ToString();
                        else if (_calcInputBuffer.Contains("*")) _calcResult = (a * b).ToString();
                        else if (_calcInputBuffer.Contains("/")) _calcResult = (b != 0 ? (a / b).ToString() : "Div/0");
                    }
                    else _calcResult = "Err";
                }
                catch { _calcResult = "Err"; }
                _calcInputBuffer = "";
            }
            else if (keyEvent.Key == ConsoleKeyEx.Backspace)
            {
                if (_calcInputBuffer.Length > 0) _calcInputBuffer = _calcInputBuffer.Substring(0, _calcInputBuffer.Length - 1);
            }
            else if (keyEvent.KeyChar >= 32 && keyEvent.KeyChar <= 126 && _calcInputBuffer.Length < 15)
            {
                _calcInputBuffer += keyEvent.KeyChar;
            }
        }

        private void HandleNotepadInput(KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Backspace)
            {
                if (_notepadBuffer.Length > 0) _notepadBuffer = _notepadBuffer.Substring(0, _notepadBuffer.Length - 1);
            }
            else if (keyEvent.Key == ConsoleKeyEx.Enter) _notepadBuffer += "\n";
            else if (keyEvent.KeyChar >= 32 && keyEvent.KeyChar <= 126 && _notepadBuffer.Length < 500)
            {
                _notepadBuffer += keyEvent.KeyChar;
            }
        }

        private void DrawDesktop()
        {
            System.Console.CursorVisible = false;
            System.Console.SetCursorPosition(0, 0);

            ConsoleColor bgDesktop = _isDarkMode ? ConsoleColor.Black : ConsoleColor.DarkBlue;

            System.Console.BackgroundColor = ConsoleColor.Gray;
            System.Console.ForegroundColor = ConsoleColor.Black;
            if (_currentApp == "launcher")
                System.Console.Write("  [TAB] Select App      [ENTER] Open App      [ESC] Exit GUI                    ");
            else
                System.Console.Write("  [ESC] Return to Menu                                                          ");
            
            for (int y = 1; y < 24; y++)
            {
                System.Console.SetCursorPosition(0, y);
                System.Console.BackgroundColor = bgDesktop;
                for (int x = 0; x < 80; x++) System.Console.Write(" ");
            }

            if (_currentApp == "launcher") DrawLauncher();
            else if (_currentApp == "settings") DrawSettingsApp();
            else if (_currentApp == "resources") DrawResourcesApp();
            else if (_currentApp == "about") DrawAboutApp();
            else if (_currentApp == "ipconfig") DrawIpConfigApp();
            else if (_currentApp == "weather") DrawWeatherApp();
            else if (_currentApp == "change_city") DrawChangeCityApp();
            else if (_currentApp == "clock") DrawClockApp();
            else if (_currentApp == "calculator") DrawCalculatorApp();
            else if (_currentApp == "notepad") DrawNotepadApp();

            System.Console.SetCursorPosition(0, 24);
            System.Console.BackgroundColor = ConsoleColor.Gray;
            System.Console.ForegroundColor = ConsoleColor.Black;
            System.Console.Write("  WeatherOS TUI v3.0 (Dynamic Themed OS)                                        ");
            System.Console.ResetColor();
        }

        private void DrawWindow(int left, int top, int width, int height, string title)
        {
            ConsoleColor winBg = _isDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;
            System.Console.ForegroundColor = ConsoleColor.White;

            System.Console.SetCursorPosition(left, top);
            System.Console.Write("+");
            for (int i = 0; i < width - 2; i++) System.Console.Write("-");
            System.Console.Write("+");

            System.Console.SetCursorPosition(left + 2, top);
            System.Console.Write($" {title} ");

            for (int y = 1; y < height - 1; y++)
            {
                System.Console.SetCursorPosition(left, top + y);
                System.Console.Write("|");
                for (int x = 0; x < width - 2; x++) System.Console.Write(" ");
                System.Console.Write("|");
            }

            System.Console.SetCursorPosition(left, top + height - 1);
            System.Console.Write("+");
            for (int i = 0; i < width - 2; i++) System.Console.Write("-");
            System.Console.Write("+");
        }

        private void DrawLauncher()
        {
            DrawWindow(20, 4, 40, 16, "MAIN MENU");
            ConsoleColor winBg = _isDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;

            for (int i = 0; i < _apps.Length; i++)
            {
                System.Console.SetCursorPosition(25, 7 + i);
                if (i == _selectedAppIndex)
                {
                    System.Console.ForegroundColor = ConsoleColor.Black;
                    System.Console.BackgroundColor = ConsoleColor.White;
                }
                else
                {
                    System.Console.ForegroundColor = ConsoleColor.White;
                    System.Console.BackgroundColor = winBg;
                }
                System.Console.Write($" {(_apps[i].Length > 0 ? "> " : "")}{_apps[i]} ".PadRight(30));
            }
        }

        private void DrawSettingsApp()
        {
            DrawWindow(10, 5, 60, 16, "CONTROL PANEL");
            ConsoleColor winBg = _isDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;

            string keyDisp = string.IsNullOrWhiteSpace(_apiKeyInputBuffer) ? "[Default]" : _apiKeyInputBuffer;

            string[] opts = { 
                "1. Open Resource Monitor", 
                "2. Theme: " + (_isDarkMode ? "Dark Mode" : "Light Mode"), 
                "3. Auto-Location: " + (_autoLocation ? "ON" : "OFF"),
                "4. Detailed Debug Logs: " + (_showDebugLogs ? "ON" : "OFF"),
                "5. Custom API Key: " + keyDisp + "_"
            };
            
            for (int i = 0; i < 5; i++)
            {
                System.Console.SetCursorPosition(15, 8 + i * 2);
                if (i == _settingsIndex)
                {
                    System.Console.ForegroundColor = ConsoleColor.Black;
                    System.Console.BackgroundColor = ConsoleColor.White;
                }
                else
                {
                    System.Console.ForegroundColor = ConsoleColor.White;
                    System.Console.BackgroundColor = winBg;
                }
                System.Console.Write($" {opts[i]} ".PadRight(50));
            }
        }

        private void DrawResourcesApp()
        {
            DrawWindow(15, 6, 50, 10, "SYSTEM RESOURCES (Live)");
            ConsoleColor winBg = _isDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;
            System.Console.ForegroundColor = ConsoleColor.Yellow;

            uint ramBytes = Cosmos.Core.GCImplementation.GetUsedRAM();
            uint totalRamMB = Cosmos.Core.CPU.GetAmountOfRAM();
            string cpuBrand = Cosmos.Core.CPU.GetCPUBrandString();

            System.Console.SetCursorPosition(18, 9);
            System.Console.Write("CPU : " + cpuBrand);

            System.Console.SetCursorPosition(18, 11);
            System.Console.Write($"RAM : {ramBytes / 1024} KB Used / {totalRamMB} MB Total");

            System.Console.SetCursorPosition(18, 13);
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.Write("Updating in real-time...");
        }

        private void DrawWeatherApp()
        {
            DrawWindow(5, 3, 70, 18, "WEATHER APPLICATION");
            ConsoleColor winBg = _isDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;

            var data = _weatherService.GetLatestReading();

            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.SetCursorPosition(30, 5);
            System.Console.Write("LOCAL: " + data.Location);

            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.SetCursorPosition(30, 7);
            System.Console.Write("Condition : " + data.Condition);

            System.Console.SetCursorPosition(30, 9);
            System.Console.Write("Temp      : " + ((int)data.TemperatureCelsius).ToString() + " C");

            System.Console.SetCursorPosition(30, 11);
            System.Console.Write("Humidity  : " + ((int)data.HumidityPercent).ToString() + " %");

            System.Console.SetCursorPosition(50, 9);
            System.Console.Write("Pressure : " + ((int)data.PressureHpa).ToString() + " hPa");

            System.Console.SetCursorPosition(50, 11);
            System.Console.Write("Wind Spd : " + ((int)data.WindSpeedKmh).ToString() + " km/h");

            System.Console.SetCursorPosition(50, 13);
            System.Console.Write("Wind Dir : " + data.WindDirection);

            DrawWeatherIcon(data.Condition, 8, 7);
        }

        private void DrawWeatherIcon(string condition, int left, int top)
        {
            string cond = condition.ToLower();
            if (cond.Contains("cloud"))
            {
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("      .--.      ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("   .-(    ).    ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("  (___.__)__)   ");
            }
            else if (cond.Contains("rain") || cond.Contains("drizzle") || cond.Contains("thunder"))
            {
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("   .-^^---.     ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("  (________)    ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("   /  /  /      ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("  /  /  /       ");
            }
            else if (cond.Contains("snow"))
            {
                System.Console.ForegroundColor = ConsoleColor.White;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("   .-^^---.     ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("  (________)    ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("   *  *  *      ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("  *  *  *       ");
            }
            else
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("    \\   /      ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("     .-.        ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("  --(   )--     ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("     `-'        ");
                System.Console.SetCursorPosition(left, top + 4); System.Console.Write("    /   \\      ");
            }
        }

        private void DrawChangeCityApp()
        {
            DrawWindow(20, 8, 40, 10, "SYSTEM DIALOG: CHANGE CITY");
            ConsoleColor winBg = _isDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;
            System.Console.ForegroundColor = ConsoleColor.White;

            System.Console.SetCursorPosition(23, 11);
            System.Console.Write("Enter new city name:");

            System.Console.SetCursorPosition(23, 13);
            System.Console.Write("> " + _cityInputBuffer + "_");
            for (int i = _cityInputBuffer.Length; i < 20; i++) System.Console.Write(" ");

            System.Console.SetCursorPosition(23, 15);
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.Write("[Press ENTER to search]");
        }

        private void DrawClockApp()
        {
            DrawWindow(25, 8, 30, 8, "DIGITAL CLOCK");
            ConsoleColor winBg = _isDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            
            string timeStr = DateTime.UtcNow.ToString("HH:mm:ss");
            string dateStr = DateTime.UtcNow.ToString("yyyy-MM-dd");

            System.Console.SetCursorPosition(32, 11);
            System.Console.Write(timeStr + " UTC");
            
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.SetCursorPosition(33, 13);
            System.Console.Write(dateStr);
        }

        private void DrawCalculatorApp()
        {
            DrawWindow(20, 4, 30, 17, "CALCULATOR");
            ConsoleColor winBg = _isDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;
            System.Console.ForegroundColor = ConsoleColor.White;

            System.Console.SetCursorPosition(22, 6);
            System.Console.Write($"[{_calcResult.PadLeft(24)}]");

            System.Console.SetCursorPosition(24, 9);
            System.Console.Write("[7]  [8]  [9]  [/]");
            System.Console.SetCursorPosition(24, 11);
            System.Console.Write("[4]  [5]  [6]  [*]");
            System.Console.SetCursorPosition(24, 13);
            System.Console.Write("[1]  [2]  [3]  [-]");
            System.Console.SetCursorPosition(24, 15);
            System.Console.Write("[0]  [.]  [C]  [+]");

            System.Console.SetCursorPosition(22, 18);
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.Write("> " + _calcInputBuffer + "_");
            for (int i = _calcInputBuffer.Length; i < 20; i++) System.Console.Write(" ");
        }

        private void DrawNotepadApp()
        {
            DrawWindow(5, 3, 70, 18, "NOTEPAD (Volatile)");
            ConsoleColor winBg = _isDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;
            System.Console.ForegroundColor = ConsoleColor.White;

            string[] lines = _notepadBuffer.Split('\n');
            for (int i = 0; i < lines.Length && i < 15; i++)
            {
                System.Console.SetCursorPosition(7, 5 + i);
                string line = lines[i];
                if (line.Length > 65) line = line.Substring(0, 65);
                System.Console.Write(line);
                if (i == lines.Length - 1) System.Console.Write("_");
            }
            if (lines.Length == 0)
            {
                System.Console.SetCursorPosition(7, 5);
                System.Console.Write("_");
            }
        }

        private void DrawAboutApp()
        {
            DrawWindow(20, 6, 40, 10, "SYSTEM INFORMATION");
            ConsoleColor winBg = _isDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;
            System.Console.ForegroundColor = ConsoleColor.White;

            System.Console.SetCursorPosition(25, 9);
            System.Console.Write("WeatherOS v2.0 (Cosmos Kernel)");
            System.Console.SetCursorPosition(25, 11);
            System.Console.Write("Developed by: Ghost");
            System.Console.SetCursorPosition(25, 13);
            System.Console.Write("Architecture: x86 / 32-bit (IL2CPU)");
        }

        private void DrawIpConfigApp()
        {
            DrawWindow(15, 6, 50, 10, "NETWORK CONFIGURATION");
            ConsoleColor winBg = _isDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;
            System.Console.ForegroundColor = ConsoleColor.Cyan;

            System.Console.SetCursorPosition(20, 9);
            System.Console.Write("Adapter: " + (Cosmos.HAL.NetworkDevice.Devices.Count > 0 ? "Ethernet (E1000/RTL8139)" : "None"));

            System.Console.SetCursorPosition(20, 11);
            System.Console.Write("IP Address : 10.0.2.15");

            System.Console.SetCursorPosition(20, 12);
            System.Console.Write("Gateway    : 10.0.2.2");

            System.Console.SetCursorPosition(20, 13);
            System.Console.Write("Subnet Mask: 255.255.255.0");
        }

        private void DrawStatusMessage(string msg)
        {
            System.Console.SetCursorPosition(23, 16);
            ConsoleColor winBg = _isDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.Write(msg);
        }
    }
}
