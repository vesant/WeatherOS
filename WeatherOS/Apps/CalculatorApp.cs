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
            desktop.DrawWindow(24, 4, 32, 17, "CALCULATOR");
            ConsoleColor winBg = desktop.IsDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;
            System.Console.ForegroundColor = ConsoleColor.White;

            System.Console.SetCursorPosition(27, 6);
            System.Console.Write($"[{_calcResult.PadLeft(24)}]");

            System.Console.SetCursorPosition(29, 9);
            System.Console.Write("[7]  [8]  [9]  [/]");
            System.Console.SetCursorPosition(29, 11);
            System.Console.Write("[4]  [5]  [6]  [*]");
            System.Console.SetCursorPosition(29, 13);
            System.Console.Write("[1]  [2]  [3]  [-]");
            System.Console.SetCursorPosition(29, 15);
            System.Console.Write("[0]  [.]  [C]  [+]");

            System.Console.SetCursorPosition(27, 18);
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.Write("> ");
            _calcEditor.DrawInline(29, 18, 20, winBg, ConsoleColor.Cyan);
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.KeyChar == 'c' || keyEvent.KeyChar == 'C')
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
