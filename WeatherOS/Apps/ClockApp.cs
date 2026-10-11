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
            desktop.DrawWindow(25, 8, 30, 8, "DIGITAL CLOCK");
            ConsoleColor winBg = desktop.IsDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
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

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
        }
    }
}
