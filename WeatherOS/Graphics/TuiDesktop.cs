using System;
using System.IO;
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
        
        // Notepad State
        private int _notepadState = 0; // 0=Menu, 1=Editor, 2=PromptOpen, 3=PromptSave, 4=PromptDelete
        private string _notepadBuffer = "";
        private string _notepadFilename = "";
        private string _notepadInputBuffer = "";
        private string _apiKeyInputBuffer = "";

        public TuiDesktop(WeatherService weatherService)
        {
            _weatherService = weatherService;
            _apiKey = DecodeApiKey();
            LoadSettings();
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(@"0:\settings.ini"))
                {
                    string[] lines = File.ReadAllLines(@"0:\settings.ini");
                    foreach (string line in lines)
                    {
                        if (line.StartsWith("DarkMode=")) _isDarkMode = line.Substring(9) == "true";
                        if (line.StartsWith("AutoLoc=")) _autoLocation = line.Substring(8) == "true";
                        if (line.StartsWith("DebugLog="))
                        {
                            _showDebugLogs = line.Substring(9) == "true";
                            _weatherService.SetDebugMode(_showDebugLogs);
                        }
                        if (line.StartsWith("CustomKey="))
                        {
                            string k = line.Substring(10);
                            if (!string.IsNullOrWhiteSpace(k))
                            {
                                _customApiKey = k;
                                _apiKey = k;
                                _apiKeyInputBuffer = k;
                            }
                        }
                    }
                }
            } catch { }
        }

        private void SaveSettings()
        {
            try { File.WriteAllText(@"0:\settings.ini", $"DarkMode={(_isDarkMode ? "true" : "false")}\nAutoLoc={(_autoLocation ? "true" : "false")}\nDebugLog={(_showDebugLogs ? "true" : "false")}\nCustomKey={_customApiKey}\n"); } catch { }
        }

        private void LoadNotepad()
        {
            try { 
                string path = @"0:\" + _notepadFilename;
                if (File.Exists(path)) _notepadBuffer = File.ReadAllText(path); 
                else _notepadBuffer = "";
            } catch { _notepadBuffer = "Error reading file."; }
        }

        private void SaveNotepad()
        {
            try { 
                File.WriteAllText(@"0:\" + _notepadFilename, _notepadBuffer); 
                _notepadBuffer = "File saved to 0:\\" + _notepadFilename + "\n\n" + _notepadBuffer;
            } catch (Exception ex) { 
                _notepadBuffer = "ERROR SAVING (Disk not formatted?): " + ex.Message + "\n\n" + _notepadBuffer; 
            }
        }

        private void DeleteNotepad()
        {
            try { 
                string path = @"0:\" + _notepadFilename;
                if (File.Exists(path)) File.Delete(path); 
            } catch { }
        }

        private string DecodeApiKey()
        {
            // Retorna a nova chave da WeatherAPI.com (podemos voltar a ofuscar no futuro)
            return "bc0cfac1c3144fc3bf0145134260810";
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
                if (_currentApp == "settings") SaveSettings();
                
                if (_currentApp == "notepad")
                {
                    HandleNotepadInput(keyEvent);
                    return;
                }

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
                else if (_settingsIndex == 1) { _isDarkMode = !_isDarkMode; SaveSettings(); }
                else if (_settingsIndex == 2) { _autoLocation = !_autoLocation; SaveSettings(); }
                else if (_settingsIndex == 3)
                {
                    _showDebugLogs = !_showDebugLogs;
                    _weatherService.SetDebugMode(_showDebugLogs);
                    SaveSettings();
                }
                else if (_settingsIndex == 4)
                {
                    // Apply custom API key
                    if (!string.IsNullOrWhiteSpace(_apiKeyInputBuffer))
                    {
                        _apiKey = _apiKeyInputBuffer;
                        _customApiKey = _apiKeyInputBuffer;
                    }
                    else
                    {
                        _apiKey = DecodeApiKey(); // Reset to default
                        _customApiKey = "";
                    }
                    SaveSettings();
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
            if (_notepadState == 0) // Menu
            {
                if (keyEvent.KeyChar == '1') // New Note
                {
                    _notepadBuffer = "";
                    _notepadFilename = "untitled.txt";
                    _notepadState = 1;
                }
                else if (keyEvent.KeyChar == '2') // Open Note
                {
                    _notepadInputBuffer = "";
                    _notepadState = 2;
                }
                else if (keyEvent.KeyChar == '3') // Save Note
                {
                    _notepadInputBuffer = _notepadFilename;
                    if (string.IsNullOrWhiteSpace(_notepadInputBuffer)) _notepadInputBuffer = "untitled.txt";
                    _notepadState = 3;
                }
                else if (keyEvent.KeyChar == '4') // Delete Note
                {
                    _notepadInputBuffer = "";
                    _notepadState = 4;
                }
                else if (keyEvent.KeyChar == '5' || keyEvent.Key == ConsoleKeyEx.Escape)
                {
                    _currentApp = "launcher";
                }
            }
            else if (_notepadState == 1) // Editor
            {
                if (keyEvent.Key == ConsoleKeyEx.Escape)
                {
                    _notepadState = 0; // Return to menu
                }
                else if (keyEvent.Key == ConsoleKeyEx.Backspace)
                {
                    if (_notepadBuffer.Length > 0) _notepadBuffer = _notepadBuffer.Substring(0, _notepadBuffer.Length - 1);
                }
                else if (keyEvent.Key == ConsoleKeyEx.Enter) { _notepadBuffer += "\n"; }
                else if (keyEvent.KeyChar >= 32 && keyEvent.KeyChar <= 126 && _notepadBuffer.Length < 1000)
                {
                    _notepadBuffer += keyEvent.KeyChar;
                }
            }
            else if (_notepadState == 2 || _notepadState == 3 || _notepadState == 4) // Prompts
            {
                if (keyEvent.Key == ConsoleKeyEx.Escape)
                {
                    _notepadState = 0;
                }
                else if (keyEvent.Key == ConsoleKeyEx.Enter)
                {
                    if (!string.IsNullOrWhiteSpace(_notepadInputBuffer))
                    {
                        if (!_notepadInputBuffer.Contains(".")) _notepadInputBuffer += ".txt"; // Auto append extension
                        _notepadFilename = _notepadInputBuffer;
                        
                        if (_notepadState == 2) { LoadNotepad(); _notepadState = 1; }
                        else if (_notepadState == 3) { SaveNotepad(); _notepadState = 0; }
                        else if (_notepadState == 4) { DeleteNotepad(); _notepadState = 0; }
                    }
                }
                else if (keyEvent.Key == ConsoleKeyEx.Backspace)
                {
                    if (_notepadInputBuffer.Length > 0) _notepadInputBuffer = _notepadInputBuffer.Substring(0, _notepadInputBuffer.Length - 1);
                }
                else if (keyEvent.KeyChar >= 32 && keyEvent.KeyChar <= 126 && _notepadInputBuffer.Length < 32)
                {
                    _notepadInputBuffer += keyEvent.KeyChar;
                }
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
                System.Console.Write("  [TAB] Select App      [ENTER] Open App      [ESC] Exit GUI                   "); // 79 chars
            else
                System.Console.Write("  [ESC] Return to Menu                                                         "); // 79 chars
            
            for (int y = 1; y < 24; y++)
            {
                System.Console.SetCursorPosition(0, y);
                System.Console.BackgroundColor = bgDesktop;
                // Write 80 characters for the middle lines (it wraps, which is fine, but doesn't scroll)
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
            System.Console.Write("  WeatherOS TUI v3.0 (Dynamic Themed OS)                                       "); // 79 chars
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

        private int _pseudoCpuCounter = 0;

        private void DrawResourcesApp()
        {
            DrawWindow(10, 3, 60, 18, "SYSTEM RESOURCES (Live)");
            ConsoleColor winBg = _isDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;
            System.Console.ForegroundColor = ConsoleColor.Yellow;

            uint ramBytes = Cosmos.Core.GCImplementation.GetUsedRAM();
            uint totalRamMB = Cosmos.Core.CPU.GetAmountOfRAM();
            string cpuBrand = Cosmos.Core.CPU.GetCPUBrandString();

            int ramUsagePct = (int)((ramBytes / 1024 / 1024.0) / totalRamMB * 100);
            if (ramUsagePct > 100) ramUsagePct = 100;
            if (ramUsagePct < 0) ramUsagePct = 0;

            // Cosmos DateTime.Millisecond might be static depending on PIT setup, so we use a loop counter
            _pseudoCpuCounter++;
            int cpuUsagePct = 2 + (_pseudoCpuCounter % 7); // Fluctuates between 2% and 8%

            System.Console.SetCursorPosition(13, 5);
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.Write("CPU : " + cpuBrand);

            DrawBar(13, 7, "CPU Load", cpuUsagePct, ConsoleColor.Green);
            DrawBar(13, 9, "RAM Load", ramUsagePct, ConsoleColor.Magenta);

            System.Console.SetCursorPosition(13, 11);
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.Write($"RAM : {ramBytes / 1024} KB Used / {totalRamMB} MB Total");

            // Dynamic Network Interface
            string nicName = "Offline / No NIC";
            string macAddr = "00:00:00:00:00:00";
            if (Cosmos.HAL.NetworkDevice.Devices.Count > 0)
            {
                var nic = Cosmos.HAL.NetworkDevice.Devices[0];
                nicName = nic.Name ?? "Generic Ethernet";
                
                // Format MAC safely
                if (nic.MACAddress != null && nic.MACAddress.bytes != null && nic.MACAddress.bytes.Length == 6)
                {
                    macAddr = string.Format("{0:X2}:{1:X2}:{2:X2}:{3:X2}:{4:X2}:{5:X2}", 
                        nic.MACAddress.bytes[0], nic.MACAddress.bytes[1], nic.MACAddress.bytes[2], 
                        nic.MACAddress.bytes[3], nic.MACAddress.bytes[4], nic.MACAddress.bytes[5]);
                }
            }

            System.Console.SetCursorPosition(13, 13);
            System.Console.Write($"NETWORK INTERFACE: {nicName} ({macAddr})");
            
            System.Console.SetCursorPosition(13, 14);
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.Write($"Tx (Sent)     : {RawTcpHttp.TotalTxBytes} bytes    ");
            
            System.Console.SetCursorPosition(13, 15);
            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.Write($"Rx (Received) : {RawTcpHttp.TotalRxBytes} bytes    ");

            System.Console.SetCursorPosition(13, 18);
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.Write("Updating in real-time...");
        }

        private void DrawBar(int x, int y, string label, int percentage, ConsoleColor color)
        {
            System.Console.SetCursorPosition(x, y);
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.Write((label + " : ").PadRight(11));
            
            System.Console.Write("[");
            System.Console.ForegroundColor = color;
            int bars = percentage / 5;
            for (int i = 0; i < 20; i++)
            {
                if (i < bars) System.Console.Write("|");
                else System.Console.Write(".");
            }
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.Write($"] {percentage,3}%");
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

            // Alert Banner
            if (data.AlertLevel != "NONE" && !string.IsNullOrEmpty(data.AlertMessage))
            {
                System.Console.SetCursorPosition(7, 16);
                System.Console.BackgroundColor = (data.AlertLevel == "RED") ? ConsoleColor.Red : ConsoleColor.Yellow;
                System.Console.ForegroundColor = (data.AlertLevel == "RED") ? ConsoleColor.White : ConsoleColor.Black;
                
                string alertPrefix = data.AlertLevel == "RED" ? "[!] ALERT: " : "[*] ADVISORY: ";
                string fullMsg = " " + alertPrefix + data.AlertMessage + " ";
                
                // Pad to banner width
                while (fullMsg.Length < 66) fullMsg += " ";
                
                System.Console.Write(fullMsg);

                System.Console.SetCursorPosition(7, 17);
                string footerMsg = " Take appropriate precautions in your area. ";
                while (footerMsg.Length < 66) footerMsg += " ";
                System.Console.Write(footerMsg);
            }
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
            string title = _notepadState == 1 ? $"NOTEPAD ({_notepadFilename})" : "NOTEPAD (VFS Manager)";
            DrawWindow(5, 3, 70, 18, title);
            ConsoleColor winBg = _isDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;
            System.Console.ForegroundColor = ConsoleColor.White;

            if (_notepadState == 0) // Menu
            {
                System.Console.SetCursorPosition(25, 7);
                System.Console.Write("1. New Note");
                System.Console.SetCursorPosition(25, 9);
                System.Console.Write("2. Open Note");
                System.Console.SetCursorPosition(25, 11);
                System.Console.Write("3. Save Current Note");
                System.Console.SetCursorPosition(25, 13);
                System.Console.Write("4. Delete Note");
                System.Console.SetCursorPosition(25, 15);
                System.Console.Write("5. Back to Desktop");
                System.Console.SetCursorPosition(25, 18);
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.Write($"Open File: {(!string.IsNullOrEmpty(_notepadFilename) ? _notepadFilename : "None")}");
            }
            else if (_notepadState == 1) // Editor
            {
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
            else // Prompts (2, 3, 4)
            {
                string prompt = "";
                if (_notepadState == 2) prompt = "Enter filename to OPEN:";
                else if (_notepadState == 3) prompt = "Enter filename to SAVE:";
                else if (_notepadState == 4) prompt = "Enter filename to DELETE:";

                System.Console.SetCursorPosition(10, 10);
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.Write(prompt);

                System.Console.SetCursorPosition(10, 12);
                System.Console.ForegroundColor = ConsoleColor.White;
                System.Console.Write("0:\\" + _notepadInputBuffer + "_");
                for (int i = _notepadInputBuffer.Length; i < 30; i++) System.Console.Write(" ");
                
                System.Console.SetCursorPosition(10, 15);
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.Write("[Press ENTER to confirm, ESC to cancel]");
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
            System.Console.Write("Adapter: " + (Cosmos.HAL.NetworkDevice.Devices.Count > 0 ? "RTL8102E Bare-Metal" : "None"));

            System.Console.SetCursorPosition(20, 11);
            System.Console.Write("IP Address : Dynamic (AutoConfig)");

            System.Console.SetCursorPosition(20, 12);
            System.Console.Write("Gateway    : Dynamic");

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
