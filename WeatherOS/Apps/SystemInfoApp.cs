using System;
using Cosmos.System;
using WeatherOS.Graphics;

namespace WeatherOS.Apps
{
    public class SystemInfoApp : IApp
    {
        public string Id => "about";
        public string Name => "System Information";
        public bool IsRealTime => false;

        public void Draw(TuiDesktop desktop)
        {
            desktop.DrawWindow(20, 6, 40, 10, "SYSTEM INFORMATION");
            ConsoleColor winBg = desktop.IsDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;
            System.Console.ForegroundColor = ConsoleColor.White;

            System.Console.SetCursorPosition(25, 9);
            System.Console.Write("WeatherOS v3.0 (Cosmos Kernel)");
            System.Console.SetCursorPosition(25, 11);
            System.Console.Write("Developed by: Ghost");
            System.Console.SetCursorPosition(25, 13);
            System.Console.Write("Architecture: x86 / 32-bit (IL2CPU)");

            System.Console.SetCursorPosition(25, 14);
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.Write("[ESC] Return to Desktop");
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                desktop.SetCurrentApp("launcher");
            }
        }
    }
}
