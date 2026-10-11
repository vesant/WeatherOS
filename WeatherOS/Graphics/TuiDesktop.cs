using System;
using System.Collections.Generic;
using System.IO;
using Cosmos.System;
using WeatherOS.Apps;
using WeatherOS.Services;

namespace WeatherOS.Graphics
{
    public class TuiDesktop
    {
        public static bool IsGuiActive = false;
        private bool _isActive;
        private string _currentApp = "launcher";
        private WeatherService _weatherService;
        private string _apiKey = "";

        // App Registry
        private Dictionary<string, IApp> _registeredApps = new Dictionary<string, IApp>();

        // Launcher State
        private int _selectedAppIndex = 0;
        private string[] _apps = { "Weather Dashboard", "Change City", "Digital Clock", "Calculator", "Notepad", "Disk & Storage Explorer", "System Information", "Network Config", "Settings", "Reboot System", "Poweroff System" };
        private string[] _appIds = { "weather", "change_city", "clock", "calculator", "notepad", "diskmgr", "about", "ipconfig", "settings", "reboot", "poweroff" };

        // Configuration State
        private bool _isDarkMode = false;
        private bool _autoLocation = true;
        private string _defaultCity = "Setubal";
        private bool _showDebugLogs = false;
        private string _customApiKey = "";

        public WeatherService WeatherService => _weatherService;
        public bool IsDarkMode { get => _isDarkMode; set => _isDarkMode = value; }
        public bool AutoLocation { get => _autoLocation; set => _autoLocation = value; }
        public string DefaultCity { get => _defaultCity; set => _defaultCity = value; }
        public bool ShowDebugLogs { get => _showDebugLogs; set => _showDebugLogs = value; }
        public string CustomApiKey { get => _customApiKey; set => _customApiKey = value; }
        public string ApiKey { get => _apiKey; set => _apiKey = value; }

        public TuiDesktop(WeatherService weatherService)
        {
            _weatherService = weatherService;
            _apiKey = DecodeApiKey();
            LoadSettings();
            RegisterApps();
        }

        private void RegisterApps()
        {
            RegisterApp(new WeatherDashboardApp());
            RegisterApp(new ChangeCityApp());
            RegisterApp(new ClockApp());
            RegisterApp(new CalculatorApp());
            RegisterApp(new NotepadApp());
            RegisterApp(new DiskManagerApp());
            RegisterApp(new SystemInfoApp());
            RegisterApp(new NetworkConfigApp());
            
            var settingsApp = new SettingsApp();
            settingsApp.InitializeBuffers(_defaultCity, _customApiKey);
            RegisterApp(settingsApp);

            RegisterApp(new SystemResourcesApp());
            RegisterApp(new DebugMenuApp());
            RegisterApp(new DiskDiagnosticsApp());
        }

        private void RegisterApp(IApp app)
        {
            if (app != null)
            {
                _registeredApps[app.Id] = app;
            }
        }

        public IApp GetApp(string id)
        {
            if (_registeredApps.ContainsKey(id)) return _registeredApps[id];
            return null;
        }

        private void LoadSettings()
        {
            try
            {
                string content;
                string status;
                if (StorageService.LoadFile("settings.ini", out content, out status))
                {
                    string[] lines = content.Split('\n');
                    foreach (string rawLine in lines)
                    {
                        string line = rawLine.Trim();
                        if (line.StartsWith("DarkMode=")) _isDarkMode = line.Substring(9) == "true";
                        if (line.StartsWith("AutoLoc=")) _autoLocation = line.Substring(8) == "true";
                        if (line.StartsWith("DefaultCity="))
                        {
                            string c = line.Substring(12).Trim();
                            if (!string.IsNullOrWhiteSpace(c))
                            {
                                _defaultCity = c;
                            }
                        }
                        if (line.StartsWith("DebugLog="))
                        {
                            _showDebugLogs = line.Substring(9) == "true";
                            _weatherService.SetDebugMode(_showDebugLogs);
                        }
                        if (line.StartsWith("CustomKey="))
                        {
                            string k = line.Substring(10).Trim();
                            if (!string.IsNullOrWhiteSpace(k))
                            {
                                _customApiKey = k;
                                _apiKey = k;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        public void SaveSettings()
        {
            string cfg = $"DarkMode={(_isDarkMode ? "true" : "false")}\nAutoLoc={(_autoLocation ? "true" : "false")}\nDefaultCity={_defaultCity}\nDebugLog={(_showDebugLogs ? "true" : "false")}\nCustomKey={_customApiKey}\n";
            string status;
            StorageService.SaveFile("settings.ini", cfg, out status);
        }

        public string GetDefaultApiKey()
        {
            return DecodeApiKey();
        }

        private string DecodeApiKey()
        {
            return "bc0cfac1c3144fc3bf0145134260810";
        }

        public string[] GetAppNames()
        {
            if (_showDebugLogs)
            {
                return new string[] {
                    "Weather Dashboard", "Change City", "Digital Clock", "Calculator",
                    "Notepad", "Disk & Storage Explorer", "System Information", "Network Config",
                    "Debug Menu [Diagnostics]", "Settings", "Reboot System", "Poweroff System"
                };
            }
            return new string[] {
                "Weather Dashboard", "Change City", "Digital Clock", "Calculator",
                "Notepad", "Disk & Storage Explorer", "System Information", "Network Config",
                "Settings", "Reboot System", "Poweroff System"
            };
        }

        public string[] GetAppIds()
        {
            if (_showDebugLogs)
            {
                return new string[] {
                    "weather", "change_city", "clock", "calculator",
                    "notepad", "diskmgr", "about", "ipconfig",
                    "debug_menu", "settings", "reboot", "poweroff"
                };
            }
            return new string[] {
                "weather", "change_city", "clock", "calculator",
                "notepad", "diskmgr", "about", "ipconfig",
                "settings", "reboot", "poweroff"
            };
        }

        public void SetCurrentApp(string appId)
        {
            _currentApp = appId;
        }

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
            IsGuiActive = true;
            try
            {
                while (_isActive)
                {
                    DrawDesktop();

                    bool isRealTimeApp = false;
                    IApp currentAppObj = GetApp(_currentApp);
                    if (currentAppObj != null && currentAppObj.IsRealTime)
                    {
                        isRealTimeApp = true;
                    }

                    if (isRealTimeApp)
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            // Actively poll network hardware in real time
                            WeatherOS.Kernel.CustomNIC?.HackPoll();

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
            }
            finally
            {
                IsGuiActive = false;
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
                    IApp notepad = GetApp("notepad");
                    notepad?.HandleInput(this, keyEvent);
                    return;
                }

                if (_currentApp == "launcher") _isActive = false;
                else _currentApp = "launcher";
            }
            else
            {
                if (_currentApp == "launcher")
                {
                    HandleLauncherInput(keyEvent);
                }
                else
                {
                    IApp app = GetApp(_currentApp);
                    if (app != null)
                    {
                        app.HandleInput(this, keyEvent);
                    }
                }
            }
        }

        private void HandleLauncherInput(KeyEvent keyEvent)
        {
            var apps = GetAppNames();
            var appIds = GetAppIds();

            if (keyEvent.Key == ConsoleKeyEx.Tab || keyEvent.Key == ConsoleKeyEx.DownArrow)
            {
                _selectedAppIndex++;
                if (_selectedAppIndex >= apps.Length) _selectedAppIndex = 0;
            }
            else if (keyEvent.Key == ConsoleKeyEx.UpArrow)
            {
                _selectedAppIndex--;
                if (_selectedAppIndex < 0) _selectedAppIndex = apps.Length - 1;
            }
            else if (keyEvent.Key == ConsoleKeyEx.Enter)
            {
                if (_selectedAppIndex >= appIds.Length) _selectedAppIndex = 0;
                string selected = appIds[_selectedAppIndex];
                if (selected == "reboot") Cosmos.System.Power.Reboot();
                else if (selected == "poweroff" || selected == "halt") Cosmos.System.Power.Shutdown();
                else _currentApp = selected;
            }
        }

        private void DrawDesktop()
        {
            System.Console.CursorVisible = false;
            System.Console.SetCursorPosition(0, 0);

            ConsoleColor bgDesktop = _isDarkMode ? ConsoleColor.Black : ConsoleColor.DarkBlue;
            System.Console.BackgroundColor = bgDesktop;
            
            // Top Bar
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.SetCursorPosition(0, 0);
            System.Console.BackgroundColor = ConsoleColor.Gray;
            System.Console.ForegroundColor = ConsoleColor.Black;
            System.Console.Write("  WeatherOS TUI v3.0  |  [ESC] Main Menu / Exit                                 "); // 79 chars

            System.Console.BackgroundColor = bgDesktop;
            for (int y = 1; y < 24; y++)
            {
                System.Console.SetCursorPosition(0, y);
                System.Console.Write("                                                                                ");
            }

            if (_currentApp == "launcher")
            {
                DrawLauncher();
            }
            else
            {
                IApp app = GetApp(_currentApp);
                if (app != null)
                {
                    app.Draw(this);
                }
                else
                {
                    DrawLauncher();
                }
            }

            System.Console.SetCursorPosition(0, 24);
            System.Console.BackgroundColor = ConsoleColor.Gray;
            System.Console.ForegroundColor = ConsoleColor.Black;
            System.Console.Write("  WeatherOS TUI v3.0 (Modular App Architecture)                                "); // 79 chars
            System.Console.ResetColor();
        }

        private void DrawLauncher()
        {
            var apps = GetAppNames();
            if (_selectedAppIndex >= apps.Length) _selectedAppIndex = 0;

            int boxWidth = 48;
            int boxHeight = apps.Length + 4;
            int left = (80 - boxWidth) / 2; // 16 (centered on 80 columns)
            int topY = 1 + (23 - boxHeight) / 2; // Vertically centered
            if (topY < 2) topY = 2;

            DrawWindow(left, topY, boxWidth, boxHeight, "MAIN MENU");
            ConsoleColor winBg = _isDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;

            for (int i = 0; i < apps.Length; i++)
            {
                System.Console.SetCursorPosition(left + 3, topY + 2 + i);
                if (i == _selectedAppIndex)
                {
                    System.Console.ForegroundColor = ConsoleColor.Black;
                    System.Console.BackgroundColor = ConsoleColor.White;
                }
                else
                {
                    System.Console.ForegroundColor = apps[i].Contains("Debug") ? ConsoleColor.Yellow : ConsoleColor.White;
                    System.Console.BackgroundColor = winBg;
                }
                string appItem = $" {(apps[i].Length > 0 ? "> " : "")}{apps[i]} ";
                if (appItem.Length > boxWidth - 6) appItem = appItem.Substring(0, boxWidth - 6);
                System.Console.Write(appItem.PadRight(boxWidth - 6));
            }
        }

        public void DrawWindow(int left, int top, int width, int height, string title)
        {
            if (left < 0) left = 0;
            if (top < 1) top = 1;
            if (left + width > 80) width = 80 - left;
            if (top + height > 24) height = 24 - top;
            if (width < 4 || height < 3) return;

            ConsoleColor winBg = _isDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;
            System.Console.ForegroundColor = ConsoleColor.White;

            System.Console.SetCursorPosition(left, top);
            System.Console.Write("+");
            for (int i = 0; i < width - 2; i++) System.Console.Write("-");
            System.Console.Write("+");

            string safeTitle = $" {title} ";
            if (safeTitle.Length > width - 4) safeTitle = safeTitle.Substring(0, width - 4);
            System.Console.SetCursorPosition(left + 2, top);
            System.Console.Write(safeTitle);

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

        public void DrawCenteredWindow(int width, int height, string title, out int left, out int top)
        {
            if (width > 78) width = 78;
            if (height > 22) height = 22;
            left = (80 - width) / 2;
            top = 1 + (23 - height) / 2;
            DrawWindow(left, top, width, height, title);
        }

        public void DrawBar(int x, int y, string label, int percentage, ConsoleColor color)
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

        public void DrawWeatherIcon(string condition, int left, int top)
        {
            string cond = (condition ?? "").ToLower();
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

        public void DrawStatusMessage(string msg)
        {
            System.Console.SetCursorPosition(23, 16);
            ConsoleColor winBg = _isDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.Write(msg);
        }
    }
}
