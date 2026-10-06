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
            DrawDataBox("TEMPERATURE", ((int)data.TemperatureCelsius).ToString() + " C", ConsoleColor.Red, 2, 4);
            DrawDataBox("HUMIDITY", ((int)data.HumidityPercent).ToString() + " %", ConsoleColor.Cyan, 28, 4);
            DrawDataBox("PRESSURE", ((int)data.PressureHpa).ToString() + " hPa", ConsoleColor.Green, 54, 4);
            
            DrawDataBox("WIND (SPEED)", ((int)data.WindSpeedKmh).ToString() + " km/h", ConsoleColor.Yellow, 2, 10);
            DrawDataBox("WIND (DIR)", data.WindDirection, ConsoleColor.Yellow, 28, 10);
            DrawDataBox("CONDITION", data.Condition, ConsoleColor.Magenta, 54, 10);

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
    }
}
