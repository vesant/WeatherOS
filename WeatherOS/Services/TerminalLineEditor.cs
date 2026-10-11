using System;
using System.Collections.Generic;
using Cosmos.System;

namespace WeatherOS.Services
{
    public static class TerminalLineEditor
    {
        private static List<string> _commandHistory = new List<string>();
        private static int _historyIndex = -1;

        public static string ReadLine(string prompt)
        {
            int startY = System.Console.CursorTop;
            int promptLen = prompt.Length;
            var editor = new TextEditorState { AllowMultiline = false, MaxLength = 65 };

            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.BackgroundColor = ConsoleColor.Black;
            System.Console.Write(prompt);

            while (true)
            {
                var keyEvent = KeyboardManager.ReadKey();

                if (keyEvent.Key == ConsoleKeyEx.Enter)
                {
                    System.Console.WriteLine();
                    string result = editor.Text.Trim();
                    if (!string.IsNullOrEmpty(result))
                    {
                        if (_commandHistory.Count == 0 || _commandHistory[_commandHistory.Count - 1] != result)
                        {
                            _commandHistory.Add(result);
                        }
                    }
                    _historyIndex = -1;
                    return editor.Text;
                }

                if (keyEvent.Key == ConsoleKeyEx.UpArrow)
                {
                    if (_commandHistory.Count > 0)
                    {
                        if (_historyIndex == -1) _historyIndex = _commandHistory.Count - 1;
                        else if (_historyIndex > 0) _historyIndex--;

                        editor.Text = _commandHistory[_historyIndex];
                        editor.CursorPos = editor.Text.Length;
                        editor.ClearSelection();
                    }
                }
                else if (keyEvent.Key == ConsoleKeyEx.DownArrow)
                {
                    if (_historyIndex != -1)
                    {
                        if (_historyIndex < _commandHistory.Count - 1)
                        {
                            _historyIndex++;
                            editor.Text = _commandHistory[_historyIndex];
                        }
                        else
                        {
                            _historyIndex = -1;
                            editor.Text = "";
                        }
                        editor.CursorPos = editor.Text.Length;
                        editor.ClearSelection();
                    }
                }
                else
                {
                    editor.HandleKey(keyEvent);
                }

                // Smooth redrawing of current command line
                System.Console.SetCursorPosition(0, startY);
                System.Console.ForegroundColor = ConsoleColor.Green;
                System.Console.BackgroundColor = ConsoleColor.Black;
                System.Console.Write(prompt);

                for (int i = 0; i < editor.Text.Length; i++)
                {
                    bool isSel = editor.HasSelection && i >= editor.SelMin && i < editor.SelMax;
                    System.Console.BackgroundColor = isSel ? ConsoleColor.White : ConsoleColor.Black;
                    System.Console.ForegroundColor = isSel ? ConsoleColor.Black : ConsoleColor.White;
                    System.Console.Write(editor.Text[i]);
                }

                // Clear trailing characters with black background
                System.Console.BackgroundColor = ConsoleColor.Black;
                int written = promptLen + editor.Text.Length;
                int maxClear = Math.Min(System.Console.WindowWidth - 1 - written, 35);
                for (int i = 0; i < maxClear; i++) System.Console.Write(" ");

                // Reposition console hardware cursor to match editor.CursorPos
                int curX = Math.Min(System.Console.WindowWidth - 1, promptLen + editor.CursorPos);
                System.Console.SetCursorPosition(curX, startY);
            }
        }
    }
}
