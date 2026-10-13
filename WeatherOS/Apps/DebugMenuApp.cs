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
            var win = desktop.CreateWindow("DEBUG TOOLS & HARDWARE DIAGNOSTICS", WindowSize.Medium);
            win.WriteRow(0, "Advanced System Telemetry & Hardware Inspector:", ConsoleColor.Cyan, 1);

            for (int i = 0; i < _options.Length; i++)
            {
                win.WriteSelectionRow(2 + i, _options[i], i == _selectedIndex);
            }

            win.WriteFooter("[Up/Dn] Move  |  [Enter] Open  |  [ESC] Exit", ConsoleColor.Yellow);
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
                    desktop.SetCurrentApp("battery");
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
