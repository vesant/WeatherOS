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

        public static int RealCpuUsagePct = 2;

        private static ulong _activeTimeAccumulator = 0;

        private static ulong _idleTimeAccumulator = 0;
        private bool _isActive;
        private string _currentApp = "launcher";
        private bool _isCommandPopupOpen = false;
        private WeatherService _weatherService;
        private string _apiKey = "";

        // App Registry
        private Dictionary<string, IApp> _registeredApps = new Dictionary<string, IApp>();

        // Launcher Categorized State
        private int _selectedAppIndex = 0;
        private int _launcherColumn = 0; // 0=Left, 1=Right
        private int _launcherLeftIndex = 0;
        private int _launcherRightIndex = 0;
        private string[] _apps = { "Weather Dashboard", "World Weather Map", "7-Day Weather Forecast", "Lunar Observatory", "Change City", "Digital Clock", "Calculator", "Notepad", "File & Storage Explorer", "System Information", "Network Config", "Settings", "Reboot System", "Poweroff System" };
        private string[] _appIds = { "weather", "world_map", "forecast_7d", "lunar_forecast", "change_city", "clock", "calculator", "notepad", "diskmgr", "about", "ipconfig", "settings", "reboot", "poweroff" };

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
        public bool IsCommandPopupOpen { get => _isCommandPopupOpen; set => _isCommandPopupOpen = value; }

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
            RegisterApp(new WorldWeatherMapApp());
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
            RegisterApp(new BatteryDiagnosticsApp());
            RegisterApp(new InstallerApp());
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
                        if (line.StartsWith("TimeFormat="))
                        {
                            string tf = line.Substring(11).Trim();
                            TimeService.Is24HourFormat = (tf == "24" || tf.ToLower() == "24h");
                        }
                        if (line.StartsWith("Is24Hour="))
                        {
                            TimeService.Is24HourFormat = line.Substring(9).Trim() == "true";
                        }
                        if (line.StartsWith("UtcOffset="))
                        {
                            int tz;
                            if (int.TryParse(line.Substring(10).Trim(), out tz))
                            {
                                TimeService.TimezoneOffset = tz;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        public void SaveSettings()
        {
            string cfg = $"DarkMode={(_isDarkMode ? "true" : "false")}\n" +
                         $"AutoLoc={(_autoLocation ? "true" : "false")}\n" +
                         $"DefaultCity={_defaultCity}\n" +
                         $"DebugLog={(_showDebugLogs ? "true" : "false")}\n" +
                         $"CustomKey={_customApiKey}\n" +
                         $"TimeFormat={(TimeService.Is24HourFormat ? "24" : "12")}\n" +
                         $"UtcOffset={TimeService.TimezoneOffset}\n";
            string status;
            StorageService.SaveFile("settings.ini", cfg, out status);

            // Saved instantly to RAM store and VFS without blocking hardware bus
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
                    "Weather Dashboard", "World Weather Map", "7-Day Weather Forecast", "Lunar Observatory", "Change City", "Digital Clock", "Calculator",
                    "Notepad", "File & Storage Explorer", "System Information", "Network Config",
                    "Debug Menu [Diagnostics]", "Settings", "Reboot System", "Poweroff System"
                };
            }
            return new string[] {
                "Weather Dashboard", "World Weather Map", "7-Day Weather Forecast", "Lunar Observatory", "Change City", "Digital Clock", "Calculator",
                "Notepad", "File & Storage Explorer", "System Information", "Network Config",
                "Settings", "Reboot System", "Poweroff System"
            };
        }

        public string[] GetAppIds()
        {
            if (_showDebugLogs)
            {
                return new string[] {
                    "weather", "world_map", "forecast_7d", "lunar_forecast", "change_city", "clock", "calculator",
                    "notepad", "diskmgr", "about", "ipconfig",
                    "debug_menu", "settings", "reboot", "poweroff"
                };
            }
            return new string[] {
                "weather", "world_map", "forecast_7d", "lunar_forecast", "change_city", "clock", "calculator",
                "notepad", "diskmgr", "about", "ipconfig",
                "settings", "reboot", "poweroff"
            };
        }

        public void SetCurrentApp(string appId)
        {
            _isCommandPopupOpen = false;
            if (appId != "world_map")
            {
                WeatherOS.Apps.WorldWeatherMapApp.ExitFullScreenBroadcastMode();
            }
            _currentApp = appId;
            if (appId == "diskmgr")
            {
                var dm = GetApp("diskmgr") as WeatherOS.Apps.DiskManagerApp;
                dm?.Refresh();
            }
            else if (appId == "settings")
            {
                var st = GetApp("settings") as WeatherOS.Apps.SettingsApp;
                st?.RefreshBuffers(this);
            }
            else if (appId == "world_map")
            {
                var wm = GetApp("world_map") as WeatherOS.Apps.WorldWeatherMapApp;
                wm?.SyncRegionFromSettings(this);
            }
        }

        public void StartApp(string appId)
        {
            _isCommandPopupOpen = false;
            _currentApp = appId;
            _isActive = true;
            RunLoop();
        }

        public void Start()
        {
            _isCommandPopupOpen = false;
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
            DrainKeyboardBuffer();
            HardwareDelayMs(100);
            DrainKeyboardBuffer();
            System.Console.Clear();
            try
            {
                while (_isActive)
                {
                    DrawDesktop();

                    bool isRealTimeApp = false;
                    IApp currentAppObj = GetApp(_currentApp);
                    if (currentAppObj != null && currentAppObj.IsRealTime && !_isCommandPopupOpen)
                    {
                        isRealTimeApp = true;
                    }

                    if (isRealTimeApp)
                    {
                        int startY, startM, startD, startH, startMin, startSec;
                        TimeService.GetCurrentLocalTime(out startY, out startM, out startD, out startH, out startMin, out startSec);

                        for (int i = 0; i < 80; i++)
                        {
                            // Actively poll network hardware in real time
                            WeatherOS.Kernel.CustomNIC?.HackPoll();

                            if (TryReadDesktopKey(out var keyEvent))
                            {
                                ProcessKeyEvent(keyEvent);
                                break;
                            }
                            HardwareDelayMs(15);
                            TimeService.Tick(15);
                            if (_currentApp != "world_map") DrawMiniClock(force: false);

                            int curY, curM, curD, curH, curMin, curSec;
                            TimeService.GetCurrentLocalTime(out curY, out curM, out curD, out curH, out curMin, out curSec);
                            if (curSec != startSec)
                            {
                                break;
                            }
                        }
                    }
                    else
                    {
                        // Non-blocking dual-path keyboard poll with zero freeze
                        KeyEvent keyEvent = default;
                        bool hasKey = false;
                        while (_isActive && !hasKey)
                        {
                            WeatherOS.Kernel.CustomNIC?.HackPoll();
                            if (TryReadDesktopKey(out keyEvent))
                            {
                                hasKey = true;
                                break;
                            }
                            HardwareDelayMs(15);
                            TimeService.Tick(15);
                            if (_currentApp != "world_map") DrawMiniClock(force: false);
                        }

                        if (_isActive && hasKey)
                        {
                            ProcessKeyEvent(keyEvent);
                        }
                    }
                }
            }
            finally
            {
                WeatherOS.Apps.WorldWeatherMapApp.ExitFullScreenBroadcastMode();
                IsGuiActive = false;
                DrainKeyboardBuffer();
                HardwareDelayMs(100);
                DrainKeyboardBuffer();
            }

            System.Console.Clear();
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine("[sys] tty1: Terminated graphical session.\n");
            System.Console.ResetColor();
        }

        private bool IsTextInputApp(string appId)
        {
            return appId == "change_city" || appId == "notepad" || appId == "settings" || appId == "transfer";
        }

        private void ProcessKeyEvent(KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.LWin || keyEvent.Key == ConsoleKeyEx.RWin)
            {
                _isCommandPopupOpen = false;
                if (_currentApp == "settings") SaveSettings();
                _isActive = false;
                DrainKeyboardBuffer();
                HardwareDelayMs(150);
                DrainKeyboardBuffer();
                return;
            }

            // If the Main Commands Popup is currently open, close it on ESC / Enter / Space / F1 / H / M / ?
            if (_isCommandPopupOpen)
            {
                if (keyEvent.Key == ConsoleKeyEx.Escape ||
                    keyEvent.Key == ConsoleKeyEx.Enter ||
                    keyEvent.Key == ConsoleKeyEx.Spacebar ||
                    keyEvent.Key == ConsoleKeyEx.F1 ||
                    keyEvent.KeyChar == 'h' || keyEvent.KeyChar == 'H' ||
                    keyEvent.KeyChar == 'm' || keyEvent.KeyChar == 'M' ||
                    keyEvent.KeyChar == '?')
                {
                    _isCommandPopupOpen = false;
                }
                return;
            }

            // Global shortcut to open the Main Commands Popup for the active application:
            // [F1] opens it in every app; [H] or [?] also opens it in non-text-editor apps
            if (keyEvent.Key == ConsoleKeyEx.F1 ||
                (!IsTextInputApp(_currentApp) && (keyEvent.KeyChar == 'h' || keyEvent.KeyChar == 'H' || keyEvent.KeyChar == '?')))
            {
                _isCommandPopupOpen = true;
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                if (_currentApp == "settings") SaveSettings();
                
                if (_currentApp == "notepad" || _currentApp == "transfer" || _currentApp == "about" || _currentApp == "installer" || _currentApp == "world_map")
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
            // Category: Meteorology & Forecast (5 items)
            list.Add(new LauncherAppEntry { Id = "weather", Name = "Weather Dashboard", Description = "Real-time weather station telemetry, temp & humidity.", Category = "METEOROLOGY" });
            list.Add(new LauncherAppEntry { Id = "world_map", Name = "World Weather Map", Description = "Continental weather map with interactive meteorological layers.", Category = "METEOROLOGY" });
            list.Add(new LauncherAppEntry { Id = "forecast_7d", Name = "7-Day Forecast", Description = "Multi-day meteorological forecast and climate trends.", Category = "METEOROLOGY" });
            list.Add(new LauncherAppEntry { Id = "lunar_forecast", Name = "Lunar Observatory", Description = "Astronomical calculations, lunar phases and illumination.", Category = "METEOROLOGY" });
            list.Add(new LauncherAppEntry { Id = "change_city", Name = "Change Location", Description = "Configure target city and station coordinates.", Category = "METEOROLOGY" });

            // Category: Dedicated Networking & File Sync (2 items)
            list.Add(new LauncherAppEntry { Id = "transfer", Name = "File Transfer & OTA", Description = "Send/Receive files and OS updates over network (UDP 7331).", Category = "NETWORKING" });
            list.Add(new LauncherAppEntry { Id = "ipconfig", Name = "Network Config", Description = "Ethernet NIC configuration, IP, gateway and DNS info.", Category = "NETWORKING" });

            // Category: Utilities & Tools (3 items)
            list.Add(new LauncherAppEntry { Id = "notepad", Name = "Notepad (Editor)", Description = "Text editor with direct FAT32 disk & RAM store support.", Category = "UTILITIES" });
            list.Add(new LauncherAppEntry { Id = "calculator", Name = "Calculator", Description = "Arithmetic calculations, trigonometry & scientific math.", Category = "UTILITIES" });
            list.Add(new LauncherAppEntry { Id = "clock", Name = "Digital Clock", Description = "Live calibrated digital clock and calendar display.", Category = "UTILITIES" });

            return list;
        }

        public List<LauncherAppEntry> GetRightLauncherItems()
        {
            var list = new List<LauncherAppEntry>();
            // Category: Storage & System
            list.Add(new LauncherAppEntry { Id = "settings", Name = "System Settings", Description = "Configure dark mode theme, default station, clock and options.", Category = "STORAGE & SYSTEM" });
            list.Add(new LauncherAppEntry { Id = "diskmgr", Name = "File Explorer", Description = "Browse FAT32 disk partitions, files & RAM virtual store.", Category = "STORAGE & SYSTEM" });
            list.Add(new LauncherAppEntry { Id = "resources", Name = "System Resources", Description = "Real-time CPU telemetry, memory heap and system load.", Category = "STORAGE & SYSTEM" });
            list.Add(new LauncherAppEntry { Id = "battery", Name = "Battery & Power", Description = "AC adapter status, battery percentage & power health.", Category = "STORAGE & SYSTEM" });
            list.Add(new LauncherAppEntry { Id = "about", Name = "System Information", Description = "Kernel specs, changelog release notes & system details.", Category = "STORAGE & SYSTEM" });
            if (!UpdateManagerService.IsInstalledOnDisk())
            {
                list.Add(new LauncherAppEntry { Id = "installer", Name = "Install to Disk", Description = "Permanent setup wizard: partition FAT32 & install boot files.", Category = "STORAGE & SYSTEM" });
            }

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

            if (_currentApp == "world_map")
            {
                IApp mapApp = GetApp("world_map");
                if (mapApp != null)
                {
                    mapApp.Draw(this);
                    return;
                }
            }
            WeatherOS.Apps.WorldWeatherMapApp.ExitFullScreenBroadcastMode();

            ConsoleColor bgDesktop = _isDarkMode ? ConsoleColor.Black : ConsoleColor.DarkBlue;
            System.Console.BackgroundColor = bgDesktop;
            
            // Top Bar (Row 0)
            string topText;
            if (_currentApp == "launcher")
            {
                topText = " WeatherOS Desktop  |  Meteorological System  |  [F1] Main Commands Menu";
            }
            else
            {
                IApp curAppObj = GetApp(_currentApp);
                string appTitle = curAppObj != null ? curAppObj.Name : _currentApp;
                topText = $" WeatherOS > {appTitle}  |  [F1] Main Commands Menu  |  [ESC] Desktop";
            }
            Screen.WriteClipped(0, 0, topText, Screen.Width - 1, ConsoleColor.Black, ConsoleColor.Gray, true);

            // Clean background rows (1 to 23)
            for (int y = 1; y < Screen.Height - 1; y++)
            {
                Screen.ClearLine(y, bgDesktop);
            }

            // Clean any hardware diagnostic exception banner written to VGA row 1 (0x000B80A0)
            try
            {
                Screen.ClearLine(1, bgDesktop);
            }
            catch { }

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

            // If Main Commands Popup is open, render centered modal overlay on top of the active app
            if (_isCommandPopupOpen)
            {
                DrawCommandsPopup();
            }

            // Bottom Bar (Row 24) - clipped to column 68 so mini digital clock stays in bottom right corner
            string btmText;
            if (_currentApp == "launcher")
            {
                btmText = " [F1 / H] Main Commands Menu  |  [Enter] Open  |  [ESC] Exit";
            }
            else
            {
                btmText = " WeatherOS v3.4  |  [F1] Main Commands Menu  |  [ESC] Desktop";
            }
            Screen.WriteClipped(0, Screen.Height - 1, btmText, 68, ConsoleColor.Black, ConsoleColor.Gray, true);
            System.Console.ResetColor();

            // Overlay Mini Digital Clock in Bottom Right Corner (Rows 23 & 24, Cols 69..79)
            DrawMiniClock(force: true);
        }

        public void DrawCommandsPopup()
        {
            string appTitle;
            string[] commands;
            GetCommandsForCurrentApp(out appTitle, out commands);

            int popW = 66;
            int popH = Math.Min(18, commands.Length + 6);
            int popLeft = (Screen.Width - popW) / 2;
            int popTop = (Screen.Height - popH) / 2;

            ConsoleColor popBg = ConsoleColor.Black;
            Screen.DrawBox(popLeft, popTop, popW, popH, "MAIN COMMANDS MENU  |  " + appTitle.ToUpper(), popBg, ConsoleColor.Cyan, ConsoleColor.Yellow);

            Screen.WriteInsideWindow(popLeft, popW, popTop + 1, 2, "Available keyboard shortcuts & actions for this application:", ConsoleColor.Gray, popBg);
            Screen.WriteInsideWindow(popLeft, popW, popTop + 2, 1, new string('-', popW - 4), ConsoleColor.DarkCyan, popBg);

            int maxLines = popH - 6;
            for (int i = 0; i < commands.Length && i < maxLines; i++)
            {
                string line = commands[i];
                ConsoleColor lineCol = line.StartsWith("[") ? ConsoleColor.White : ConsoleColor.Cyan;
                Screen.WriteInsideWindow(popLeft, popW, popTop + 3 + i, 3, line, lineCol, popBg);
            }

            Screen.WriteInsideWindow(popLeft, popW, popTop + popH - 3, 1, new string('-', popW - 4), ConsoleColor.DarkCyan, popBg);
            Screen.WriteInsideWindow(popLeft, popW, popTop + popH - 2, 2, "[F1 / ESC / Enter] Close Commands Menu", ConsoleColor.Yellow, popBg);
        }

        public void GetCommandsForCurrentApp(out string appTitle, out string[] commands)
        {
            switch (_currentApp)
            {
                case "launcher":
                    appTitle = "Application Launcher";
                    commands = new string[]
                    {
                        "[Up / Down Arrows]   Move selection within active column",
                        "[Left / Right / Tab] Switch between Left & Right columns",
                        "[Enter]              Launch the selected application",
                        "[F1 / H / ?]         Open or close this Main Commands Menu",
                        "[ESC / Win Key]      Exit desktop session to system shell"
                    };
                    break;

                case "weather":
                    appTitle = "Weather Dashboard";
                    commands = new string[]
                    {
                        "[R]                  Refresh live telemetry from Weather API",
                        "[M]                  Open Full-Screen World Weather Map",
                        "[C]                  Change target city / station coordinates",
                        "[F]                  Open 7-Day Extended Weather Forecast",
                        "[L]                  Open Lunar Observatory & Moon Phases",
                        "[F1 / H]             Open or close this Main Commands Menu",
                        "[ESC]                Return to Application Launcher"
                    };
                    break;

                case "world_map":
                    appTitle = "World Weather Map";
                    commands = new string[]
                    {
                        "[L]                  Cycle Meteorological Map Layer (5 Layers)",
                        "[1..5]               Select Layer: 1=Icons 2=Temp 3=Rain 4=Wind 5=hPa",
                        "[Tab / C / Z]        Switch Continental Sector (5 Continents)",
                        "[Left/Right/Up/Dn]   Select Regional Weather Station on Map",
                        "[R]                  Sync Live Telemetry for Selected Station",
                        "[A]                  Sync Live Telemetry for All Region Stations",
                        "[Enter]              Open Selected Station in Weather Dashboard",
                        "[F1 / H / M]         Open or close this Main Commands Menu",
                        "[ESC]                Exit Broadcast Mode to Desktop Launcher"
                    };
                    break;

                case "forecast_7d":
                    appTitle = "7-Day Weather Forecast";
                    commands = new string[]
                    {
                        "[R]                  Refresh 7-Day Open-Meteo Telemetry",
                        "[C]                  Change Location / Target City",
                        "[W]                  Open Weather Dashboard for Current City",
                        "[M]                  Open World Weather Map",
                        "[F1 / H]             Open or close this Main Commands Menu",
                        "[ESC]                Return to Application Launcher"
                    };
                    break;

                case "lunar_forecast":
                    appTitle = "Lunar Observatory";
                    commands = new string[]
                    {
                        "[Left / Right]       Step backward or forward by 1 day",
                        "[Up / Down]          Jump backward or forward by 7 days",
                        "[T / R]              Reset to Today's Live UTC Ephemeris",
                        "[W]                  Return to Weather Dashboard",
                        "[F1 / H]             Open or close this Main Commands Menu",
                        "[ESC]                Return to Application Launcher"
                    };
                    break;

                case "change_city":
                    appTitle = "Change Location";
                    commands = new string[]
                    {
                        "[A..Z / 0..9 / ,]    Type city name or Lat,Lon coordinates",
                        "[Backspace]          Delete last character in search box",
                        "[Enter]              Fetch live weather & open Dashboard",
                        "[F1]                 Open or close this Main Commands Menu",
                        "[ESC]                Cancel and return to Desktop Launcher"
                    };
                    break;

                case "clock":
                    appTitle = "Digital Clock";
                    commands = new string[]
                    {
                        "[T]                  Toggle 24-Hour / 12-Hour (AM/PM) display",
                        "[+ / Right Arrow]    Increase UTC Timezone Offset (+1 hour)",
                        "[- / Left Arrow]     Decrease UTC Timezone Offset (-1 hour)",
                        "[S]                  Open System Settings",
                        "[F1 / H]             Open or close this Main Commands Menu",
                        "[ESC]                Return to Application Launcher"
                    };
                    break;

                case "calculator":
                    appTitle = "Scientific Calculator";
                    commands = new string[]
                    {
                        "[0..9 / .]           Enter numeric digits and decimal point",
                        "[+  -  *  /]         Standard arithmetic operations",
                        "[Enter / =]          Compute expression result",
                        "[S / O / T]          Trigonometric sin(x), cos(x), tan(x) (deg)",
                        "[Q / L / P]          sqrt(x), log10(x), and x^2 power",
                        "[C / Backspace]      Clear all (AC) or delete last digit",
                        "[F1 / H]             Open or close this Main Commands Menu",
                        "[ESC]                Return to Application Launcher"
                    };
                    break;

                case "notepad":
                    appTitle = "Notepad Text Editor";
                    commands = new string[]
                    {
                        "[Up / Down]          Navigate File Browser or Editor lines",
                        "[Enter]              Open selected file / New line in editor",
                        "[N] (Browser)        Create a new blank document",
                        "[D] (Browser)        Delete selected note from storage",
                        "[F2] (Editor)        Open Save File modal dialog",
                        "[F3] (Editor)        Open Load File picker dialog",
                        "[F1]                 Open or close this Main Commands Menu",
                        "[ESC]                Return to File Browser / Desktop"
                    };
                    break;

                case "diskmgr":
                    appTitle = "File & Storage Explorer";
                    commands = new string[]
                    {
                        "[Tab / Left / Right] Switch between Drives & Directory Files",
                        "[Up / Down Arrows]   Select drive, folder, or file entry",
                        "[Enter]              Open folder or inspect file preview",
                        "[O]                  Open selected text file in Notepad",
                        "[C / V]              Copy selected item / Paste from clipboard",
                        "[N / D]              Create new folder (mkdir) / Delete item",
                        "[R]                  Refresh storage volumes and file tree",
                        "[F1 / H]             Open or close this Main Commands Menu",
                        "[ESC]                Return to Application Launcher"
                    };
                    break;

                case "about":
                    appTitle = "System Information";
                    commands = new string[]
                    {
                        "[C]                  Open System Changelog & Release Notes",
                        "[Up / Down Arrows]   Scroll through version history in Changelog",
                        "[I] (on Live USB)    Launch Hard Disk Installer Wizard",
                        "[F1 / H]             Open or close this Main Commands Menu",
                        "[ESC]                Close Changelog or return to Desktop"
                    };
                    break;

                case "ipconfig":
                    appTitle = "Network Configuration";
                    commands = new string[]
                    {
                        "[R]                  Re-run DHCP Discover / Refresh NIC Link",
                        "[T]                  Open Network File Transfer & OTA App",
                        "[F1 / H]             Open or close this Main Commands Menu",
                        "[ESC]                Return to Application Launcher"
                    };
                    break;

                case "transfer":
                    appTitle = "File Transfer & OTA";
                    commands = new string[]
                    {
                        "[1 / R]              Enter Receive Mode (Listen on UDP 7331)",
                        "[2 / S]              Enter Send File Mode (Transmit to PC)",
                        "[Enter] (After Rx)   Install received .pkg update or save file",
                        "[Tab / Arrows]       Navigate fields in Send File mode",
                        "[F1]                 Open or close this Main Commands Menu",
                        "[ESC]                Cancel transfer or return to Desktop"
                    };
                    break;

                case "settings":
                    appTitle = "System Settings";
                    commands = new string[]
                    {
                        "[Up / Down Arrows]   Select configuration setting row",
                        "[Enter / Space]      Toggle boolean option or edit text field",
                        "[Left / Right]       Adjust UTC Timezone Offset (-12 to +14)",
                        "[F5]                 Reset API Key to built-in default",
                        "[F1]                 Open or close this Main Commands Menu",
                        "[ESC]                Save settings and return to Desktop"
                    };
                    break;

                case "resources":
                    appTitle = "System Resources";
                    commands = new string[]
                    {
                        "[G / C]              Trigger manual memory heap collection",
                        "[R]                  Refresh telemetry counters",
                        "[F1 / H]             Open or close this Main Commands Menu",
                        "[ESC]                Return to Application Launcher"
                    };
                    break;

                case "battery":
                    appTitle = "Battery & Power";
                    commands = new string[]
                    {
                        "[R]                  Refresh ACPI / EC power status registers",
                        "[F1 / H]             Open or close this Main Commands Menu",
                        "[ESC]                Return to Application Launcher"
                    };
                    break;

                case "debug_menu":
                    appTitle = "Debug Menu & Logs";
                    commands = new string[]
                    {
                        "[Left / Right / Tab] Switch diagnostic tab (1..4)",
                        "[Up / Down Arrows]   Scroll logs or select diagnostic test",
                        "[Enter]              Execute selected hardware/network test",
                        "[C]                  Clear diagnostic logs",
                        "[F1 / H]             Open or close this Main Commands Menu",
                        "[ESC]                Return to Application Launcher"
                    };
                    break;

                case "installer":
                    appTitle = "Hard Disk Installer";
                    commands = new string[]
                    {
                        "[Up / Down Arrows]   Select target physical ATA/IDE disk",
                        "[Enter]              Confirm selection / Start installation",
                        "[R]                  Retry installation after an error",
                        "[F1 / H]             Open or close this Main Commands Menu",
                        "[ESC]                Cancel setup and return to Desktop"
                    };
                    break;

                default:
                    appTitle = "WeatherOS Application";
                    commands = new string[]
                    {
                        "[Arrows / Tab]       Navigate interface controls",
                        "[Enter]              Confirm / Activate selection",
                        "[F1 / H]             Open or close this Main Commands Menu",
                        "[ESC]                Return to Application Launcher"
                    };
                    break;
            }
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
            WriteInsideWindow(left, width, top + 1, 1, "Select an application by category (Press [F1] for Main Commands Menu):", ConsoleColor.White, winBg);

            // Vertical middle divider
            for (int y = top + 2; y <= top + 14; y++)
            {
                System.Console.SetCursorPosition(38, y);
                System.Console.ForegroundColor = ConsoleColor.DarkCyan;
                System.Console.Write("|");
            }

            // --- LEFT COLUMN ---
            // Category 1: Meteorology & Forecast (items 0 to 4)
            WriteInsideWindow(left, 36, top + 2, 1, "[ METEOROLOGY & FORECAST ]", ConsoleColor.Yellow, winBg);

            for (int i = 0; i < 5 && i < leftItems.Count; i++)
            {
                int rowY = top + 3 + i;
                bool isSel = (_launcherColumn == 0 && _launcherLeftIndex == i);
                DrawLauncherItemRow(left + 2, rowY, leftItems[i].Name, isSel, 33, winBg);
            }

            // Category 2: Networking & File Sync (items 5 to 6)
            WriteInsideWindow(left, 36, top + 8, 1, "[ NETWORKING & FILE SYNC ]", ConsoleColor.Yellow, winBg);

            for (int i = 5; i < 7 && i < leftItems.Count; i++)
            {
                int rowY = top + 9 + (i - 5);
                bool isSel = (_launcherColumn == 0 && _launcherLeftIndex == i);
                DrawLauncherItemRow(left + 2, rowY, leftItems[i].Name, isSel, 33, winBg);
            }

            // Category 3: Utilities & Tools (items 7 to end)
            WriteInsideWindow(left, 36, top + 11, 1, "[ UTILITIES & TOOLS ]", ConsoleColor.Yellow, winBg);

            for (int i = 7; i < leftItems.Count; i++)
            {
                int rowY = top + 12 + (i - 7);
                if (rowY <= top + 14)
                {
                    bool isSel = (_launcherColumn == 0 && _launcherLeftIndex == i);
                    DrawLauncherItemRow(left + 2, rowY, leftItems[i].Name, isSel, 33, winBg);
                }
            }

            // --- RIGHT COLUMN ---
            // Category 4: Storage & System
            WriteClipped(40, top + 2, "[ STORAGE & SYSTEM ]", 34, ConsoleColor.Yellow, winBg);
            int storageY = top + 3;
            int powerY = top + 9;
            bool powerHeaderDrawn = false;

            for (int i = 0; i < rightItems.Count; i++)
            {
                bool isSel = (_launcherColumn == 1 && _launcherRightIndex == i);
                if (rightItems[i].Category == "STORAGE & SYSTEM")
                {
                    if (storageY <= top + 8)
                    {
                        DrawLauncherItemRow(40, storageY, rightItems[i].Name, isSel, 33, winBg);
                        storageY++;
                    }
                }
                else
                {
                    if (!powerHeaderDrawn)
                    {
                        WriteClipped(40, powerY, "[ SYSTEM POWER ]", 34, ConsoleColor.Yellow, winBg);
                        powerHeaderDrawn = true;
                    }
                    powerY++;
                    if (powerY <= top + 14)
                    {
                        DrawLauncherItemRow(40, powerY, rightItems[i].Name, isSel, 33, winBg);
                    }
                }
            }

            // Horizontal Divider before details
            System.Console.BackgroundColor = winBg;
            System.Console.SetCursorPosition(left + 1, top + 15);
            System.Console.ForegroundColor = ConsoleColor.DarkCyan;
            System.Console.Write(new string('-', width - 2));

            // Selected App Details Box
            LauncherAppEntry currentSelected = null;
            if (_launcherColumn == 0 && leftItems.Count > 0 && _launcherLeftIndex < leftItems.Count)
            {
                currentSelected = leftItems[_launcherLeftIndex];
            }
            else if (_launcherColumn == 1 && rightItems.Count > 0 && _launcherRightIndex < rightItems.Count)
            {
                currentSelected = rightItems[_launcherRightIndex];
            }

            if (currentSelected != null)
            {
                string selHeader = $"Selected: {currentSelected.Name}  [{currentSelected.Category}]";
                WriteInsideWindow(left, width, top + 16, 1, selHeader, ConsoleColor.Yellow, winBg);

                string desc = currentSelected.Description;
                WriteInsideWindow(left, width, top + 17, 1, desc, ConsoleColor.Gray, winBg);
            }

            // Horizontal Divider before controls
            System.Console.SetCursorPosition(left + 1, top + 18);
            System.Console.ForegroundColor = ConsoleColor.DarkCyan;
            System.Console.Write(new string('-', width - 2));

            // Unified Main Commands Menu Footer
            WriteInsideWindow(left, width, top + 19, 1, "[F1 / H] Main Commands Menu  |  [Enter] Launch App  |  [ESC] Exit", ConsoleColor.Yellow, winBg);
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

            // 1. Thunderstorm
            if (cond.Contains("thunder") || cond.Contains("storm") || cond.Contains("lightning"))
            {
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("     .-^^---.   ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("    (________)  ");
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("       / _/     ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("        /       ");
            }
            // 2. Rain / Showers
            else if (cond.Contains("rain") || cond.Contains("drizzle") || cond.Contains("shower"))
            {
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("     .-^^---.   ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("    (________)  ");
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("     /  /  /    ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("    /  /  /     ");
            }
            // 3. Snow
            else if (cond.Contains("snow") || cond.Contains("sleet") || cond.Contains("ice") || cond.Contains("blizzard"))
            {
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("     .-^^---.   ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("    (________)  ");
                System.Console.ForegroundColor = ConsoleColor.White;
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("     *   *   *  ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("       *   *    ");
            }
            // 4. Fog / Mist
            else if (cond.Contains("fog") || cond.Contains("mist") || cond.Contains("haze"))
            {
                System.Console.ForegroundColor = ConsoleColor.Gray;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("   _ - _ - _ -  ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("    _ - _ - _   ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("   - _ - _ - _  ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("    _ - _ - _   ");
            }
            // 5. Wind
            else if (cond.Contains("wind") || cond.Contains("breeze") || cond.Contains("gale"))
            {
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("   ~~~~~~  --~  ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("  ~~~~  -~ --~  ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("      ~~~~~~    ");
            }
            // 6. Night - Cloudy
            else if (isNight && (cond.Contains("cloud") || cond.Contains("overcast") || cond.Contains("part")))
            {
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("     .---.      ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("    /  /  .--.  ");
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("  .-(    ).  )  ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write(" (___.__)__)_)  ");
            }
            // 7. Night - Clear / Moon
            else if (isNight || cond.Contains("moon") || cond.Contains("night") || cond.Contains("noite"))
            {
                DrawMoonPhaseIcon(moonPhase, left, top);
            }
            // 8. Day - Partly Cloudy
            else if (cond.Contains("part") || (cond.Contains("sun") && cond.Contains("cloud")))
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("   \\  /         ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write(" _-(  )  .--.   ");
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("   .-(   ).  )  ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("  (___.__)__)_) ");
            }
            // 9. Day - Clouds / Overcast
            else if (cond.Contains("cloud") || cond.Contains("overcast"))
            {
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("      .--.      ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("   .-(    ).    ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("  (___.__)__)   ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("  (__________)  ");
            }
            // 10. Day - Sunny / Clear Sky
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

            // 1. New Moon
            if (p.Contains("new") || p.Contains("nova"))
            {
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("    .-----.  *  ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("   / . . . \\    ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("  | . . . . |   ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("   \\ . . . /  * ");
                System.Console.SetCursorPosition(left, top + 4); System.Console.Write(" *  `-----'     ");
            }
            // 2. Waxing Crescent
            else if (p.Contains("waxing crescent") || (p.Contains("crescent") && !p.Contains("waning")) || p.Contains("crescente"))
            {
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("     .---)  *   ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("    /   /       ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("   |   |     *  ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("    \\   \\       ");
                System.Console.SetCursorPosition(left, top + 4); System.Console.Write("  *  `---)   .  ");
            }
            // 3. First Quarter
            else if (p.Contains("first quarter") || p.Contains("first") || p.Contains("1st"))
            {
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("     .---|      ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("    / . .|##  * ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write(" * | . . |##    ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("    \\ . .|##    ");
                System.Console.SetCursorPosition(left, top + 4); System.Console.Write("     `---|   .  ");
            }
            // 4. Waxing Gibbous
            else if (p.Contains("waxing gibbous") || (p.Contains("gibbous") && !p.Contains("waning")))
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("    .-----.     ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("   / (#####\\  * ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("  | (#######|   ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("   \\ (#####/  * ");
                System.Console.SetCursorPosition(left, top + 4); System.Console.Write(" *  `-----'     ");
            }
            // 5. Full Moon
            else if (p.Contains("full") || p.Contains("cheia"))
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("    .-----.  *  ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("   /  O O  \\    ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write(" * |    O    |  ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("    \\       / * ");
                System.Console.SetCursorPosition(left, top + 4); System.Console.Write("     `-----'    ");
            }
            // 6. Waning Gibbous
            else if (p.Contains("waning gibbous"))
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("    .-----.  *  ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write(" * /#####) \\    ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("  |#######) |   ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("   \\#####) /  * ");
                System.Console.SetCursorPosition(left, top + 4); System.Console.Write("    `-----'     ");
            }
            // 7. Last Quarter / Third Quarter
            else if (p.Contains("last quarter") || p.Contains("third quarter") || p.Contains("last") || p.Contains("third"))
            {
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("      .---.     ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("  *  /###|. \\   ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("    |####|.  | *");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("     \\###|. /   ");
                System.Console.SetCursorPosition(left, top + 4); System.Console.Write("      `---'     ");
            }
            // 8. Waning Crescent
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

        private static int _lastMiniClockMin = -1;
        private static int _lastMiniClockDay = -1;

        public static void DrainKeyboardBuffer()
        {
            try
            {
                while (KeyboardManager.TryReadKey(out _)) { }
                var pStatus = new Cosmos.Core.IOPort(0x64);
                var pData = new Cosmos.Core.IOPort(0x60);
                for (int i = 0; i < 20; i++)
                {
                    if ((pStatus.Byte & 0x01) != 0)
                    {
                        byte _ = pData.Byte;
                    }
                    else break;
                }
                _ps2Extended = false;
            }
            catch { }
        }

        public static void DrawMiniClock(bool force = false)
        {
            try
            {
                int year, month, day, hour, minute, second;
                WeatherOS.Services.TimeService.GetCurrentLocalTime(out year, out month, out day, out hour, out minute, out second);

                if (!force && minute == _lastMiniClockMin && day == _lastMiniClockDay)
                {
                    return;
                }

                _lastMiniClockMin = minute;
                _lastMiniClockDay = day;

                string timeStr = $"   {hour:D2}:{minute:D2}  ";
                string dateStr = $"{day:D2}/{month:D2}/{year:D4} ";

                Screen.WriteClipped(69, 23, timeStr, 11, ConsoleColor.White, ConsoleColor.Black, true);
                Screen.WriteClipped(69, 24, dateStr, 11, ConsoleColor.White, ConsoleColor.Black, true);
            }
            catch { }
        }


        public static void HardwareDelayMs(int ms)
        {
            if (ms <= 0) return;
            try
            {
                var port = new Cosmos.Core.IOPort(0x80);
                int count = ms * 1000;
                for (int i = 0; i < count; i++)
                {
                    port.Byte = 0;
                }
            }
            catch
            {
                for (int i = 0; i < ms * 10000; i++) { }
            }
        }

        private static bool _ps2Extended = false;

        public static bool TryReadDesktopKey(out KeyEvent keyEvent)
        {
            keyEvent = default;

            // 1. Cosmos KeyboardManager FIFO queue
            try
            {
                if (KeyboardManager.TryReadKey(out keyEvent))
                {
                    return true;
                }
            }
            catch { }

            // 2. Direct 8042 PS/2 Hardware Port Polling (Ports 0x64 Status, 0x60 Data)
            // Toshiba NB100 laptop built-in keyboard sends scancodes directly to port 0x60
            try
            {
                var pStatus = new Cosmos.Core.IOPort(0x64);
                byte status = pStatus.Byte;
                // Bit 0: Output buffer full (data ready)
                if ((status & 0x01) != 0)
                {
                    var pData = new Cosmos.Core.IOPort(0x60);
                    byte scancode = pData.Byte;
                    // Bit 5: If set, data came from PS/2 mouse/touchpad - ignore to prevent jitter
                    if ((status & 0x20) == 0)
                    {
                        if (scancode == 0xE0)
                        {
                            _ps2Extended = true;
                            return false;
                        }

                        bool isExt = _ps2Extended;
                        _ps2Extended = false;

                        // Check for key-down (bit 7 clear)
                        if ((scancode & 0x80) == 0)
                        {
                            ConsoleKeyEx key = ConsoleKeyEx.NoName;
                            char keyChar = '\0';

                            if (isExt)
                            {
                                switch (scancode)
                                {
                                    case 0x48: key = ConsoleKeyEx.UpArrow; break;
                                    case 0x50: key = ConsoleKeyEx.DownArrow; break;
                                    case 0x4B: key = ConsoleKeyEx.LeftArrow; break;
                                    case 0x4D: key = ConsoleKeyEx.RightArrow; break;
                                    case 0x47: key = ConsoleKeyEx.Home; break;
                                    case 0x4F: key = ConsoleKeyEx.End; break;
                                    case 0x49: key = ConsoleKeyEx.PageUp; break;
                                    case 0x51: key = ConsoleKeyEx.PageDown; break;
                                    case 0x53: key = ConsoleKeyEx.Delete; break;
                                    case 0x1C: key = ConsoleKeyEx.Enter; keyChar = '\r'; break;
                                    case 0x5B: key = ConsoleKeyEx.LWin; break;
                                    case 0x5C: key = ConsoleKeyEx.RWin; break;
                                }
                            }
                            else
                            {
                                switch (scancode)
                                {
                                    case 0x01: key = ConsoleKeyEx.Escape; break;
                                    case 0x02: key = ConsoleKeyEx.D1; keyChar = '1'; break;
                                    case 0x03: key = ConsoleKeyEx.D2; keyChar = '2'; break;
                                    case 0x04: key = ConsoleKeyEx.D3; keyChar = '3'; break;
                                    case 0x05: key = ConsoleKeyEx.D4; keyChar = '4'; break;
                                    case 0x06: key = ConsoleKeyEx.D5; keyChar = '5'; break;
                                    case 0x07: key = ConsoleKeyEx.D6; keyChar = '6'; break;
                                    case 0x08: key = ConsoleKeyEx.D7; keyChar = '7'; break;
                                    case 0x09: key = ConsoleKeyEx.D8; keyChar = '8'; break;
                                    case 0x0A: key = ConsoleKeyEx.D9; keyChar = '9'; break;
                                    case 0x0B: key = ConsoleKeyEx.D0; keyChar = '0'; break;
                                    case 0x0C: key = ConsoleKeyEx.Minus; keyChar = '-'; break;
                                    case 0x0D: key = ConsoleKeyEx.Equal; keyChar = '+'; break;
                                    case 0x0E: key = ConsoleKeyEx.Backspace; keyChar = '\b'; break;
                                    case 0x0F: key = ConsoleKeyEx.Tab; keyChar = '\t'; break;
                                    case 0x10: key = ConsoleKeyEx.Q; keyChar = 'q'; break;
                                    case 0x11: key = ConsoleKeyEx.W; keyChar = 'w'; break;
                                    case 0x12: key = ConsoleKeyEx.E; keyChar = 'e'; break;
                                    case 0x13: key = ConsoleKeyEx.R; keyChar = 'r'; break;
                                    case 0x14: key = ConsoleKeyEx.T; keyChar = 't'; break;
                                    case 0x15: key = ConsoleKeyEx.Y; keyChar = 'y'; break;
                                    case 0x16: key = ConsoleKeyEx.U; keyChar = 'u'; break;
                                    case 0x17: key = ConsoleKeyEx.I; keyChar = 'i'; break;
                                    case 0x18: key = ConsoleKeyEx.O; keyChar = 'o'; break;
                                    case 0x19: key = ConsoleKeyEx.P; keyChar = 'p'; break;
                                    case 0x1C: key = ConsoleKeyEx.Enter; keyChar = '\r'; break;
                                    case 0x1E: key = ConsoleKeyEx.A; keyChar = 'a'; break;
                                    case 0x1F: key = ConsoleKeyEx.S; keyChar = 's'; break;
                                    case 0x20: key = ConsoleKeyEx.D; keyChar = 'd'; break;
                                    case 0x21: key = ConsoleKeyEx.F; keyChar = 'f'; break;
                                    case 0x22: key = ConsoleKeyEx.G; keyChar = 'g'; break;
                                    case 0x23: key = ConsoleKeyEx.H; keyChar = 'h'; break;
                                    case 0x24: key = ConsoleKeyEx.J; keyChar = 'j'; break;
                                    case 0x25: key = ConsoleKeyEx.K; keyChar = 'k'; break;
                                    case 0x26: key = ConsoleKeyEx.L; keyChar = 'l'; break;
                                    case 0x2C: key = ConsoleKeyEx.Z; keyChar = 'z'; break;
                                    case 0x2D: key = ConsoleKeyEx.X; keyChar = 'x'; break;
                                    case 0x2E: key = ConsoleKeyEx.C; keyChar = 'c'; break;
                                    case 0x2F: key = ConsoleKeyEx.V; keyChar = 'v'; break;
                                    case 0x30: key = ConsoleKeyEx.B; keyChar = 'b'; break;
                                    case 0x31: key = ConsoleKeyEx.N; keyChar = 'n'; break;
                                    case 0x32: key = ConsoleKeyEx.M; keyChar = 'm'; break;
                                    case 0x33: key = ConsoleKeyEx.Comma; keyChar = ','; break;
                                    case 0x34: key = ConsoleKeyEx.Period; keyChar = '.'; break;
                                    case 0x35: key = ConsoleKeyEx.Slash; keyChar = '/'; break;
                                    case 0x37: key = ConsoleKeyEx.D8; keyChar = '*'; break;
                                    case 0x39: key = ConsoleKeyEx.Spacebar; keyChar = ' '; break;
                                    case 0x3B: key = ConsoleKeyEx.F1; break;
                                    case 0x3C: key = ConsoleKeyEx.F2; break;
                                    case 0x3D: key = ConsoleKeyEx.F3; break;
                                    case 0x3E: key = ConsoleKeyEx.F4; break;
                                    case 0x3F: key = ConsoleKeyEx.F5; break;
                                    case 0x40: key = ConsoleKeyEx.F6; break;
                                    case 0x41: key = ConsoleKeyEx.F7; break;
                                    case 0x42: key = ConsoleKeyEx.F8; break;
                                    case 0x43: key = ConsoleKeyEx.F9; break;
                                    case 0x44: key = ConsoleKeyEx.F10; break;
                                    case 0x48: key = ConsoleKeyEx.UpArrow; break;
                                    case 0x4B: key = ConsoleKeyEx.LeftArrow; break;
                                    case 0x4D: key = ConsoleKeyEx.RightArrow; break;
                                    case 0x50: key = ConsoleKeyEx.DownArrow; break;
                                    case 0x5B: key = ConsoleKeyEx.LWin; break;
                                    case 0x5C: key = ConsoleKeyEx.RWin; break;
                                }
                            }

                            if (key != ConsoleKeyEx.NoName)
                            {
                                keyEvent = new KeyEvent { Key = key, KeyChar = keyChar };
                                return true;
                            }
                        }
                    }
                }
            }
            catch { }

            return false;
        }
    }
}
