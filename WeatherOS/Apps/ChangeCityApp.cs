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
            int winWidth = 48;
            int winHeight = 9;
            int left = (80 - winWidth) / 2;
            int top = (24 - winHeight) / 2;

            desktop.DrawWindow(left, top, winWidth, winHeight, "CHANGE CITY");
            ConsoleColor winBg = desktop.IsDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;
            System.Console.ForegroundColor = ConsoleColor.White;

            System.Console.SetCursorPosition(left + 4, top + 2);
            System.Console.Write("Enter new city name:");

            System.Console.SetCursorPosition(left + 4, top + 4);
            System.Console.Write("> ");
            _cityEditor.DrawInline(left + 6, top + 4, 25, winBg, ConsoleColor.White);

            System.Console.SetCursorPosition(left + 4, top + 7);
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.Write("[Enter] Search  |  [ESC] Cancel");
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                desktop.SetCurrentApp("launcher");
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.Enter)
            {
                if (!string.IsNullOrWhiteSpace(_cityEditor.Text))
                {
                    string target = _cityEditor.Text.Trim();
                    int winWidth = 48;
                    int left = (80 - winWidth) / 2;
                    int top = (24 - 9) / 2;

                    ConsoleColor winBg = desktop.IsDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
                    System.Console.BackgroundColor = winBg;
                    System.Console.ForegroundColor = ConsoleColor.Yellow;
                    System.Console.SetCursorPosition(left + 4, top + 6);
                    string status = $"Fetching weather for '{target}'...";
                    if (status.Length > winWidth - 8) status = status.Substring(0, winWidth - 8);
                    System.Console.Write(status.PadRight(winWidth - 8));

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
