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
            return true;
        }

        public void Stop()
        {
            _isActive = false;
            System.Console.Clear();
        }

        public void Render(ref WeatherData data)
        {
            if (!_isActive) return;

            // Lock cursor position to avoid flickering
            System.Console.SetCursorPosition(0, 0);

            DrawHeader();
            
            // Draw Weather Icon on the left
            DrawWeatherIcon(data.Condition, 4, 3);
            
            // Draw Main Info on the right of the icon
            System.Console.SetCursorPosition(30, 4);
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.Write("STATION : ");
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.Write(data.Location);
            for(int i = data.Location.Length; i < 20; i++) System.Console.Write(" ");
            
            System.Console.SetCursorPosition(30, 5);
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.Write("WEATHER : ");
            System.Console.ForegroundColor = ConsoleColor.Magenta;
            System.Console.Write(data.Condition);
            for(int i = data.Condition.Length; i < 20; i++) System.Console.Write(" ");
            
            // Grid of data
            DrawDataBox("TEMPERATURE", ((int)data.TemperatureCelsius).ToString() + " C", ConsoleColor.Red, 1, 10);
            DrawDataBox("HUMIDITY", ((int)data.HumidityPercent).ToString() + " %", ConsoleColor.Cyan, 27, 10);
            DrawDataBox("PRESSURE", ((int)data.PressureHpa).ToString() + " hPa", ConsoleColor.Green, 53, 10);
            
            DrawDataBox("WIND SPEED", ((int)data.WindSpeedKmh).ToString() + " km/h", ConsoleColor.Yellow, 1, 15);
            DrawDataBox("WIND DIR", data.WindDirection, ConsoleColor.Yellow, 27, 15);
            DrawDataBox("STATUS", "LIVE (TCP)", ConsoleColor.Green, 53, 15);

            DrawFooter();
        }

        private void DrawHeader()
        {
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.BackgroundColor = ConsoleColor.Blue;
            System.Console.Write("              WEATHER OS - TERMINAL DASHBOARD (OPENWEATHER API)                ");
            System.Console.ResetColor();
        }

        private void DrawDataBox(string title, string value, ConsoleColor valueColor, int left, int top)
        {
            System.Console.SetCursorPosition(left, top);
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.Write("+------------------------+");
            
            System.Console.SetCursorPosition(left, top + 1);
            System.Console.Write("| ");
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.Write(title);
            for(int i = title.Length; i < 22; i++) System.Console.Write(" ");
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.Write(" |");

            System.Console.SetCursorPosition(left, top + 2);
            System.Console.Write("| ");
            System.Console.ForegroundColor = valueColor;
            System.Console.Write(value);
            for(int i = value.Length; i < 22; i++) System.Console.Write(" ");
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
            for(int i = 0; i < 79; i++) System.Console.Write("-");
            
            System.Console.SetCursorPosition(0, 24);
            System.Console.ForegroundColor = ConsoleColor.Gray;
            System.Console.Write(" >> STATUS: ");
            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.Write("ONLINE (OPENWEATHER API)");
            System.Console.ForegroundColor = ConsoleColor.Gray;
            System.Console.Write("   |   PRESS ");
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.Write("[ESC]");
            System.Console.ResetColor();
        }

        private void DrawWeatherIcon(string condition, int left, int top)
        {
            string cond = condition.ToLower();
            if (cond.Contains("cloud"))
            {
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("      .--.      ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("   .-(    ).    ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("  (___.__)__)   ");
            }
            else if (cond.Contains("rain") || cond.Contains("drizzle") || cond.Contains("thunder"))
            {
                System.Console.ForegroundColor = ConsoleColor.Blue;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("   .-^^---.     ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("  (________)    ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("   /  /  /      ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("  /  /  /       ");
            }
            else if (cond.Contains("snow"))
            {
                System.Console.ForegroundColor = ConsoleColor.White;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("   .-^^---.     ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("  (________)    ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("   *  *  *      ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("  *  *  *       ");
            }
            else // Clear or unknown
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("    \\   /      ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("     .-.        ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("  --(   )--     ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("     `-'        ");
                System.Console.SetCursorPosition(left, top + 4); System.Console.Write("    /   \\      ");
            }
            System.Console.ResetColor();
        }
    }
}
