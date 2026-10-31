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

        private int _gridRow = 0;
        private int _gridCol = 0;
        private string _expression = "";
        private string _calcResult = "0";

        // 5 rows x 4 columns grid
        private static readonly string[][] ButtonLabels = new string[][]
        {
            new string[] { "7", "8", "9", "/" },
            new string[] { "4", "5", "6", "*" },
            new string[] { "1", "2", "3", "-" },
            new string[] { "0", ".", "C", "+" },
            new string[] { "DEL", "DEL", "=", "=" }
        };

                public void Draw(TuiDesktop desktop)
        {
            int winWidth = 46;
            int winHeight = 20;
            int left, top;
            desktop.DrawCenteredWindow(winWidth, winHeight, "CALCULATOR (KEYPAD)", out left, out top);
            ConsoleColor winBg = desktop.IsDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;
            System.Console.ForegroundColor = ConsoleColor.White;

            // Expression display
            string exprDisp = string.IsNullOrEmpty(_expression) ? "0" : _expression;
            if (exprDisp.Length > winWidth - 12) exprDisp = exprDisp.Substring(exprDisp.Length - (winWidth - 12));
            string displayExpr = $"Expr: [{exprDisp.PadLeft(winWidth - 12)}]";
            desktop.WriteInsideWindow(left, winWidth, top + 2, 2, displayExpr, ConsoleColor.Cyan, winBg);

            // Result display
            string resDisp = string.IsNullOrEmpty(_calcResult) ? "0" : _calcResult;
            if (resDisp.Length > winWidth - 12) resDisp = resDisp.Substring(0, winWidth - 12);
            string displayRes = $" =   [{resDisp.PadLeft(winWidth - 12)}]";
            desktop.WriteInsideWindow(left, winWidth, top + 3, 2, displayRes, ConsoleColor.Yellow, winBg);

            // Separator
            desktop.WriteInsideWindow(left, winWidth, top + 4, 1, new string('-', winWidth - 2), ConsoleColor.DarkGray, winBg);

            // 5 Rows of Buttons (Centered in 46-col window: 4 buttons x 8 chars + gaps = 38 chars wide, left margin = left + 4)
            int colStartX = left + 4;
            for (int r = 0; r < 5; r++)
            {
                int rowY = top + 5 + (r * 2);

                if (r < 4)
                {
                    // 4 standard buttons
                    for (int c = 0; c < 4; c++)
                    {
                        string label = ButtonLabels[r][c];
                        bool isSel = (r == _gridRow && c == _gridCol);
                        ConsoleColor bBg = isSel ? ConsoleColor.Yellow : ConsoleColor.DarkCyan;
                        ConsoleColor bFg = isSel ? ConsoleColor.Black : ConsoleColor.White;

                        string btnText = $"[  {label}  ]";
                        Screen.WriteClipped(colStartX + (c * 10), rowY, btnText, btnText.Length, bFg, bBg);
                    }
                }
                else
                {
                    // Row 4: Wide DEL and = buttons
                    bool isSelDel = (_gridRow == 4 && _gridCol < 2);
                    bool isSelEq = (_gridRow == 4 && _gridCol >= 2);

                    ConsoleColor delBg = isSelDel ? ConsoleColor.Yellow : ConsoleColor.DarkCyan;
                    ConsoleColor delFg = isSelDel ? ConsoleColor.Black : ConsoleColor.White;
                    Screen.WriteClipped(colStartX, rowY, "[    CLEAR / DEL    ]", 21, delFg, delBg);

                    ConsoleColor eqBg = isSelEq ? ConsoleColor.Yellow : ConsoleColor.DarkGreen;
                    ConsoleColor eqFg = isSelEq ? ConsoleColor.Black : ConsoleColor.White;
                    Screen.WriteClipped(colStartX + 22, rowY, "[       =       ]", 17, eqFg, eqBg);
                }
            }

            // Separator before footer
            desktop.WriteInsideWindow(left, winWidth, top + 15, 1, new string('-', winWidth - 2), ConsoleColor.DarkGray, winBg);

            // Footer
            desktop.WriteInsideWindow(left, winWidth, top + 16, 2, "[Arrows] Navigate | [Enter] Select Key | [ESC] Exit", ConsoleColor.Yellow, winBg);
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                desktop.SetCurrentApp("launcher");
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.UpArrow)
            {
                _gridRow = (_gridRow - 1 + 5) % 5;
                return;
            }
            if (keyEvent.Key == ConsoleKeyEx.DownArrow)
            {
                _gridRow = (_gridRow + 1) % 5;
                return;
            }
            if (keyEvent.Key == ConsoleKeyEx.LeftArrow)
            {
                _gridCol = (_gridCol - 1 + 4) % 4;
                return;
            }
            if (keyEvent.Key == ConsoleKeyEx.RightArrow)
            {
                _gridCol = (_gridCol + 1) % 4;
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.Enter)
            {
                PressButton(_gridRow, _gridCol);
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.Backspace)
            {
                DeleteChar();
                return;
            }

            // Keyboard shortcut for Clear
            if (keyEvent.KeyChar == 'c' || keyEvent.KeyChar == 'C')
            {
                ClearAll();
                return;
            }
        }

        private void PressButton(int row, int col)
        {
            if (row < 0 || row >= 5 || col < 0 || col >= 4) return;
            string btn = ButtonLabels[row][col];

            if (btn == "C")
            {
                ClearAll();
            }
            else if (btn == "DEL")
            {
                DeleteChar();
            }
            else if (btn == "=")
            {
                EvaluateExpression();
            }
            else
            {
                AppendChar(btn[0]);
            }
        }

        private void AppendChar(char c)
        {
            if (_expression.Length < 24)
            {
                if ((c == '+' || c == '*' || c == '/') && _expression.Length == 0) return;
                _expression += c;
            }
        }

        private void DeleteChar()
        {
            if (_expression.Length > 0)
            {
                _expression = _expression.Substring(0, _expression.Length - 1);
            }
        }

        private void ClearAll()
        {
            _expression = "";
            _calcResult = "0";
        }

        private void EvaluateExpression()
        {
            if (string.IsNullOrWhiteSpace(_expression))
            {
                _calcResult = "0";
                return;
            }

            try
            {
                string expr = _expression.Trim();

                int opIdx = -1;
                char op = ' ';
                int startSearch = expr.StartsWith("-") ? 1 : 0;
                for (int i = startSearch; i < expr.Length; i++)
                {
                    char c = expr[i];
                    if (c == '+' || c == '-' || c == '*' || c == '/')
                    {
                        op = c;
                        opIdx = i;
                        break;
                    }
                }

                if (opIdx > 0 && opIdx < expr.Length - 1)
                {
                    string sA = expr.Substring(0, opIdx).Trim();
                    string sB = expr.Substring(opIdx + 1).Trim();

                    double a = double.Parse(sA);
                    double b = double.Parse(sB);
                    double res = 0;

                    if (op == '+') res = a + b;
                    else if (op == '-') res = a - b;
                    else if (op == '*') res = a * b;
                    else if (op == '/')
                    {
                        if (Math.Abs(b) < 0.000000001)
                        {
                            _calcResult = "Div/0";
                            return;
                        }
                        res = a / b;
                    }

                    if (Math.Abs(res - Math.Floor(res)) < 0.000001)
                    {
                        _calcResult = ((long)res).ToString();
                    }
                    else
                    {
                        _calcResult = res.ToString("F4").TrimEnd('0');
                    }
                }
                else
                {
                    double single = double.Parse(expr);
                    _calcResult = single.ToString();
                }
            }
            catch
            {
                _calcResult = "Err";
            }
        }
    }
}
