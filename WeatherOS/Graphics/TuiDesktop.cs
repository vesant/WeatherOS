using System;
using Cosmos.System;
using WeatherOS.Services;

namespace WeatherOS.Graphics
{
    public class TuiDesktop
    {
        private bool _isActive;
        private string _currentApp = "weather";
        private WeatherService _weatherService;
        private string _apiKey = "4496d004663e00fc92591605e55cd7dc"; // Same key used in Kernel

        public TuiDesktop(WeatherService weatherService)
        {
            _weatherService = weatherService;
        }

        public void Start()
        {
            if (_weatherService.GetLatestReading().Location == "UNKNOWN")
            {
                _currentApp = "change_city";
            }
            
            _isActive = true;
            RunLoop();
        }

        private void RunLoop()
        {
            while (_isActive)
            {
                DrawDesktop();

                // Block and wait for key
                var keyEvent = KeyboardManager.ReadKey();

                if (keyEvent.Key == ConsoleKeyEx.Escape)
                {
                    _isActive = false;
                }
                else if (keyEvent.Key == ConsoleKeyEx.F1)
                {
                    _currentApp = "weather";
                }
                else if (keyEvent.Key == ConsoleKeyEx.F2)
                {
                    _currentApp = "change_city";
                }
                else if (_currentApp == "change_city")
                {
                    HandleChangeCityInput(keyEvent);
                }
            }

            System.Console.Clear();
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine("[sys] tty1: Terminated graphical session.\n");
            System.Console.ResetColor();
        }

        private string _cityInputBuffer = "";

        private void HandleChangeCityInput(KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Enter)
            {
                if (!string.IsNullOrWhiteSpace(_cityInputBuffer))
                {
                    DrawStatusMessage("Fetching new data via TCP...");
                    _weatherService.FetchOpenWeather(_cityInputBuffer.Trim(), _apiKey);
                    _cityInputBuffer = "";
                    _currentApp = "weather"; // Go back to weather app
                }
            }
            else if (keyEvent.Key == ConsoleKeyEx.Backspace)
            {
                if (_cityInputBuffer.Length > 0)
                {
                    _cityInputBuffer = _cityInputBuffer.Substring(0, _cityInputBuffer.Length - 1);
                }
            }
            else if (keyEvent.KeyChar >= 32 && keyEvent.KeyChar <= 126)
            {
                if (_cityInputBuffer.Length < 20)
                {
                    _cityInputBuffer += keyEvent.KeyChar;
                }
            }
        }

        private void DrawDesktop()
        {
            System.Console.CursorVisible = false;
            System.Console.SetCursorPosition(0, 0);

            // Menu Bar
            System.Console.BackgroundColor = ConsoleColor.Gray;
            System.Console.ForegroundColor = ConsoleColor.Black;
            System.Console.Write("  [F1] Weather App      [F2] Change City      [ESC] Exit GUI                    ");
            System.Console.ResetColor();

            // Clear Background
            for (int y = 1; y < 24; y++)
            {
                System.Console.SetCursorPosition(0, y);
                System.Console.BackgroundColor = ConsoleColor.DarkBlue;
                for (int x = 0; x < 80; x++) System.Console.Write(" ");
            }
            System.Console.ResetColor();

            if (_currentApp == "weather")
            {
                DrawWeatherApp();
            }
            else if (_currentApp == "change_city")
            {
                DrawChangeCityApp();
            }

            // Bottom Bar
            System.Console.SetCursorPosition(0, 24);
            System.Console.BackgroundColor = ConsoleColor.Gray;
            System.Console.ForegroundColor = ConsoleColor.Black;
            System.Console.Write("  WeatherOS TUI v1.0 (Archaic Mode)                                             ");
            System.Console.ResetColor();
        }

        private void DrawWindow(int left, int top, int width, int height, string title)
        {
            System.Console.BackgroundColor = ConsoleColor.Blue;
            System.Console.ForegroundColor = ConsoleColor.White;

            // Top border
            System.Console.SetCursorPosition(left, top);
            System.Console.Write("+");
            for (int i = 0; i < width - 2; i++) System.Console.Write("-");
            System.Console.Write("+");

            // Title
            System.Console.SetCursorPosition(left + 2, top);
            System.Console.Write($" {title} ");

            // Body
            for (int y = 1; y < height - 1; y++)
            {
                System.Console.SetCursorPosition(left, top + y);
                System.Console.Write("|");
                for (int x = 0; x < width - 2; x++) System.Console.Write(" ");
                System.Console.Write("|");
            }

            // Bottom border
            System.Console.SetCursorPosition(left, top + height - 1);
            System.Console.Write("+");
            for (int i = 0; i < width - 2; i++) System.Console.Write("-");
            System.Console.Write("+");
            
            System.Console.ResetColor();
        }

        private void DrawWeatherApp()
        {
            DrawWindow(5, 3, 70, 18, "WEATHER APPLICATION");

            var data = _weatherService.GetLatestReading();

            System.Console.BackgroundColor = ConsoleColor.Blue;
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            
            System.Console.SetCursorPosition(10, 5);
            System.Console.Write("LOCAL: " + data.Location);

            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.SetCursorPosition(10, 7);
            System.Console.Write("Condition : " + data.Condition);

            System.Console.SetCursorPosition(10, 9);
            System.Console.Write("Temp      : " + ((int)data.TemperatureCelsius).ToString() + " C");

            System.Console.SetCursorPosition(10, 11);
            System.Console.Write("Humidity  : " + ((int)data.HumidityPercent).ToString() + " %");

            System.Console.SetCursorPosition(40, 9);
            System.Console.Write("Pressure  : " + ((int)data.PressureHpa).ToString() + " hPa");

            System.Console.SetCursorPosition(40, 11);
            System.Console.Write("Wind Spd  : " + ((int)data.WindSpeedKmh).ToString() + " km/h");

            System.Console.SetCursorPosition(40, 13);
            System.Console.Write("Wind Dir  : " + data.WindDirection);

            System.Console.ResetColor();
        }

        private void DrawChangeCityApp()
        {
            DrawWindow(20, 8, 40, 10, "SYSTEM DIALOG");

            System.Console.BackgroundColor = ConsoleColor.Blue;
            System.Console.ForegroundColor = ConsoleColor.White;

            System.Console.SetCursorPosition(23, 11);
            System.Console.Write("Enter new city name:");

            System.Console.SetCursorPosition(23, 13);
            System.Console.Write("> " + _cityInputBuffer + "_");
            
            // Pad to clear remaining text
            for (int i = _cityInputBuffer.Length; i < 20; i++) System.Console.Write(" ");

            System.Console.SetCursorPosition(23, 15);
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.Write("[Press ENTER to search]");
            
            System.Console.ResetColor();
        }

        private void DrawStatusMessage(string msg)
        {
            System.Console.SetCursorPosition(23, 16);
            System.Console.BackgroundColor = ConsoleColor.Blue;
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.Write(msg);
            System.Console.ResetColor();
        }
    }
}
