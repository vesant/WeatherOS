using System;
using Cosmos.System;
using WeatherOS.Graphics;
using WeatherOS.Services;

namespace WeatherOS.Apps
{
    public class ChangeCityApp : IApp
    {
        public string Id => "change_city";
        public string Name => "Change City";
        public bool IsRealTime => false;

        private TextEditorState _cityEditor = new TextEditorState { AllowMultiline = false, MaxLength = 25 };

        public void Draw(TuiDesktop desktop)
        {
            desktop.DrawWindow(20, 8, 40, 10, "SYSTEM DIALOG: CHANGE CITY");
            ConsoleColor winBg = desktop.IsDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;
            System.Console.ForegroundColor = ConsoleColor.White;

            System.Console.SetCursorPosition(23, 11);
            System.Console.Write("Enter new city name:");

            System.Console.SetCursorPosition(23, 13);
            System.Console.Write("> ");
            _cityEditor.DrawInline(25, 13, 20, winBg, ConsoleColor.White);

            System.Console.SetCursorPosition(23, 15);
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.Write("[ENTER: Search | Ctrl+A: All | Ctrl+Arrows: Select]");
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Enter)
            {
                if (!string.IsNullOrWhiteSpace(_cityEditor.Text))
                {
                    string target = _cityEditor.Text.Trim();
                    desktop.DrawStatusMessage("Fetching weather for " + target + "...");
                    desktop.WeatherService.FetchOpenWeather(target, desktop.ApiKey);
                    _cityEditor.Text = "";
                    _cityEditor.CursorPos = 0;
                    _cityEditor.ClearSelection();
                    desktop.SetCurrentApp("weather");
                }
            }
            else
            {
                _cityEditor.HandleKey(keyEvent);
            }
        }
    }
}
