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

        private int _notepadState = 0; // 0=Menu, 1=Editor, 2=PromptOpen/WindowsPicker, 3=PromptSave, 4=PromptDelete, 5=ListFiles, 6=SelectStorageDestination
        private string _notepadFilename = "untitled.txt";
        private string _lastStatus = "Storage Ready (RAM & VFS Storage)";

        private TextEditorState _editorState = new TextEditorState { AllowMultiline = true, MaxLength = 2048 };
        private TextEditorState _promptEditor = new TextEditorState { AllowMultiline = false, MaxLength = 40 };
        private int _selectedLocationIndex = 0;

        // Windows-like Open File Picker State
        private int _openDialogLocationIndex = 0;
        private int _openDialogSelectedFileIndex = 0;
        private int _openDialogScrollOffset = 0;
        private List<StorageFileInfo> _openDialogFiles = new List<StorageFileInfo>();
        private List<StorageLocation> _cachedLocations = new List<StorageLocation>();
        private int _openPreviousState = 0; // 0=Menu, 1=Editor

        public void Draw(TuiDesktop desktop)
        {
            if (_notepadState == 2)
            {
                DrawOpenFileDialog(desktop);
                return;
            }

            string title;
            if (_notepadState == 1)
                title = $"NOTEPAD: {_notepadFilename}  |  [F2] Save  [ESC] Menu";
            else if (_notepadState == 6)
                title = "NOTEPAD: Select Storage Target  |  [ESC] Cancel";
            else
                title = "NOTEPAD (Storage Manager)  |  [ESC] Exit";

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
                string stMsg = "Status: " + _lastStatus;
                if (stMsg.Length > 63) stMsg = stMsg.Substring(0, 60) + "...";
                System.Console.Write(stMsg.PadRight(63));
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
                System.Console.Write("[F2] Save Note  |  [Ctrl+O] Open Note  |  [ESC] Menu");

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
                System.Console.Write("Available Storage Devices & Partitions:");

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

                System.Console.BackgroundColor = winBg;
                System.Console.SetCursorPosition(8, 17);
                System.Console.ForegroundColor = ConsoleColor.DarkCyan;
                string stDisp = "Status: " + _lastStatus;
                if (stDisp.Length > 62) stDisp = stDisp.Substring(0, 59) + "...";
                System.Console.Write(stDisp.PadRight(62));

                System.Console.SetCursorPosition(8, 18);
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                int maxOpt = Math.Min(locations.Count, 4);
                System.Console.Write($"[Enter / 1-{maxOpt}] Save to Target  |  [ESC] Return to Editor");
            }
            else // Prompts (3=Save Filename, 4=Delete)
            {
                string prompt = "";
                if (_notepadState == 3) prompt = "ENTER FILENAME TO SAVE:";
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
                    _openPreviousState = 0;
                    OpenFileDialogStart();
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

                bool isOpen = keyEvent.Key == ConsoleKeyEx.F3 ||
                              (isCtrl && (keyEvent.Key == ConsoleKeyEx.O || keyEvent.KeyChar == 'o' || keyEvent.KeyChar == 'O')) ||
                              keyEvent.KeyChar == 15; // ASCII 15 = Ctrl+O

                if (keyEvent.Key == ConsoleKeyEx.Escape)
                {
                    _notepadState = 0;
                }
                else if (isOpen)
                {
                    _openPreviousState = 1;
                    OpenFileDialogStart();
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
                        _selectedLocationIndex = GetDefaultWritableLocationIndex();
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
            else if (_notepadState == 2) // Windows-like Open File Dialog
            {
                if (keyEvent.Key == ConsoleKeyEx.Escape)
                {
                    _notepadState = _openPreviousState;
                }
                else if (keyEvent.Key == ConsoleKeyEx.UpArrow)
                {
                    if (_openDialogFiles != null && _openDialogFiles.Count > 0)
                    {
                        if (_openDialogSelectedFileIndex > 0)
                        {
                            _openDialogSelectedFileIndex--;
                            if (_openDialogSelectedFileIndex < _openDialogScrollOffset)
                            {
                                _openDialogScrollOffset = _openDialogSelectedFileIndex;
                            }
                        }
                    }
                }
                else if (keyEvent.Key == ConsoleKeyEx.DownArrow)
                {
                    if (_openDialogFiles != null && _openDialogFiles.Count > 0)
                    {
                        if (_openDialogSelectedFileIndex < _openDialogFiles.Count - 1)
                        {
                            _openDialogSelectedFileIndex++;
                            if (_openDialogSelectedFileIndex >= _openDialogScrollOffset + 8)
                            {
                                _openDialogScrollOffset = _openDialogSelectedFileIndex - 7;
                            }
                        }
                    }
                }
                else if (keyEvent.Key == ConsoleKeyEx.Tab || keyEvent.Key == ConsoleKeyEx.RightArrow)
                {
                    if (_cachedLocations != null && _cachedLocations.Count > 0)
                    {
                        _openDialogLocationIndex = (_openDialogLocationIndex + 1) % _cachedLocations.Count;
                        RefreshOpenDialogFiles();
                    }
                }
                else if (keyEvent.Key == ConsoleKeyEx.LeftArrow)
                {
                    if (_cachedLocations != null && _cachedLocations.Count > 0)
                    {
                        _openDialogLocationIndex = (_openDialogLocationIndex - 1 + _cachedLocations.Count) % _cachedLocations.Count;
                        RefreshOpenDialogFiles();
                    }
                }
                else if (keyEvent.Key == ConsoleKeyEx.Enter)
                {
                    if (_openDialogFiles != null && _openDialogFiles.Count > 0 &&
                        _openDialogSelectedFileIndex >= 0 && _openDialogSelectedFileIndex < _openDialogFiles.Count)
                    {
                        var picked = _openDialogFiles[_openDialogSelectedFileIndex];
                        string content;
                        string status;
                        bool ok = StorageService.LoadFileFromLocation(picked.Name, picked.LocationId, out content, out status);
                        _lastStatus = status;
                        if (ok)
                        {
                            _notepadFilename = picked.Name;
                            _editorState.Text = content ?? "";
                            _editorState.CursorPos = _editorState.Text.Length;
                            _editorState.ClearSelection();
                            _notepadState = 1; // Opens directly in editor!
                        }
                    }
                    else
                    {
                        _lastStatus = "No file available to open on this drive.";
                    }
                }
            }
            else if (_notepadState == 3 || _notepadState == 4) // Prompts (3=Save Filename, 4=Delete)
            {
                if (keyEvent.Key == ConsoleKeyEx.Escape)
                {
                    _notepadState = 0;
                }
                else if (keyEvent.Key == ConsoleKeyEx.Enter)
                {
                    string targetFile = StorageService.NormalizeFilename(_promptEditor.Text);

                    if (_notepadState == 3) // Save Prompt -> Select Destination Menu
                    {
                        _notepadFilename = targetFile;
                        _selectedLocationIndex = GetDefaultWritableLocationIndex();
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

        private void OpenFileDialogStart()
        {
            _cachedLocations = StorageService.GetStorageLocations();
            if (_cachedLocations == null || _cachedLocations.Count == 0)
            {
                _cachedLocations = new List<StorageLocation>();
            }
            if (_openDialogLocationIndex >= _cachedLocations.Count)
            {
                _openDialogLocationIndex = 0;
            }
            RefreshOpenDialogFiles();
            _notepadState = 2;
        }

        private void RefreshOpenDialogFiles()
        {
            string locId = "disk0";
            if (_cachedLocations != null && _openDialogLocationIndex >= 0 && _openDialogLocationIndex < _cachedLocations.Count)
            {
                locId = _cachedLocations[_openDialogLocationIndex].Id;
            }
            _openDialogFiles = StorageService.GetFilesForLocation(locId);
            _openDialogSelectedFileIndex = 0;
            _openDialogScrollOffset = 0;

            if (_openDialogFiles != null && !string.IsNullOrEmpty(_notepadFilename))
            {
                for (int i = 0; i < _openDialogFiles.Count; i++)
                {
                    if (string.Equals(_openDialogFiles[i].Name, _notepadFilename, StringComparison.OrdinalIgnoreCase))
                    {
                        _openDialogSelectedFileIndex = i;
                        if (_openDialogSelectedFileIndex >= 8) _openDialogScrollOffset = _openDialogSelectedFileIndex - 7;
                        break;
                    }
                }
            }
        }

        private void DrawOpenFileDialog(TuiDesktop desktop)
        {
            desktop.DrawWindow(4, 2, 72, 20, "OPEN FILE  |  WINDOWS FILE PICKER");
            ConsoleColor winBg = desktop.IsDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;

            // Header / Look In:
            System.Console.SetCursorPosition(6, 4);
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.Write("Look in: ");

            string driveName = "Disk 0";
            if (_cachedLocations != null && _openDialogLocationIndex >= 0 && _openDialogLocationIndex < _cachedLocations.Count)
            {
                driveName = _cachedLocations[_openDialogLocationIndex].GetDescription();
            }
            string driveBox = $"[ < {driveName} > ]";
            if (driveBox.Length > 44) driveBox = driveBox.Substring(0, 41) + "... > ]";

            System.Console.BackgroundColor = ConsoleColor.DarkGray;
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.Write(driveBox);

            System.Console.BackgroundColor = winBg;
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.Write("  [Tab: Switch Drive]");

            // File table header
            System.Console.SetCursorPosition(6, 6);
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            string th = "  Name                    Size        Type            Location        ";
            System.Console.Write(th.PadRight(68));

            System.Console.SetCursorPosition(6, 7);
            System.Console.ForegroundColor = ConsoleColor.DarkCyan;
            System.Console.Write(new string('-', 68));

            // File list (8 visible rows: y = 8 to 15)
            int visibleRows = 8;
            if (_openDialogFiles == null || _openDialogFiles.Count == 0)
            {
                System.Console.SetCursorPosition(6, 10);
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.Write("  (No files found on this drive. Press [Tab] to switch drive)");

                for (int r = 1; r < visibleRows; r++)
                {
                    System.Console.SetCursorPosition(6, 8 + r);
                    if (8 + r != 10) System.Console.Write(new string(' ', 68));
                }
            }
            else
            {
                for (int r = 0; r < visibleRows; r++)
                {
                    int fileIdx = _openDialogScrollOffset + r;
                    System.Console.SetCursorPosition(6, 8 + r);

                    if (fileIdx < _openDialogFiles.Count)
                    {
                        var f = _openDialogFiles[fileIdx];
                        bool isSelected = (fileIdx == _openDialogSelectedFileIndex);

                        if (isSelected)
                        {
                            System.Console.BackgroundColor = ConsoleColor.White;
                            System.Console.ForegroundColor = ConsoleColor.Black;
                        }
                        else
                        {
                            System.Console.BackgroundColor = winBg;
                            System.Console.ForegroundColor = ConsoleColor.White;
                        }

                        string marker = isSelected ? "> " : "  ";
                        string namePart = f.Name;
                        if (namePart.Length > 20) namePart = namePart.Substring(0, 17) + "...";

                        string sizePart = FormatFileSize(f.Size);
                        string typePart = FormatFileType(f.Name);
                        string locPart = f.LocationName;
                        if (locPart.Length > 14) locPart = locPart.Substring(0, 12) + "..";

                        string rowText = marker + namePart.PadRight(22) + sizePart.PadRight(12) + typePart.PadRight(16) + locPart.PadRight(16);
                        if (rowText.Length > 68) rowText = rowText.Substring(0, 68);
                        System.Console.Write(rowText.PadRight(68));
                    }
                    else
                    {
                        System.Console.BackgroundColor = winBg;
                        System.Console.Write(new string(' ', 68));
                    }
                }
            }

            System.Console.BackgroundColor = winBg;
            System.Console.SetCursorPosition(6, 16);
            System.Console.ForegroundColor = ConsoleColor.DarkCyan;
            System.Console.Write(new string('-', 68));

            // Bottom Selected File Box
            string selectedFileName = "";
            if (_openDialogFiles != null && _openDialogSelectedFileIndex >= 0 && _openDialogSelectedFileIndex < _openDialogFiles.Count)
            {
                selectedFileName = _openDialogFiles[_openDialogSelectedFileIndex].Name;
            }
            System.Console.SetCursorPosition(6, 17);
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.Write("File name:     ");
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.Write($"[ {selectedFileName.PadRight(47)} ]");

            System.Console.SetCursorPosition(6, 18);
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.Write("Files of type: ");
            System.Console.ForegroundColor = ConsoleColor.Gray;
            System.Console.Write($"[ All Files (*.*, *.txt, *.ini, *.log)          ]");

            // Status / instructions bar
            System.Console.SetCursorPosition(6, 20);
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.Write("[▲/▼] Select  |  [Tab/◄/►] Switch Drive  |  [Enter] Open  |  [ESC] Cancel");

            if (!string.IsNullOrEmpty(_lastStatus))
            {
                System.Console.SetCursorPosition(6, 21);
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                string st = "Status: " + _lastStatus;
                if (st.Length > 68) st = st.Substring(0, 65) + "...";
                System.Console.Write(st.PadRight(68));
            }
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes <= 0) return "0 B";
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024} KB";
            return $"{bytes / (1024 * 1024)} MB";
        }

        private static string FormatFileType(string filename)
        {
            if (string.IsNullOrEmpty(filename)) return "File";
            string u = filename.ToUpper();
            if (u.EndsWith(".TXT")) return "Text Document";
            if (u.EndsWith(".INI") || u.EndsWith(".CFG")) return "Configuration";
            if (u.EndsWith(".LOG")) return "Log File";
            if (u.EndsWith(".DAT") || u.EndsWith(".BIN")) return "Binary Data";
            return "Document";
        }

        private int GetDefaultWritableLocationIndex()
        {
            var locs = StorageService.GetStorageLocations();
            for (int i = 0; i < locs.Count; i++)
            {
                if (!locs[i].IsReadOnly) return i;
            }
            return 0;
        }

        private void SaveToLocation(StorageLocation loc)
        {
            string status;
            StorageService.SaveFileToLocation(_notepadFilename, _editorState.Text, loc.Id, out status);
            _lastStatus = status;
            _notepadState = 1; // Return to editor with file saved and status shown
        }

        public void OpenDirect(string filename, string content)
        {
            _notepadFilename = StorageService.NormalizeFilename(filename);
            _editorState.Text = content ?? "";
            _editorState.CursorPos = _editorState.Text.Length;
            _editorState.ClearSelection();
            _notepadState = 1;
            _lastStatus = $"Loaded {_notepadFilename} [OK]";
        }
    }
}
