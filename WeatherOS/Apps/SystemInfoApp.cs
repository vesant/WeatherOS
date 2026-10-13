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
            var win = desktop.CreateWindow("SYSTEM INFORMATION", WindowSize.Small);
            win.WriteRow(1, "WeatherOS v3.0 (Cosmos Kernel)", ConsoleColor.White, 2);
            win.WriteRow(3, "Developed by: Ghost", ConsoleColor.White, 2);
            win.WriteRow(5, "Architecture: x86 / 32-bit (IL2CPU)", ConsoleColor.White, 2);
            win.WriteFooter("[ESC] Return to Desktop", ConsoleColor.Yellow);
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
