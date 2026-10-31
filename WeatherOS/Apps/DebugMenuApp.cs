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
            "1. Live System Resources & Power Monitor",
            "2. Network Card Hardware & Direct Sockets",
            "3. ACPI Power & Battery Live Telemetry",
            "4. Return to Main Menu"
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
                    desktop.SetCurrentApp("resources");
                }
                else if (_selectedIndex == 1)
                {
                    desktop.SetCurrentApp("ipconfig");
                }
                else if (_selectedIndex == 2)
                {
                    desktop.SetCurrentApp("battery");
                }
                else if (_selectedIndex == 3)
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
