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
            int winWidth = 34;
            int winHeight = 18;
            int left, top;
            desktop.DrawCenteredWindow(winWidth, winHeight, "CALCULATOR", out left, out top);
            ConsoleColor winBg = desktop.IsDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;
            System.Console.ForegroundColor = ConsoleColor.White;

            string display = $"[{_calcResult.PadLeft(winWidth - 8)}]";
            desktop.WriteInsideWindow(left, winWidth, top + 2, 2, display, ConsoleColor.White, winBg);

            desktop.WriteInsideWindow(left, winWidth, top + 5, 4, "[7]  [8]  [9]  [/]", ConsoleColor.White, winBg);
            desktop.WriteInsideWindow(left, winWidth, top + 7, 4, "[4]  [5]  [6]  [*]", ConsoleColor.White, winBg);
            desktop.WriteInsideWindow(left, winWidth, top + 9, 4, "[1]  [2]  [3]  [-]", ConsoleColor.White, winBg);
            desktop.WriteInsideWindow(left, winWidth, top + 11, 4, "[0]  [.]  [C]  [+]", ConsoleColor.White, winBg);

            desktop.WriteInsideWindow(left, winWidth, top + 13, 2, "> ", ConsoleColor.Cyan, winBg);
            _calcEditor.DrawInline(left + 5, top + 13, winWidth - 9, winBg, ConsoleColor.Cyan);

            desktop.WriteInsideWindow(left, winWidth, top + 15, 1, "[Enter]=  [C]Clr  [ESC]Exit", ConsoleColor.Yellow, winBg);
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
