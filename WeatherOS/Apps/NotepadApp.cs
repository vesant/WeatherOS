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

            var win = desktop.CreateWindow(title, WindowSize.Large);

            if (_notepadState == 0) // Main Menu
            {
                win.WriteRow(1, $"Active File : {_notepadFilename} ({_editorState.Text.Length} chars)", ConsoleColor.Yellow, 2);

                win.WriteRow(3, "1. New Note (Clear Editor)", ConsoleColor.White, 2);
                win.WriteRow(4, "2. Open Note from Storage", ConsoleColor.White, 2);
                win.WriteRow(5, "3. Save Current Note (Select Storage Destination)", ConsoleColor.White, 2);
                win.WriteRow(6, "4. Delete Note from Storage", ConsoleColor.White, 2);
                win.WriteRow(7, "5. List All Stored Files", ConsoleColor.White, 2);
                win.WriteRow(8, "6. Return to Desktop", ConsoleColor.White, 2);

                if (!string.IsNullOrEmpty(_lastStatus))
                {
                    win.WriteRow(10, "Status: " + _lastStatus, ConsoleColor.Cyan, 2);
                }

                win.WriteFooter("[1-6] Select Option  |  [ESC] Return to Desktop", ConsoleColor.Yellow);
            }
            else if (_notepadState == 1) // Editor Mode
            {
                string fullText = _editorState.Text;
                string[] lines = fullText.Split('\n');
                int runningCharIndex = 0;

                for (int lineIdx = 0; lineIdx < lines.Length && lineIdx < 11; lineIdx++)
                {
                    int lineY = win.InnerTop + 1 + lineIdx;
                    string line = lines[lineIdx];

                    for (int col = 0; col < line.Length && col < win.InnerWidth - 4; col++)
                    {
                        int globalIdx = runningCharIndex + col;
                        bool isSel = _editorState.HasSelection && globalIdx >= _editorState.SelMin && globalIdx < _editorState.SelMax;

                        System.Console.SetCursorPosition(win.InnerLeft + 2 + col, lineY);
                        System.Console.BackgroundColor = isSel ? ConsoleColor.White : win.Background;
                        System.Console.ForegroundColor = isSel ? ConsoleColor.Black : ConsoleColor.White;
                        System.Console.Write(line[col]);
                    }

                    // Cursor indicator if cursor is on this line
                    int lineEnd = runningCharIndex + line.Length;
                    if (!_editorState.HasSelection && _editorState.CursorPos >= runningCharIndex && _editorState.CursorPos <= lineEnd)
                    {
                        int curCol = _editorState.CursorPos - runningCharIndex;
                        if (curCol >= 0 && curCol <= win.InnerWidth - 4)
                        {
                            System.Console.SetCursorPosition(win.InnerLeft + 2 + curCol, lineY);
                            System.Console.BackgroundColor = win.Background;
                            System.Console.ForegroundColor = ConsoleColor.Yellow;
                            System.Console.Write("_");
                        }
                    }

                    runningCharIndex += line.Length + 1;
                }

                if (lines.Length == 0)
                {
                    System.Console.SetCursorPosition(win.InnerLeft + 2, win.InnerTop + 1);
                    System.Console.ForegroundColor = ConsoleColor.Yellow;
                    System.Console.Write("_");
                }

                // File metrics and status
                string stLine = $"File: {_notepadFilename} ({_editorState.Text.Length} chars)";
                if (!string.IsNullOrEmpty(_lastStatus)) stLine += $" | {_lastStatus}";
                win.WriteRow(13, stLine, ConsoleColor.Cyan, 1);

                // Essential shortcut bar
                win.WriteFooter("[F2] Save Note  |  [Ctrl+O] Open Note  |  [ESC] Menu", ConsoleColor.Yellow);
            }
            else if (_notepadState == 5) // File List Viewer
            {
                win.WriteRow(1, "AVAILABLE FILES IN STORAGE:", ConsoleColor.Yellow, 2);

                var files = StorageService.GetFileList();
                if (files.Count == 0)
                {
                    win.WriteRow(3, "(No files saved yet. Create and save a file first!)", ConsoleColor.White, 2);
                }
                else
                {
                    for (int i = 0; i < files.Count && i < 8; i++)
                    {
                        win.WriteRow(3 + i, $"* {files[i]}", ConsoleColor.White, 2);
                    }
                }

                win.WriteFooter("[Any Key / ESC] Return to Menu", ConsoleColor.Yellow);
            }
            else if (_notepadState == 6) // Select Storage Destination & FileSystem Mini-Menu
            {
                win.WriteRow(1, $"SELECT STORAGE TARGET FOR: {_notepadFilename}", ConsoleColor.Yellow, 2);
                win.WriteRow(2, $"Content Size: {_editorState.Text.Length} characters", ConsoleColor.Gray, 2);
                win.WriteRow(4, "Available Storage Devices & Partitions:", ConsoleColor.Cyan, 2);

                var locations = StorageService.GetStorageLocations();
                for (int i = 0; i < locations.Count && i < 4; i++)
                {
                    string opt = $"[{i + 1}] {locations[i].GetDescription()}";
                    win.WriteSelectionRow(6 + i * 2, opt, i == _selectedLocationIndex, 2);
                }

                if (!string.IsNullOrEmpty(_lastStatus))
                {
                    win.WriteRow(13, "Status: " + _lastStatus, ConsoleColor.DarkCyan, 2);
                }

                int maxOpt = Math.Min(locations.Count, 4);
                win.WriteFooter($"[Enter / 1-{maxOpt}] Save to Target  |  [ESC] Return to Editor", ConsoleColor.Yellow);
            }
            else // Prompts (3=Save Filename, 4=Delete)
            {
                string prompt = "";
                if (_notepadState == 3) prompt = "ENTER FILENAME TO SAVE:";
                else if (_notepadState == 4) prompt = "ENTER FILENAME TO DELETE:";

                win.WriteRow(3, prompt, ConsoleColor.Yellow, 2);
                win.WriteAt(2, 5, "> ", ConsoleColor.White);
                _promptEditor.DrawInline(win.InnerLeft + 4, win.InnerTop + 5, 35, win.Background, ConsoleColor.White);

                win.WriteFooter("[Enter] Confirm  |  [ESC] Cancel", ConsoleColor.Yellow);
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
            string locId = "ram";
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
            var win = desktop.CreateWindow("OPEN FILE  |  WINDOWS FILE PICKER", WindowSize.FullScreen);

            // Header / Look In:
            string driveName = "RAM Virtual Store";
            if (_cachedLocations != null && _openDialogLocationIndex >= 0 && _openDialogLocationIndex < _cachedLocations.Count)
            {
                driveName = _cachedLocations[_openDialogLocationIndex].GetDescription();
            }
            string driveBox = $"[ < {driveName} > ]";
            if (driveBox.Length > 44) driveBox = driveBox.Substring(0, 41) + "... > ]";

            win.WriteRow(1, "Look in: " + driveBox + "  [Tab: Switch Drive]", ConsoleColor.Yellow, 2);

            // File table header
            string th = "  Name                    Size        Type            Location        ";
            win.WriteRow(2, th, ConsoleColor.Yellow, 2);
            win.WriteSeparator(3);

            // File list (8 visible rows: relY = 4 to 11)
            int visibleRows = 8;
            if (_openDialogFiles == null || _openDialogFiles.Count == 0)
            {
                win.WriteRow(5, "(No files found on this drive. Press [Tab] to switch drive)", ConsoleColor.Yellow, 2);
            }
            else
            {
                for (int r = 0; r < visibleRows; r++)
                {
                    int fileIdx = _openDialogScrollOffset + r;

                    if (fileIdx < _openDialogFiles.Count)
                    {
                        var f = _openDialogFiles[fileIdx];
                        bool isSelected = (fileIdx == _openDialogSelectedFileIndex);

                        string namePart = f.Name;
                        if (namePart.Length > 20) namePart = namePart.Substring(0, 17) + "...";

                        string sizePart = FormatFileSize(f.Size);
                        string typePart = FormatFileType(f.Name);
                        string locPart = f.LocationName;
                        if (locPart.Length > 14) locPart = locPart.Substring(0, 12) + "..";

                        string rowText = namePart.PadRight(22) + sizePart.PadRight(12) + typePart.PadRight(16) + locPart.PadRight(16);
                        win.WriteSelectionRow(4 + r, rowText, isSelected, 1);
                    }
                }
            }

            win.WriteSeparator(12);

            // Bottom Selected File Box
            string selectedFileName = "";
            if (_openDialogFiles != null && _openDialogSelectedFileIndex >= 0 && _openDialogSelectedFileIndex < _openDialogFiles.Count)
            {
                selectedFileName = _openDialogFiles[_openDialogSelectedFileIndex].Name;
            }
            win.WriteRow(13, $"File name:     [ {selectedFileName.PadRight(47)} ]", ConsoleColor.Yellow, 2);
            win.WriteRow(14, "Files of type: [ All Files (*.*, *.txt, *.ini, *.log)          ]", ConsoleColor.Yellow, 2);

            if (!string.IsNullOrEmpty(_lastStatus))
            {
                win.WriteRow(15, "Status: " + _lastStatus, ConsoleColor.Cyan, 2);
            }

            // Status / instructions footer bar
            win.WriteFooter("[Up/Dn] Select  |  [Tab] Drive  |  [Enter] Open  |  [ESC] Cancel", ConsoleColor.Yellow);
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
