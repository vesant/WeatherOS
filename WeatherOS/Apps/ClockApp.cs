using System;
using Cosmos.System;
using WeatherOS.Graphics;

namespace WeatherOS.Apps
{
    public class ClockApp : IApp
    {
        public string Id => "clock";
        public string Name => "Digital Clock";
        public bool IsRealTime => true;

        public void Draw(TuiDesktop desktop)
        {
            var win = desktop.CreateWindow("DIGITAL CLOCK", WindowSize.Small);
            
            string timeStr = DateTime.UtcNow.ToString("HH:mm:ss");
            string dateStr = DateTime.UtcNow.ToString("yyyy-MM-dd");

            win.WriteRow(1, "Time: " + timeStr + " UTC", ConsoleColor.Cyan, 2);
            win.WriteRow(3, "Date: " + dateStr, ConsoleColor.White, 2);
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
