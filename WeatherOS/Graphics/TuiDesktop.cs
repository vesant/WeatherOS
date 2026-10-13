using System;
using System.Collections.Generic;
using System.IO;
using Cosmos.System;
using WeatherOS.Apps;
using WeatherOS.Services;

namespace WeatherOS.Graphics
{
    public class LauncherAppEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Category { get; set; } = "";
    }

    public class TuiDesktop
    {
        public static bool IsGuiActive = false;
        private bool _isActive;
        private string _currentApp = "launcher";
        private WeatherService _weatherService;
        private string _apiKey = "";

        // App Registry
        private Dictionary<string, IApp> _registeredApps = new Dictionary<string, IApp>();

        // Launcher Categorized State
        private int _selectedAppIndex = 0;
        private int _launcherColumn = 0; // 0=Left, 1=Right
        private int _launcherLeftIndex = 0;
        private int _launcherRightIndex = 0;
        private string[] _apps = { "Weather Dashboard", "7-Day Weather Forecast", "Lunar Observatory", "Change City", "Digital Clock", "Calculator", "Notepad", "File & Storage Explorer", "System Information", "Network Config", "Settings", "Reboot System", "Poweroff System" };
        private string[] _appIds = { "weather", "forecast_7d", "lunar_forecast", "change_city", "clock", "calculator", "notepad", "diskmgr", "about", "ipconfig", "settings", "reboot", "poweroff" };

        // Configuration State
        private bool _isDarkMode = false;
        private bool _autoLocation = false;
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
            RegisterApp(new WeatherForecastApp());
            RegisterApp(new LunarForecastApp());
            RegisterApp(new ChangeCityApp());
            RegisterApp(new ClockApp());
            RegisterApp(new CalculatorApp());
            RegisterApp(new NotepadApp());
            RegisterApp(new DiskManagerApp());
            RegisterApp(new SystemInfoApp());
            RegisterApp(new NetworkConfigApp());
            RegisterApp(new NetTransferApp());
            
            var settingsApp = new SettingsApp();
            settingsApp.InitializeBuffers(_defaultCity, _customApiKey);
            RegisterApp(settingsApp);

            RegisterApp(new SystemResourcesApp());
            RegisterApp(new DebugMenuApp());
            RegisterApp(new DiskDiagnosticsApp());
            RegisterApp(new BatteryDiagnosticsApp());
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
                    "Weather Dashboard", "7-Day Weather Forecast", "Lunar Observatory", "Change City", "Digital Clock", "Calculator",
                    "Notepad", "File & Storage Explorer", "System Information", "Network Config",
                    "Debug Menu [Diagnostics]", "Settings", "Reboot System", "Poweroff System"
                };
            }
            return new string[] {
                "Weather Dashboard", "7-Day Weather Forecast", "Lunar Observatory", "Change City", "Digital Clock", "Calculator",
                "Notepad", "File & Storage Explorer", "System Information", "Network Config",
                "Settings", "Reboot System", "Poweroff System"
            };
        }

        public string[] GetAppIds()
        {
            if (_showDebugLogs)
            {
                return new string[] {
                    "weather", "forecast_7d", "lunar_forecast", "change_city", "clock", "calculator",
                    "notepad", "diskmgr", "about", "ipconfig",
                    "debug_menu", "settings", "reboot", "poweroff"
                };
            }
            return new string[] {
                "weather", "forecast_7d", "lunar_forecast", "change_city", "clock", "calculator",
                "notepad", "diskmgr", "about", "ipconfig",
                "settings", "reboot", "poweroff"
            };
        }

        public void SetCurrentApp(string appId)
        {
            _currentApp = appId;
            if (appId == "diskmgr")
            {
                var dm = GetApp("diskmgr") as WeatherOS.Apps.DiskManagerApp;
                dm?.Refresh();
            }
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
            System.Console.Clear();
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

                            if (KeyboardManager.TryReadKey(out var keyEvent))
                            {
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
                
                if (_currentApp == "notepad" || _currentApp == "transfer")
                {
                    IApp curApp = GetApp(_currentApp);
                    curApp?.HandleInput(this, keyEvent);
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

        public List<LauncherAppEntry> GetLeftLauncherItems()
        {
            var list = new List<LauncherAppEntry>();
            // Category: Meteorology & Forecast
            list.Add(new LauncherAppEntry { Id = "weather", Name = "Weather Dashboard", Description = "Real-time weather station telemetry, temp & humidity.", Category = "METEOROLOGY" });
            list.Add(new LauncherAppEntry { Id = "forecast_7d", Name = "7-Day Forecast", Description = "Multi-day meteorological forecast and climate trends.", Category = "METEOROLOGY" });
            list.Add(new LauncherAppEntry { Id = "lunar_forecast", Name = "Lunar Observatory", Description = "Astronomical calculations, lunar phases and illumination.", Category = "METEOROLOGY" });
            list.Add(new LauncherAppEntry { Id = "change_city", Name = "Change Location", Description = "Configure target city and station coordinates.", Category = "METEOROLOGY" });

            // Category: Dedicated Networking & File Sync
            list.Add(new LauncherAppEntry { Id = "transfer", Name = "File Transfer & OTA", Description = "Send/Receive files and OS updates over network (UDP 7331).", Category = "NETWORKING" });
            list.Add(new LauncherAppEntry { Id = "ipconfig", Name = "Network Config", Description = "Ethernet NIC configuration, IP, gateway and DNS info.", Category = "NETWORKING" });

            // Category: Utilities & Tools
            list.Add(new LauncherAppEntry { Id = "notepad", Name = "Notepad (Editor)", Description = "Text editor with direct FAT32 disk & RAM store support.", Category = "UTILITIES" });
            list.Add(new LauncherAppEntry { Id = "calculator", Name = "Calculator", Description = "Arithmetic calculations, trigonometry & scientific math.", Category = "UTILITIES" });
            list.Add(new LauncherAppEntry { Id = "clock", Name = "Digital Clock", Description = "Live hardware RTC real-time clock and calendar display.", Category = "UTILITIES" });
            list.Add(new LauncherAppEntry { Id = "settings", Name = "System Settings", Description = "Configure dark mode theme, default station and options.", Category = "UTILITIES" });

            return list;
        }

        public List<LauncherAppEntry> GetRightLauncherItems()
        {
            var list = new List<LauncherAppEntry>();
            // Category: Storage & System
            list.Add(new LauncherAppEntry { Id = "diskmgr", Name = "File Explorer", Description = "Browse FAT32 disk partitions, files & RAM virtual store.", Category = "STORAGE & SYSTEM" });
            list.Add(new LauncherAppEntry { Id = "diskdiag", Name = "Storage Diagnostics", Description = "Hardware ATA/AHCI scan, automated self-test & partition info.", Category = "STORAGE & SYSTEM" });
            list.Add(new LauncherAppEntry { Id = "resources", Name = "System Resources", Description = "Real-time CPU telemetry, memory heap and system load.", Category = "STORAGE & SYSTEM" });
            list.Add(new LauncherAppEntry { Id = "battery", Name = "Battery & Power", Description = "AC adapter status, battery percentage & power health.", Category = "STORAGE & SYSTEM" });
            list.Add(new LauncherAppEntry { Id = "about", Name = "System Information", Description = "Cosmos kernel specifications, OS build & hardware summary.", Category = "STORAGE & SYSTEM" });

            // Category: System Power
            list.Add(new LauncherAppEntry { Id = "reboot", Name = "Reboot System", Description = "Safely restart the computer and reload WeatherOS.", Category = "POWER & SYSTEM" });
            list.Add(new LauncherAppEntry { Id = "poweroff", Name = "Power Off Computer", Description = "Safely shut down computer via ACPI power control.", Category = "POWER & SYSTEM" });

            if (_showDebugLogs)
            {
                list.Add(new LauncherAppEntry { Id = "debug_menu", Name = "Debug Menu & Logs", Description = "Low-level system diagnostics, driver tests & kernel logs.", Category = "POWER & SYSTEM" });
            }

            return list;
        }

        private void HandleLauncherInput(KeyEvent keyEvent)
        {
            var leftItems = GetLeftLauncherItems();
            var rightItems = GetRightLauncherItems();

            if (keyEvent.Key == ConsoleKeyEx.UpArrow)
            {
                if (_launcherColumn == 0)
                {
                    _launcherLeftIndex--;
                    if (_launcherLeftIndex < 0) _launcherLeftIndex = leftItems.Count - 1;
                }
                else
                {
                    _launcherRightIndex--;
                    if (_launcherRightIndex < 0) _launcherRightIndex = rightItems.Count - 1;
                }
            }
            else if (keyEvent.Key == ConsoleKeyEx.DownArrow)
            {
                if (_launcherColumn == 0)
                {
                    _launcherLeftIndex++;
                    if (_launcherLeftIndex >= leftItems.Count) _launcherLeftIndex = 0;
                }
                else
                {
                    _launcherRightIndex++;
                    if (_launcherRightIndex >= rightItems.Count) _launcherRightIndex = 0;
                }
            }
            else if (keyEvent.Key == ConsoleKeyEx.Tab)
            {
                _launcherColumn = 1 - _launcherColumn;
            }
            else if (keyEvent.Key == ConsoleKeyEx.RightArrow)
            {
                if (_launcherColumn == 0)
                {
                    _launcherColumn = 1;
                    if (_launcherRightIndex >= rightItems.Count) _launcherRightIndex = rightItems.Count - 1;
                }
            }
            else if (keyEvent.Key == ConsoleKeyEx.LeftArrow)
            {
                if (_launcherColumn == 1)
                {
                    _launcherColumn = 0;
                    if (_launcherLeftIndex >= leftItems.Count) _launcherLeftIndex = leftItems.Count - 1;
                }
            }
            else if (keyEvent.Key == ConsoleKeyEx.Enter)
            {
                LauncherAppEntry selected = _launcherColumn == 0
                    ? leftItems[_launcherLeftIndex]
                    : rightItems[_launcherRightIndex];

                if (selected.Id == "reboot")
                {
                    Cosmos.System.Power.Reboot();
                }
                else if (selected.Id == "poweroff" || selected.Id == "halt")
                {
                    Cosmos.System.Power.Shutdown();
                }
                else
                {
                    SetCurrentApp(selected.Id);
                }
            }
        }

        private void DrawDesktop()
        {
            System.Console.CursorVisible = false;

            ConsoleColor bgDesktop = _isDarkMode ? ConsoleColor.Black : ConsoleColor.DarkBlue;
            System.Console.BackgroundColor = bgDesktop;
            
            // Top Bar (Row 0)
            string topText;
            if (_currentApp == "launcher")
            {
                topText = " WeatherOS Desktop  |  Categorized Meteorological System";
            }
            else if (_currentApp == "notepad")
            {
                topText = " WeatherOS > Notepad  |  [F2] Save Note  |  [ESC] Menu / Desktop";
            }
            else
            {
                IApp curAppObj = GetApp(_currentApp);
                string appTitle = curAppObj != null ? curAppObj.Name : _currentApp;
                topText = $" WeatherOS > {appTitle}  |  [ESC] Return to Desktop";
            }
            Screen.WriteClipped(0, 0, topText, Screen.Width - 1, ConsoleColor.Black, ConsoleColor.Gray, true);

            // Clean background rows (1 to 23)
            for (int y = 1; y < Screen.Height - 1; y++)
            {
                Screen.ClearLine(y, bgDesktop);
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

            // Bottom Bar (Row 24)
            string btmText;
            if (_currentApp == "launcher")
            {
                btmText = " [Arrows/Tab] Navigate Categories  |  [Enter] Open Application  |  [ESC] Exit";
            }
            else
            {
                btmText = " WeatherOS v3.0  |  Cosmos x86 Dedicated Meteorological Kernel";
            }
            Screen.WriteClipped(0, Screen.Height - 1, btmText, Screen.Width - 1, ConsoleColor.Black, ConsoleColor.Gray, true);
            System.Console.ResetColor();
        }

        private void DrawLauncher()
        {
            var leftItems = GetLeftLauncherItems();
            var rightItems = GetRightLauncherItems();

            if (_launcherLeftIndex >= leftItems.Count) _launcherLeftIndex = 0;
            if (_launcherRightIndex >= rightItems.Count) _launcherRightIndex = 0;

            int left = 2;
            int top = 1;
            int width = 76;
            int height = 22;

            DrawWindow(left, top, width, height, "WEATHEROS WORKSPACE  |  APPLICATION LAUNCHER");
            ConsoleColor winBg = _isDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;

            // Subheader
            WriteInsideWindow(left, width, top + 1, 1, "Select an application by category or press [Tab / Arrows] to navigate:", ConsoleColor.White, winBg);

            // Vertical middle divider
            for (int y = top + 2; y <= top + 14; y++)
            {
                System.Console.SetCursorPosition(38, y);
                System.Console.ForegroundColor = ConsoleColor.DarkCyan;
                System.Console.Write("|");
            }

            // --- LEFT COLUMN ---
            // Category 1: Meteorology & Forecast (items 0 to 3)
            WriteInsideWindow(left, 36, top + 2, 1, "[ METEOROLOGY & FORECAST ]", ConsoleColor.Yellow, winBg);

            for (int i = 0; i < 4 && i < leftItems.Count; i++)
            {
                int rowY = top + 3 + i;
                bool isSel = (_launcherColumn == 0 && _launcherLeftIndex == i);
                DrawLauncherItemRow(left + 2, rowY, leftItems[i].Name, isSel, 33, winBg);
            }

            // Category 2: Networking & File Sync (items 4 to 5)
            WriteInsideWindow(left, 36, top + 7, 1, "[ NETWORKING & FILE SYNC ]", ConsoleColor.Yellow, winBg);

            for (int i = 4; i < 6 && i < leftItems.Count; i++)
            {
                int rowY = top + 8 + (i - 4);
                bool isSel = (_launcherColumn == 0 && _launcherLeftIndex == i);
                DrawLauncherItemRow(left + 2, rowY, leftItems[i].Name, isSel, 33, winBg);
            }

            // Category 3: Utilities & Tools (items 6 to end)
            WriteInsideWindow(left, 36, top + 10, 1, "[ UTILITIES & TOOLS ]", ConsoleColor.Yellow, winBg);

            for (int i = 6; i < leftItems.Count; i++)
            {
                int rowY = top + 11 + (i - 6);
                if (rowY <= top + 14)
                {
                    bool isSel = (_launcherColumn == 0 && _launcherLeftIndex == i);
                    DrawLauncherItemRow(left + 2, rowY, leftItems[i].Name, isSel, 33, winBg);
                }
            }

            // --- RIGHT COLUMN ---
            // Category 4: Storage & System (items 0 to 4)
            WriteClipped(40, top + 2, "[ STORAGE & SYSTEM ]", 34, ConsoleColor.Yellow, winBg);

            for (int i = 0; i < 5 && i < rightItems.Count; i++)
            {
                int rowY = top + 3 + i;
                bool isSel = (_launcherColumn == 1 && _launcherRightIndex == i);
                DrawLauncherItemRow(40, rowY, rightItems[i].Name, isSel, 33, winBg);
            }

            // Category 5: System Power (items 5 to end)
            WriteClipped(40, top + 8, "[ SYSTEM POWER ]", 34, ConsoleColor.Yellow, winBg);

            for (int i = 5; i < rightItems.Count; i++)
            {
                int rowY = top + 9 + (i - 5);
                if (rowY <= top + 14)
                {
                    bool isSel = (_launcherColumn == 1 && _launcherRightIndex == i);
                    DrawLauncherItemRow(40, rowY, rightItems[i].Name, isSel, 33, winBg);
                }
            }

            // Horizontal Divider before details
            System.Console.BackgroundColor = winBg;
            System.Console.SetCursorPosition(left + 1, top + 15);
            System.Console.ForegroundColor = ConsoleColor.DarkCyan;
            System.Console.Write(new string('-', width - 2));

            // Selected App Details Box
            LauncherAppEntry currentSelected = _launcherColumn == 0
                ? leftItems[_launcherLeftIndex]
                : rightItems[_launcherRightIndex];

            string selHeader = $"Selected: {currentSelected.Name}  [{currentSelected.Category}]";
            WriteInsideWindow(left, width, top + 16, 1, selHeader, ConsoleColor.Yellow, winBg);

            string desc = currentSelected.Description;
            WriteInsideWindow(left, width, top + 17, 1, desc, ConsoleColor.Gray, winBg);

            // Horizontal Divider before controls
            System.Console.SetCursorPosition(left + 1, top + 18);
            System.Console.ForegroundColor = ConsoleColor.DarkCyan;
            System.Console.Write(new string('-', width - 2));

            // Bottom Navigation Help (clean ASCII, perfectly clipped)
            WriteInsideWindow(left, width, top + 19, 1, "[Up/Dn] Move  |  [Tab] Switch Col  |  [Enter] Launch  |  [ESC] Exit", ConsoleColor.Yellow, winBg);
        }

        private void DrawLauncherItemRow(int x, int y, string text, bool isSelected, int maxWidth, ConsoleColor defaultBg)
        {
            if (isSelected)
            {
                string line = " > " + text;
                Screen.WriteClipped(x, y, line, maxWidth, ConsoleColor.Black, ConsoleColor.White, true);
            }
            else
            {
                string line = "   " + text;
                ConsoleColor fg = text.Contains("Debug") ? ConsoleColor.Yellow : ConsoleColor.White;
                Screen.WriteClipped(x, y, line, maxWidth, fg, defaultBg, true);
            }
        }

        public void WriteClipped(int x, int y, string text, int maxAllowedWidth, ConsoleColor? fg = null, ConsoleColor? bg = null, bool padToWidth = false)
        {
            Screen.WriteClipped(x, y, text, maxAllowedWidth, fg, bg, padToWidth);
        }

        public void WriteInsideWindow(int winLeft, int winWidth, int y, int padLeft, string text, ConsoleColor? fg = null, ConsoleColor? bg = null, bool fillRemaining = false)
        {
            Screen.WriteInsideWindow(winLeft, winWidth, y, padLeft, text, fg, bg, fillRemaining);
        }

        public TuiWindow CreateWindow(string title, WindowSize size = WindowSize.Medium)
        {
            ConsoleColor winBg = _isDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            ConsoleColor borderCol = _isDarkMode ? ConsoleColor.DarkCyan : ConsoleColor.White;
            var win = new TuiWindow(title, size, winBg, borderCol, ConsoleColor.Yellow);
            win.DrawFrame();
            return win;
        }

        public TuiWindow CreateWindow(string title, int preferredWidth, int preferredHeight)
        {
            ConsoleColor winBg = _isDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            ConsoleColor borderCol = _isDarkMode ? ConsoleColor.DarkCyan : ConsoleColor.White;
            var win = new TuiWindow(title, preferredWidth, preferredHeight, winBg, borderCol, ConsoleColor.Yellow);
            win.DrawFrame();
            return win;
        }

        public void DrawWindow(int left, int top, int width, int height, string title)
        {
            ConsoleColor winBg = _isDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            ConsoleColor borderCol = _isDarkMode ? ConsoleColor.DarkCyan : ConsoleColor.White;
            Screen.DrawBox(left, top, width, height, title, winBg, borderCol, ConsoleColor.Yellow);
        }

        public void DrawCenteredWindow(int requestedWidth, int requestedHeight, string title, out int left, out int top)
        {
            int width, height;
            Screen.CalculateWindow(requestedWidth, requestedHeight, out left, out top, out width, out height);
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
            int hour = DateTime.UtcNow.Hour;
            bool isNight = (hour < 7 || hour >= 20) || (condition != null && condition.ToLower().Contains("night"));
            DrawWeatherIcon(condition, isNight, "", left, top);
        }

        public void DrawWeatherIcon(string condition, bool isNight, string moonPhase, int left, int top)
        {
            string cond = (condition ?? "").ToLower();
            string phase = (moonPhase ?? "").ToLower();

            // 1. Thunderstorm / Tempestade
            if (cond.Contains("thunder") || cond.Contains("storm") || cond.Contains("lightning"))
            {
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("     .-^^---.   ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("    (________)  ");
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("       / _/     ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("        /       ");
            }
            // 2. Rain / Aguaceiros
            else if (cond.Contains("rain") || cond.Contains("drizzle") || cond.Contains("shower"))
            {
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("     .-^^---.   ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("    (________)  ");
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("     /  /  /    ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("    /  /  /     ");
            }
            // 3. Snow / Neve
            else if (cond.Contains("snow") || cond.Contains("sleet") || cond.Contains("ice") || cond.Contains("blizzard"))
            {
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("     .-^^---.   ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("    (________)  ");
                System.Console.ForegroundColor = ConsoleColor.White;
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("     *   *   *  ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("       *   *    ");
            }
            // 4. Fog / Mist / Neblina
            else if (cond.Contains("fog") || cond.Contains("mist") || cond.Contains("haze"))
            {
                System.Console.ForegroundColor = ConsoleColor.Gray;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("   _ - _ - _ -  ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("    _ - _ - _   ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("   - _ - _ - _  ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("    _ - _ - _   ");
            }
            // 5. Wind / Vento
            else if (cond.Contains("wind") || cond.Contains("breeze") || cond.Contains("gale"))
            {
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("   ~~~~~~  --~  ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("  ~~~~  -~ --~  ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("      ~~~~~~    ");
            }
            // 6. Night - Cloudy (Lua com Nuvens)
            else if (isNight && (cond.Contains("cloud") || cond.Contains("overcast") || cond.Contains("part")))
            {
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("     .---.      ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("    /  /  .--.  ");
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("  .-(    ).  )  ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write(" (___.__)__)_)  ");
            }
            // 7. Night - Clear / Moon (Lua de Noite de acordo com a fase lunar exata)
            else if (isNight || cond.Contains("moon") || cond.Contains("night") || cond.Contains("noite"))
            {
                DrawMoonPhaseIcon(moonPhase, left, top);
            }
            // 8. Day - Partly Cloudy (Sol com Nuvens)
            else if (cond.Contains("part") || (cond.Contains("sun") && cond.Contains("cloud")))
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("   \\  /         ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write(" _-(  )  .--.   ");
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("   .-(   ).  )  ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("  (___.__)__)_) ");
            }
            // 9. Day - Clouds / Overcast (Nublado)
            else if (cond.Contains("cloud") || cond.Contains("overcast"))
            {
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("      .--.      ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("   .-(    ).    ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("  (___.__)__)   ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("  (__________)  ");
            }
            // 10. Day - Sunny / Clear Sky (Sol Radiante)
            else
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("    \\   |   /   ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("      .-.       ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("   --(   )--    ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("      `-'       ");
                System.Console.SetCursorPosition(left, top + 4); System.Console.Write("    /   |   \\   ");
            }
        }

        public void DrawMoonPhaseIcon(string moonPhase, int left, int top)
        {
            string p = (moonPhase ?? "").ToLower();

            // 1. New Moon (Lua Nova)
            if (p.Contains("new") || p.Contains("nova"))
            {
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("    .-----.  *  ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("   / . . . \\    ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("  | . . . . |   ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("   \\ . . . /  * ");
                System.Console.SetCursorPosition(left, top + 4); System.Console.Write(" *  `-----'     ");
            }
            // 2. Waxing Crescent (Lua Crescente)
            else if (p.Contains("waxing crescent") || (p.Contains("crescent") && !p.Contains("waning")) || p.Contains("crescente"))
            {
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("     .---)  *   ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("    /   /       ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("   |   |     *  ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("    \\   \\       ");
                System.Console.SetCursorPosition(left, top + 4); System.Console.Write("  *  `---)   .  ");
            }
            // 3. First Quarter (Quarto Crescente)
            else if (p.Contains("first quarter") || p.Contains("first") || p.Contains("1st"))
            {
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("     .---|      ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("    / . .|##  * ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write(" * | . . |##    ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("    \\ . .|##    ");
                System.Console.SetCursorPosition(left, top + 4); System.Console.Write("     `---|   .  ");
            }
            // 4. Waxing Gibbous (Gibosa Crescente)
            else if (p.Contains("waxing gibbous") || (p.Contains("gibbous") && !p.Contains("waning")))
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("    .-----.     ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("   / (#####\\  * ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("  | (#######|   ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("   \\ (#####/  * ");
                System.Console.SetCursorPosition(left, top + 4); System.Console.Write(" *  `-----'     ");
            }
            // 5. Full Moon (Lua Cheia)
            else if (p.Contains("full") || p.Contains("cheia"))
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("    .-----.  *  ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("   /  O O  \\    ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write(" * |    O    |  ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("    \\       / * ");
                System.Console.SetCursorPosition(left, top + 4); System.Console.Write("     `-----'    ");
            }
            // 6. Waning Gibbous (Gibosa Minguante)
            else if (p.Contains("waning gibbous"))
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("    .-----.  *  ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write(" * /#####) \\    ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("  |#######) |   ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("   \\#####) /  * ");
                System.Console.SetCursorPosition(left, top + 4); System.Console.Write("    `-----'     ");
            }
            // 7. Last Quarter / Third Quarter (Quarto Minguante)
            else if (p.Contains("last quarter") || p.Contains("third quarter") || p.Contains("last") || p.Contains("third"))
            {
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("      .---.     ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("  *  /###|. \\   ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("    |####|.  | *");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("     \\###|. /   ");
                System.Console.SetCursorPosition(left, top + 4); System.Console.Write("      `---'     ");
            }
            // 8. Waning Crescent (Lua Minguante)
            else
            {
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("  *  (---.      ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("      \\   \\     ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("   *   |   |    ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("      /   /   * ");
                System.Console.SetCursorPosition(left, top + 4); System.Console.Write("     (---'   .  ");
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
