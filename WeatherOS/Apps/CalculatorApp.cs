using System;
using Cosmos.System;
using WeatherOS.Graphics;
using WeatherOS.Services;

namespace WeatherOS.Apps
{
    public class CalculatorApp : IApp
    {
        public string Id => "calculator";
        public string Name => "Calculator";
        public bool IsRealTime => false;

        private TextEditorState _calcEditor = new TextEditorState { AllowMultiline = false, MaxLength = 18 };
        private string _calcResult = "";

        public void Draw(TuiDesktop desktop)
        {
            desktop.DrawWindow(24, 3, 32, 18, "CALCULATOR");
            ConsoleColor winBg = desktop.IsDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;
            System.Console.ForegroundColor = ConsoleColor.White;

            System.Console.SetCursorPosition(27, 5);
            System.Console.Write($"[{_calcResult.PadLeft(24)}]");

            System.Console.SetCursorPosition(29, 8);
            System.Console.Write("[7]  [8]  [9]  [/]");
            System.Console.SetCursorPosition(29, 10);
            System.Console.Write("[4]  [5]  [6]  [*]");
            System.Console.SetCursorPosition(29, 12);
            System.Console.Write("[1]  [2]  [3]  [-]");
            System.Console.SetCursorPosition(29, 14);
            System.Console.Write("[0]  [.]  [C]  [+]");

            System.Console.SetCursorPosition(27, 16);
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.Write("> ");
            _calcEditor.DrawInline(29, 16, 20, winBg, ConsoleColor.Cyan);

            System.Console.SetCursorPosition(26, 19);
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.Write("[Enter] =  [C] Clr  [ESC] Exit");
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                desktop.SetCurrentApp("launcher");
            }
            else if (keyEvent.KeyChar == 'c' || keyEvent.KeyChar == 'C')
            {
                _calcEditor.Text = "";
                _calcEditor.CursorPos = 0;
                _calcEditor.ClearSelection();
                _calcResult = "0";
            }
            else if (keyEvent.Key == ConsoleKeyEx.Enter)
            {
                try
                {
                    string input = _calcEditor.Text;
                    string[] tokens = input.Split(new char[] { '+', '-', '*', '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length >= 2)
                    {
                        double a = double.Parse(tokens[0]);
                        double b = double.Parse(tokens[1]);
                        if (input.Contains("+")) _calcResult = (a + b).ToString();
                        else if (input.Contains("-")) _calcResult = (a - b).ToString();
                        else if (input.Contains("*")) _calcResult = (a * b).ToString();
                        else if (input.Contains("/")) _calcResult = (b != 0 ? (a / b).ToString() : "Div/0");
                    }
                    else _calcResult = "Err";
                }
                catch { _calcResult = "Err"; }
                _calcEditor.Text = "";
                _calcEditor.CursorPos = 0;
                _calcEditor.ClearSelection();
            }
            else
            {
                _calcEditor.HandleKey(keyEvent);
            }
        }
    }
}
