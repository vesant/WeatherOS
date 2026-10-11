using System;
using Cosmos.System;
using WeatherOS.Graphics;
using WeatherOS.Services;

namespace WeatherOS.Apps
{
    public class DebugMenuApp : IApp
    {
        public string Id => "debug_menu";
        public string Name => "Debug Menu";
        public bool IsRealTime => false;

        private int _selectedIndex = 0;
        private string[] _options = {
            "1. Disk Diagnostics & Hardware Analyzer",
            "2. Live System Resources & Power Monitor",
            "3. Network Card Hardware & Direct Sockets",
            "4. ACPI Power & Battery Live Telemetry",
            "5. Return to Main Menu"
        };

        public void Draw(TuiDesktop desktop)
        {
            int winWidth = 56;
            int winHeight = 13;
            int left = (80 - winWidth) / 2;
            int top = (24 - winHeight) / 2;

            desktop.DrawWindow(left, top, winWidth, winHeight, "DEBUG TOOLS & HARDWARE DIAGNOSTICS");
            ConsoleColor winBg = desktop.IsDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;

            System.Console.SetCursorPosition(left + 3, top + 2);
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.Write("Advanced System Telemetry & Hardware Inspector:");

            for (int i = 0; i < _options.Length; i++)
            {
                System.Console.SetCursorPosition(left + 3, top + 4 + i);
                if (i == _selectedIndex)
                {
                    System.Console.ForegroundColor = ConsoleColor.Black;
                    System.Console.BackgroundColor = ConsoleColor.White;
                }
                else
                {
                    System.Console.ForegroundColor = (i == 0) ? ConsoleColor.Yellow : ConsoleColor.White;
                    System.Console.BackgroundColor = winBg;
                }

                string opt = $" > {_options[i]} ";
                if (opt.Length > winWidth - 6) opt = opt.Substring(0, winWidth - 9) + "... ";
                System.Console.Write(opt.PadRight(winWidth - 6));
            }
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Tab || keyEvent.Key == ConsoleKeyEx.DownArrow)
            {
                _selectedIndex++;
                if (_selectedIndex >= _options.Length) _selectedIndex = 0;
            }
            else if (keyEvent.Key == ConsoleKeyEx.UpArrow)
            {
                _selectedIndex--;
                if (_selectedIndex < 0) _selectedIndex = _options.Length - 1;
            }
            else if (keyEvent.Key == ConsoleKeyEx.Enter)
            {
                if (_selectedIndex == 0)
                {
                    // The first program: Disk Diagnostics & Hardware Analyzer
                    var app = desktop.GetApp("diskdiag") as DiskDiagnosticsApp;
                    app?.Refresh();
                    desktop.SetCurrentApp("diskdiag");
                }
                else if (_selectedIndex == 1)
                {
                    desktop.SetCurrentApp("resources");
                }
                else if (_selectedIndex == 2)
                {
                    desktop.SetCurrentApp("ipconfig");
                }
                else if (_selectedIndex == 3)
                {
                    var b = BatteryService.GetBatteryInfo();
                    string ac = b.IsAcConnected ? "AC Plugged (Charging)" : "On Battery (Discharging)";
                    desktop.DrawStatusMessage($"Power: {ac} | Level: {b.Percentage}% [{b.ChassisType}]");
                }
                else if (_selectedIndex == 4)
                {
                    desktop.SetCurrentApp("launcher");
                }
            }
            else if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                desktop.SetCurrentApp("launcher");
            }
        }
    }
}
