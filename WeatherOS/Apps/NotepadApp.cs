using System;
using System.Collections.Generic;
using Cosmos.System;
using WeatherOS.Graphics;
using WeatherOS.Services;

namespace WeatherOS.Apps
{
    public class NotepadApp : IApp
    {
        public string Id => "notepad";
        public string Name => "Notepad";
        public bool IsRealTime => false;

        private int _notepadState = 0; // 0=Menu, 1=Editor, 2=PromptOpen, 3=PromptSave, 4=PromptDelete, 5=ListFiles, 6=SelectStorageDestination
        private string _notepadFilename = "untitled.txt";
        private string _lastStatus = "Storage Ready (RAM & VFS Storage)";

        private TextEditorState _editorState = new TextEditorState { AllowMultiline = true, MaxLength = 2048 };
        private TextEditorState _promptEditor = new TextEditorState { AllowMultiline = false, MaxLength = 40 };
        private int _selectedLocationIndex = 0;

        public void Draw(TuiDesktop desktop)
        {
            string title = _notepadState == 1 ? $"NOTEPAD: {_notepadFilename}" : "NOTEPAD (VFS Storage Manager)";
            desktop.DrawWindow(5, 3, 70, 18, title);
            ConsoleColor winBg = desktop.IsDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;
            System.Console.ForegroundColor = ConsoleColor.White;

            if (_notepadState == 0) // Main Menu
            {
                System.Console.SetCursorPosition(10, 5);
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.Write($"Active File : {_notepadFilename} ({_editorState.Text.Length} chars)");

                System.Console.ForegroundColor = ConsoleColor.White;
                System.Console.SetCursorPosition(10, 7);
                System.Console.Write("1. New Note (Clear Editor)");
                System.Console.SetCursorPosition(10, 8);
                System.Console.Write("2. Open Note from Storage");
                System.Console.SetCursorPosition(10, 9);
                System.Console.Write("3. Save Current Note (Select Storage Destination)");
                System.Console.SetCursorPosition(10, 10);
                System.Console.Write("4. Delete Note from Storage");
                System.Console.SetCursorPosition(10, 11);
                System.Console.Write("5. List All Stored Files");
                System.Console.SetCursorPosition(10, 12);
                System.Console.Write("6. Return to Desktop");

                // Status message bar
                System.Console.SetCursorPosition(10, 14);
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.Write("[1-5] Select Option  |  [ESC] Return to Desktop");

                System.Console.SetCursorPosition(10, 16);
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.Write("Status: " + _lastStatus);
            }
            else if (_notepadState == 1) // Editor Mode
            {
                string fullText = _editorState.Text;
                string[] lines = fullText.Split('\n');
                int runningCharIndex = 0;

                for (int lineIdx = 0; lineIdx < lines.Length && lineIdx < 13; lineIdx++)
                {
                    System.Console.SetCursorPosition(7, 5 + lineIdx);
                    string line = lines[lineIdx];

                    for (int col = 0; col < line.Length && col < 65; col++)
                    {
                        int globalIdx = runningCharIndex + col;
                        bool isSel = _editorState.HasSelection && globalIdx >= _editorState.SelMin && globalIdx < _editorState.SelMax;

                        System.Console.BackgroundColor = isSel ? ConsoleColor.White : winBg;
                        System.Console.ForegroundColor = isSel ? ConsoleColor.Black : ConsoleColor.White;
                        System.Console.Write(line[col]);
                    }

                    // Cursor indicator if cursor is on this line
                    int lineEnd = runningCharIndex + line.Length;
                    if (!_editorState.HasSelection && _editorState.CursorPos >= runningCharIndex && _editorState.CursorPos <= lineEnd)
                    {
                        int curCol = _editorState.CursorPos - runningCharIndex;
                        if (curCol >= 0 && curCol <= 65)
                        {
                            System.Console.SetCursorPosition(7 + curCol, 5 + lineIdx);
                            System.Console.BackgroundColor = winBg;
                            System.Console.ForegroundColor = ConsoleColor.Yellow;
                            System.Console.Write("_");
                        }
                    }

                    runningCharIndex += line.Length + 1;
                }

                if (lines.Length == 0)
                {
                    System.Console.SetCursorPosition(7, 5);
                    System.Console.ForegroundColor = ConsoleColor.Yellow;
                    System.Console.Write("_");
                }

                // Essential shortcut bar
                System.Console.SetCursorPosition(7, 18);
                System.Console.BackgroundColor = winBg;
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.Write("[F2 / Ctrl+S] Save Note  |  [ESC] Menu / Exit");

                // File metrics and status
                System.Console.SetCursorPosition(7, 19);
                System.Console.BackgroundColor = winBg;
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                string stLine = $"File: {_notepadFilename} ({_editorState.Text.Length} chars)";
                if (!string.IsNullOrEmpty(_lastStatus)) stLine += $" | {_lastStatus}";
                if (stLine.Length > 65) stLine = stLine.Substring(0, 65);
                System.Console.Write(stLine.PadRight(65));
            }
            else if (_notepadState == 5) // File List Viewer
            {
                System.Console.SetCursorPosition(10, 5);
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.Write("AVAILABLE FILES IN STORAGE:");

                System.Console.ForegroundColor = ConsoleColor.White;
                var files = StorageService.GetFileList();
                if (files.Count == 0)
                {
                    System.Console.SetCursorPosition(10, 7);
                    System.Console.Write("(No files saved yet. Create and save a file first!)");
                }
                else
                {
                    for (int i = 0; i < files.Count && i < 10; i++)
                    {
                        System.Console.SetCursorPosition(10, 7 + i);
                        System.Console.Write($"• {files[i]}");
                    }
                }

                System.Console.SetCursorPosition(10, 18);
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.Write("[Any Key / ESC] Return to Menu");
            }
            else if (_notepadState == 6) // Select Storage Destination & FileSystem Mini-Menu
            {
                System.Console.SetCursorPosition(8, 5);
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.Write($"SELECT STORAGE TARGET FOR: {_notepadFilename}");

                System.Console.SetCursorPosition(8, 6);
                System.Console.ForegroundColor = ConsoleColor.Gray;
                System.Console.Write($"Content Size: {_editorState.Text.Length} characters");

                System.Console.SetCursorPosition(8, 8);
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.Write("Available Storage Devices & File Systems:");

                var locations = StorageService.GetStorageLocations();
                for (int i = 0; i < locations.Count && i < 4; i++)
                {
                    System.Console.SetCursorPosition(8, 10 + i * 2);
                    if (i == _selectedLocationIndex)
                    {
                        System.Console.BackgroundColor = ConsoleColor.White;
                        System.Console.ForegroundColor = ConsoleColor.Black;
                    }
                    else
                    {
                        System.Console.BackgroundColor = winBg;
                        System.Console.ForegroundColor = locations[i].IsReadOnly ? ConsoleColor.DarkGray : ConsoleColor.White;
                    }
                    string opt = $" [{i + 1}] {locations[i].GetDescription()} ";
                    if (opt.Length > 62) opt = opt.Substring(0, 59) + "... ";
                    System.Console.Write(opt.PadRight(62));
                }

                System.Console.SetCursorPosition(8, 18);
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.Write("[Enter / 1-4] Save to Target  |  [ESC] Return to Editor");
            }
            else // Prompts (2=Open, 3=Save Filename, 4=Delete)
            {
                string prompt = "";
                if (_notepadState == 2) prompt = "ENTER FILENAME TO OPEN:";
                else if (_notepadState == 3) prompt = "ENTER FILENAME TO SAVE:";
                else if (_notepadState == 4) prompt = "ENTER FILENAME TO DELETE:";

                System.Console.SetCursorPosition(10, 8);
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.Write(prompt);

                System.Console.SetCursorPosition(10, 10);
                System.Console.ForegroundColor = ConsoleColor.White;
                System.Console.Write("> ");
                _promptEditor.DrawInline(12, 10, 35, winBg, ConsoleColor.White);

                System.Console.SetCursorPosition(10, 13);
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.Write("[Enter] Confirm  |  [ESC] Cancel");
            }
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (_notepadState == 0) // Main Menu
            {
                if (keyEvent.KeyChar == '1')
                {
                    _editorState.Text = "";
                    _editorState.CursorPos = 0;
                    _editorState.ClearSelection();
                    _notepadFilename = "untitled.txt";
                    _notepadState = 1;
                }
                else if (keyEvent.KeyChar == '2')
                {
                    _promptEditor.Text = _notepadFilename;
                    _promptEditor.CursorPos = _promptEditor.Text.Length;
                    _promptEditor.ClearSelection();
                    _notepadState = 2;
                }
                else if (keyEvent.KeyChar == '3')
                {
                    _promptEditor.Text = _notepadFilename;
                    _promptEditor.CursorPos = _promptEditor.Text.Length;
                    _promptEditor.ClearSelection();
                    _notepadState = 3;
                }
                else if (keyEvent.KeyChar == '4')
                {
                    _promptEditor.Text = _notepadFilename;
                    _promptEditor.CursorPos = _promptEditor.Text.Length;
                    _promptEditor.ClearSelection();
                    _notepadState = 4;
                }
                else if (keyEvent.KeyChar == '5')
                {
                    _notepadState = 5;
                }
                else if (keyEvent.KeyChar == '6' || keyEvent.Key == ConsoleKeyEx.Escape)
                {
                    desktop.SetCurrentApp("launcher");
                }
            }
            else if (_notepadState == 1) // Editor Mode
            {
                bool isCtrl = (keyEvent.Modifiers & ConsoleModifiers.Control) != 0;
                bool isSave = keyEvent.Key == ConsoleKeyEx.F2 ||
                              (isCtrl && (keyEvent.Key == ConsoleKeyEx.S || keyEvent.KeyChar == 's' || keyEvent.KeyChar == 'S')) ||
                              keyEvent.KeyChar == 19; // ASCII 19 = Ctrl+S

                if (keyEvent.Key == ConsoleKeyEx.Escape)
                {
                    _notepadState = 0;
                }
                else if (isSave)
                {
                    if (_notepadFilename == "untitled.txt")
                    {
                        _promptEditor.Text = _notepadFilename;
                        _promptEditor.CursorPos = _promptEditor.Text.Length;
                        _promptEditor.ClearSelection();
                        _notepadState = 3;
                    }
                    else
                    {
                        _selectedLocationIndex = 0;
                        _notepadState = 6; // Destination Mini-Menu
                    }
                }
                else
                {
                    _editorState.HandleKey(keyEvent);
                }
            }
            else if (_notepadState == 5) // File List Viewer
            {
                _notepadState = 0;
            }
            else if (_notepadState == 6) // Destination Selector Mini-Menu
            {
                var locations = StorageService.GetStorageLocations();
                if (keyEvent.Key == ConsoleKeyEx.Escape)
                {
                    _notepadState = 1; // Return to editor
                }
                else if (keyEvent.Key == ConsoleKeyEx.UpArrow)
                {
                    _selectedLocationIndex--;
                    if (_selectedLocationIndex < 0) _selectedLocationIndex = locations.Count - 1;
                }
                else if (keyEvent.Key == ConsoleKeyEx.DownArrow)
                {
                    _selectedLocationIndex++;
                    if (_selectedLocationIndex >= locations.Count) _selectedLocationIndex = 0;
                }
                else if (keyEvent.KeyChar >= '1' && keyEvent.KeyChar <= '9')
                {
                    int idx = keyEvent.KeyChar - '1';
                    if (idx < locations.Count)
                    {
                        _selectedLocationIndex = idx;
                        SaveToLocation(locations[idx]);
                    }
                }
                else if (keyEvent.Key == ConsoleKeyEx.Enter)
                {
                    if (_selectedLocationIndex >= 0 && _selectedLocationIndex < locations.Count)
                    {
                        SaveToLocation(locations[_selectedLocationIndex]);
                    }
                }
            }
            else if (_notepadState == 2 || _notepadState == 3 || _notepadState == 4) // Prompts
            {
                if (keyEvent.Key == ConsoleKeyEx.Escape)
                {
                    _notepadState = 0;
                }
                else if (keyEvent.Key == ConsoleKeyEx.Enter)
                {
                    string targetFile = StorageService.NormalizeFilename(_promptEditor.Text);

                    if (_notepadState == 2) // Open File
                    {
                        string content;
                        string status;
                        bool ok = StorageService.LoadFile(targetFile, out content, out status);
                        _lastStatus = status;
                        if (ok)
                        {
                            _notepadFilename = targetFile;
                            _editorState.Text = content;
                            _editorState.CursorPos = content.Length;
                            _editorState.ClearSelection();
                            _notepadState = 1; // Open directly in editor!
                        }
                        else
                        {
                            _notepadState = 0;
                        }
                    }
                    else if (_notepadState == 3) // Save Prompt -> Select Destination Menu
                    {
                        _notepadFilename = targetFile;
                        _selectedLocationIndex = 0;
                        _notepadState = 6; // Transition to mini-menu to pick device/filesystem!
                    }
                    else if (_notepadState == 4) // Delete File
                    {
                        string status;
                        StorageService.DeleteFile(targetFile, out status);
                        _lastStatus = status;
                        if (string.Equals(_notepadFilename, targetFile, StringComparison.OrdinalIgnoreCase))
                        {
                            _notepadFilename = "untitled.txt";
                            _editorState.Text = "";
                            _editorState.CursorPos = 0;
                            _editorState.ClearSelection();
                        }
                        _notepadState = 0;
                    }
                }
                else
                {
                    _promptEditor.HandleKey(keyEvent);
                }
            }
        }

        private void SaveToLocation(StorageLocation loc)
        {
            string status;
            StorageService.SaveFileToLocation(_notepadFilename, _editorState.Text, loc.Id, out status);
            _lastStatus = status;
            _notepadState = 1; // Return to editor with file saved and status shown
        }
    }
}
