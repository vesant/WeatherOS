using System;
using Cosmos.System;
using WeatherOS.Services;

namespace WeatherOS.Graphics
{
    public class TuiDesktop
    {
        private bool _isActive;
        private string _currentApp = "launcher";
        private WeatherService _weatherService;
        private string _apiKey = "4496d004663e00fc92591605e55cd7dc";

        // Launcher State
        private int _selectedAppIndex = 0;
        private string[] _apps = { "Weather Dashboard", "Change City", "Digital Clock", "Calculator", "Notepad" };
        private string[] _appIds = { "weather", "change_city", "clock", "calculator", "notepad" };

        // Input Buffers
        private string _cityInputBuffer = "";
        private string _calcInputBuffer = "";
        private string _calcResult = "";
        private string _notepadBuffer = "";

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
            else
            {
                _currentApp = "launcher";
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
                    if (_currentApp == "launcher")
                    {
                        _isActive = false; // Exit GUI
                    }
                    else
                    {
                        _currentApp = "launcher"; // Back to Menu
                    }
                }
                else
                {
                    // Route input to the active app
                    if (_currentApp == "launcher") HandleLauncherInput(keyEvent);
                    else if (_currentApp == "change_city") HandleChangeCityInput(keyEvent);
                    else if (_currentApp == "calculator") HandleCalculatorInput(keyEvent);
                    else if (_currentApp == "notepad") HandleNotepadInput(keyEvent);
                }
            }

            System.Console.Clear();
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine("[sys] tty1: Terminated graphical session.\n");
            System.Console.ResetColor();
        }

        private void HandleLauncherInput(KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Tab || keyEvent.Key == ConsoleKeyEx.DownArrow)
            {
                _selectedAppIndex++;
                if (_selectedAppIndex >= _apps.Length) _selectedAppIndex = 0;
            }
            else if (keyEvent.Key == ConsoleKeyEx.UpArrow)
            {
                _selectedAppIndex--;
                if (_selectedAppIndex < 0) _selectedAppIndex = _apps.Length - 1;
            }
            else if (keyEvent.Key == ConsoleKeyEx.Enter)
            {
                _currentApp = _appIds[_selectedAppIndex];
            }
        }

        private void HandleChangeCityInput(KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Enter)
            {
                if (!string.IsNullOrWhiteSpace(_cityInputBuffer))
                {
                    DrawStatusMessage("Fetching new data via TCP...");
                    _weatherService.FetchOpenWeather(_cityInputBuffer.Trim(), _apiKey);
                    _cityInputBuffer = "";
                    _currentApp = "weather";
                }
            }
            else if (keyEvent.Key == ConsoleKeyEx.Backspace)
            {
                if (_cityInputBuffer.Length > 0) _cityInputBuffer = _cityInputBuffer.Substring(0, _cityInputBuffer.Length - 1);
            }
            else if (keyEvent.KeyChar >= 32 && keyEvent.KeyChar <= 126 && _cityInputBuffer.Length < 20)
            {
                _cityInputBuffer += keyEvent.KeyChar;
            }
        }

        private void HandleCalculatorInput(KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Enter)
            {
                try
                {
                    // Very simple parsing for A + B
                    string[] parts = _calcInputBuffer.Split(new char[] { '+', '-', '*', '/' });
                    if (parts.Length == 2)
                    {
                        int a = int.Parse(parts[0].Trim());
                        int b = int.Parse(parts[1].Trim());
                        if (_calcInputBuffer.Contains("+")) _calcResult = (a + b).ToString();
                        else if (_calcInputBuffer.Contains("-")) _calcResult = (a - b).ToString();
                        else if (_calcInputBuffer.Contains("*")) _calcResult = (a * b).ToString();
                        else if (_calcInputBuffer.Contains("/")) _calcResult = (b != 0 ? (a / b).ToString() : "Div/0");
                    }
                    else
                    {
                        _calcResult = "Err: Use A+B";
                    }
                }
                catch { _calcResult = "Error"; }
                _calcInputBuffer = "";
            }
            else if (keyEvent.Key == ConsoleKeyEx.Backspace)
            {
                if (_calcInputBuffer.Length > 0) _calcInputBuffer = _calcInputBuffer.Substring(0, _calcInputBuffer.Length - 1);
            }
            else if (keyEvent.KeyChar >= 32 && keyEvent.KeyChar <= 126 && _calcInputBuffer.Length < 20)
            {
                _calcInputBuffer += keyEvent.KeyChar;
            }
        }

        private void HandleNotepadInput(KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Backspace)
            {
                if (_notepadBuffer.Length > 0) _notepadBuffer = _notepadBuffer.Substring(0, _notepadBuffer.Length - 1);
            }
            else if (keyEvent.Key == ConsoleKeyEx.Enter)
            {
                _notepadBuffer += "\n";
            }
            else if (keyEvent.KeyChar >= 32 && keyEvent.KeyChar <= 126 && _notepadBuffer.Length < 500)
            {
                _notepadBuffer += keyEvent.KeyChar;
            }
        }

        private void DrawDesktop()
        {
            System.Console.CursorVisible = false;
            System.Console.SetCursorPosition(0, 0);

            // Menu Bar
            System.Console.BackgroundColor = ConsoleColor.Gray;
            System.Console.ForegroundColor = ConsoleColor.Black;
            if (_currentApp == "launcher")
            {
                System.Console.Write("  [TAB] Select App      [ENTER] Open App      [ESC] Exit GUI                    ");
            }
            else
            {
                System.Console.Write("  [ESC] Return to Launcher Menu                                                 ");
            }
            System.Console.ResetColor();

            // Clear Background
            for (int y = 1; y < 24; y++)
            {
                System.Console.SetCursorPosition(0, y);
                System.Console.BackgroundColor = ConsoleColor.DarkBlue;
                for (int x = 0; x < 80; x++) System.Console.Write(" ");
            }
            System.Console.ResetColor();

            // Draw current view
            if (_currentApp == "launcher") DrawLauncher();
            else if (_currentApp == "weather") DrawWeatherApp();
            else if (_currentApp == "change_city") DrawChangeCityApp();
            else if (_currentApp == "clock") DrawClockApp();
            else if (_currentApp == "calculator") DrawCalculatorApp();
            else if (_currentApp == "notepad") DrawNotepadApp();

            // Bottom Bar
            System.Console.SetCursorPosition(0, 24);
            System.Console.BackgroundColor = ConsoleColor.Gray;
            System.Console.ForegroundColor = ConsoleColor.Black;
            System.Console.Write("  WeatherOS TUI v2.0 (Multitasking Archaic Mode)                                ");
            System.Console.ResetColor();
        }

        private void DrawLauncher()
        {
            DrawWindow(20, 5, 40, 14, "MAIN MENU");

            System.Console.BackgroundColor = ConsoleColor.Blue;

            for (int i = 0; i < _apps.Length; i++)
            {
                System.Console.SetCursorPosition(25, 8 + i);
                if (i == _selectedAppIndex)
                {
                    System.Console.ForegroundColor = ConsoleColor.Black;
                    System.Console.BackgroundColor = ConsoleColor.White;
                    System.Console.Write($" > {_apps[i]} ");
                    for(int s = _apps[i].Length + 3; s < 30; s++) System.Console.Write(" ");
                }
                else
                {
                    System.Console.ForegroundColor = ConsoleColor.White;
                    System.Console.BackgroundColor = ConsoleColor.Blue;
                    System.Console.Write($"   {_apps[i]} ");
                    for(int s = _apps[i].Length + 3; s < 30; s++) System.Console.Write(" ");
                }
            }
            System.Console.ResetColor();
        }

        private void DrawWindow(int left, int top, int width, int height, string title)
        {
            System.Console.BackgroundColor = ConsoleColor.Blue;
            System.Console.ForegroundColor = ConsoleColor.White;

            System.Console.SetCursorPosition(left, top);
            System.Console.Write("+");
            for (int i = 0; i < width - 2; i++) System.Console.Write("-");
            System.Console.Write("+");

            System.Console.SetCursorPosition(left + 2, top);
            System.Console.Write($" {title} ");

            for (int y = 1; y < height - 1; y++)
            {
                System.Console.SetCursorPosition(left, top + y);
                System.Console.Write("|");
                for (int x = 0; x < width - 2; x++) System.Console.Write(" ");
                System.Console.Write("|");
            }

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
            System.Console.SetCursorPosition(30, 5);
            System.Console.Write("LOCAL: " + data.Location);

            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.SetCursorPosition(30, 7);
            System.Console.Write("Condition : " + data.Condition);

            System.Console.SetCursorPosition(30, 9);
            System.Console.Write("Temp      : " + ((int)data.TemperatureCelsius).ToString() + " C");

            System.Console.SetCursorPosition(30, 11);
            System.Console.Write("Humidity  : " + ((int)data.HumidityPercent).ToString() + " %");

            System.Console.SetCursorPosition(50, 9);
            System.Console.Write("Pressure : " + ((int)data.PressureHpa).ToString() + " hPa");

            System.Console.SetCursorPosition(50, 11);
            System.Console.Write("Wind Spd : " + ((int)data.WindSpeedKmh).ToString() + " km/h");

            System.Console.SetCursorPosition(50, 13);
            System.Console.Write("Wind Dir : " + data.WindDirection);

            // ASCII Weather Icon
            DrawWeatherIcon(data.Condition, 8, 7);

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
                System.Console.ForegroundColor = ConsoleColor.Cyan;
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
        }

        private void DrawChangeCityApp()
        {
            DrawWindow(20, 8, 40, 10, "SYSTEM DIALOG: CHANGE CITY");

            System.Console.BackgroundColor = ConsoleColor.Blue;
            System.Console.ForegroundColor = ConsoleColor.White;

            System.Console.SetCursorPosition(23, 11);
            System.Console.Write("Enter new city name:");

            System.Console.SetCursorPosition(23, 13);
            System.Console.Write("> " + _cityInputBuffer + "_");
            for (int i = _cityInputBuffer.Length; i < 20; i++) System.Console.Write(" ");

            System.Console.SetCursorPosition(23, 15);
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.Write("[Press ENTER to search]");
            
            System.Console.ResetColor();
        }

        private void DrawClockApp()
        {
            DrawWindow(25, 8, 30, 8, "DIGITAL CLOCK");
            System.Console.BackgroundColor = ConsoleColor.Blue;
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            
            string timeStr = DateTime.UtcNow.ToString("HH:mm:ss");
            string dateStr = DateTime.UtcNow.ToString("yyyy-MM-dd");

            System.Console.SetCursorPosition(32, 11);
            System.Console.Write(timeStr + " UTC");
            
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.SetCursorPosition(33, 13);
            System.Console.Write(dateStr);
            
            System.Console.ResetColor();
        }

        private void DrawCalculatorApp()
        {
            DrawWindow(20, 6, 40, 12, "CALCULATOR");
            System.Console.BackgroundColor = ConsoleColor.Blue;
            System.Console.ForegroundColor = ConsoleColor.White;

            System.Console.SetCursorPosition(23, 9);
            System.Console.Write("Equation (e.g. 5 + 10):");

            System.Console.SetCursorPosition(23, 11);
            System.Console.Write("> " + _calcInputBuffer + "_");
            for (int i = _calcInputBuffer.Length; i < 20; i++) System.Console.Write(" ");

            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.SetCursorPosition(23, 14);
            System.Console.Write("RESULT: " + _calcResult);
            for (int i = _calcResult.Length; i < 20; i++) System.Console.Write(" ");

            System.Console.ResetColor();
        }

        private void DrawNotepadApp()
        {
            DrawWindow(5, 3, 70, 18, "NOTEPAD (Volatile)");
            System.Console.BackgroundColor = ConsoleColor.Blue;
            System.Console.ForegroundColor = ConsoleColor.White;

            string[] lines = _notepadBuffer.Split('\n');
            for (int i = 0; i < lines.Length && i < 15; i++)
            {
                System.Console.SetCursorPosition(7, 5 + i);
                string line = lines[i];
                if (line.Length > 65) line = line.Substring(0, 65);
                System.Console.Write(line);
                if (i == lines.Length - 1) System.Console.Write("_"); // Cursor
            }
            if (lines.Length == 0)
            {
                System.Console.SetCursorPosition(7, 5);
                System.Console.Write("_");
            }

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
