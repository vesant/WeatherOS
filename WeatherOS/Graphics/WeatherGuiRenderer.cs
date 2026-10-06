using System;
using WeatherOS.Services;

namespace WeatherOS.Graphics
{
    public class WeatherGuiRenderer
    {
        private bool _isActive;
        public bool IsActive => _isActive;

        public WeatherGuiRenderer()
        {
        }

        public bool Start()
        {
            _isActive = true;
            System.Console.Clear();
            System.Console.CursorVisible = false;
            return true;
        }

        public void Stop()
        {
            _isActive = false;
            System.Console.Clear();
            System.Console.CursorVisible = true;
        }

        public void Render(ref WeatherData data)
        {
            if (!_isActive) return;

            // Lock cursor position to avoid flickering
            System.Console.SetCursorPosition(0, 0);

            DrawHeader();
            DrawDataBox("TEMPERATURE", $"{data.TemperatureCelsius:F1} C", ConsoleColor.Red, 2, 4);
            DrawDataBox("HUMIDITY", $"{data.HumidityPercent:F1} %", ConsoleColor.Cyan, 28, 4);
            DrawDataBox("PRESSURE", $"{data.PressureHpa:F1} hPa", ConsoleColor.Green, 54, 4);
            
            DrawDataBox("WIND (SPEED)", $"{data.WindSpeedKmh:F1} km/h", ConsoleColor.Yellow, 2, 10);
            DrawDataBox("WIND (DIR)", $"{data.WindDirection}", ConsoleColor.Yellow, 28, 10);
            DrawDataBox("CONDITION", $"{data.Condition}", ConsoleColor.Magenta, 54, 10);

            DrawFooter();
        }

        private void DrawHeader()
        {
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.BackgroundColor = ConsoleColor.Blue;
            string title = "   WEATHER OS - TERMINAL DASHBOARD (UDP TELEMETRY)   ";
            int padding = (80 - title.Length) / 2;
            System.Console.WriteLine(new string(' ', padding) + title + new string(' ', 80 - padding - title.Length));
            System.Console.ResetColor();
            System.Console.WriteLine();
            System.Console.WriteLine();
        }

        private void DrawDataBox(string title, string value, ConsoleColor valueColor, int left, int top)
        {
            System.Console.SetCursorPosition(left, top);
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.Write("+------------------------+");
            
            System.Console.SetCursorPosition(left, top + 1);
            System.Console.Write("| ");
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.Write(title.PadRight(22));
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.Write(" |");

            System.Console.SetCursorPosition(left, top + 2);
            System.Console.Write("| ");
            System.Console.ForegroundColor = valueColor;
            System.Console.Write(value.PadRight(22));
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.Write(" |");

            System.Console.SetCursorPosition(left, top + 3);
            System.Console.Write("+------------------------+");
            System.Console.ResetColor();
        }

        private void DrawFooter()
        {
            System.Console.SetCursorPosition(0, 23);
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.WriteLine(new string('-', 80));
            System.Console.ForegroundColor = ConsoleColor.Gray;
            System.Console.Write(" >> STATUS: ");
            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.Write("ONLINE (UDP PORT 6000)");
            System.Console.ForegroundColor = ConsoleColor.Gray;
            System.Console.Write("   |   PRESS ");
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.Write("[ESC]");
            System.Console.ForegroundColor = ConsoleColor.Gray;
            System.Console.Write(" TO EXIT");
        }
    }
}
