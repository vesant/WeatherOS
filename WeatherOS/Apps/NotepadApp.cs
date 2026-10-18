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

        // Unified File Explorer & Open File Dialog State
        private string _openDialogPath = "RAM:\\";
        private int _openDialogSelectedItemIndex = 0;
        private int _openDialogScrollOffset = 0;
        private List<FileExplorerItem> _openDialogItems = new List<FileExplorerItem>();
        private int _openPreviousState = 0; // 0=Menu, 1=Editor
        private bool _isOpenDialogCreatingDir = false;
        private string _openDialogNewDirName = "";
        private bool _isOpenDialogConfirmingDelete = false;

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
            else if (_notepadState == 2) // Unified File Explorer & Open Dialog
            {
                // 1. If creating directory
                if (_isOpenDialogCreatingDir)
                {
                    if (keyEvent.Key == ConsoleKeyEx.Escape)
                    {
                        _isOpenDialogCreatingDir = false;
                        _openDialogNewDirName = "";
                        _lastStatus = "Folder creation cancelled.";
                    }
                    else if (keyEvent.Key == ConsoleKeyEx.Enter)
                    {
                        string status;
                        bool ok = StorageService.CreateDirectory(_openDialogPath, _openDialogNewDirName, out status);
                        _lastStatus = status;
                        _isOpenDialogCreatingDir = false;
                        _openDialogNewDirName = "";
                        RefreshOpenDialogItems();
                    }
                    else if (keyEvent.Key == ConsoleKeyEx.Backspace)
                    {
                        if (_openDialogNewDirName.Length > 0)
                        {
                            _openDialogNewDirName = _openDialogNewDirName.Substring(0, _openDialogNewDirName.Length - 1);
                        }
                    }
                    else if (keyEvent.KeyChar >= 32 && keyEvent.KeyChar <= 126)
                    {
                        char c = keyEvent.KeyChar;
                        if (c != '/' && c != '\\' && c != ':' && c != '*' && c != '?' && c != '"' && c != '<' && c != '>' && c != '|')
                        {
                            if (_openDialogNewDirName.Length < 32)
                            {
                                _openDialogNewDirName += c;
                            }
                        }
                    }
                    return;
                }

                // 2. If confirming delete
                if (_isOpenDialogConfirmingDelete)
                {
                    if (keyEvent.Key == ConsoleKeyEx.Y)
                    {
                        if (_openDialogSelectedItemIndex >= 0 && _openDialogSelectedItemIndex < _openDialogItems.Count)
                        {
                            var sel = _openDialogItems[_openDialogSelectedItemIndex];
                            if (sel.Name == "..")
                            {
                                _lastStatus = "Cannot delete parent directory reference.";
                            }
                            else
                            {
                                string stat;
                                bool ok = StorageService.DeletePath(sel.FullPath, sel.IsDirectory, out stat);
                                _lastStatus = stat;
                                RefreshOpenDialogItems();
                            }
                        }
                        _isOpenDialogConfirmingDelete = false;
                    }
                    else if (keyEvent.Key == ConsoleKeyEx.N || keyEvent.Key == ConsoleKeyEx.Escape)
                    {
                        _isOpenDialogConfirmingDelete = false;
                        _lastStatus = "Delete cancelled.";
                    }
                    return;
                }

                // 3. Normal navigation & actions
                if (keyEvent.Key == ConsoleKeyEx.Escape)
                {
                    _notepadState = _openPreviousState;
                }
                else if (keyEvent.Key == ConsoleKeyEx.UpArrow)
                {
                    if (_openDialogItems != null && _openDialogItems.Count > 0)
                    {
                        if (_openDialogSelectedItemIndex > 0)
                        {
                            _openDialogSelectedItemIndex--;
                            if (_openDialogSelectedItemIndex < _openDialogScrollOffset)
                            {
                                _openDialogScrollOffset = _openDialogSelectedItemIndex;
                            }
                        }
                    }
                }
                else if (keyEvent.Key == ConsoleKeyEx.DownArrow)
                {
                    if (_openDialogItems != null && _openDialogItems.Count > 0)
                    {
                        if (_openDialogSelectedItemIndex < _openDialogItems.Count - 1)
                        {
                            _openDialogSelectedItemIndex++;
                            if (_openDialogSelectedItemIndex >= _openDialogScrollOffset + 8)
                            {
                                _openDialogScrollOffset = _openDialogSelectedItemIndex - 7;
                            }
                        }
                    }
                }
                else if (keyEvent.Key == ConsoleKeyEx.Tab)
                {
                    // Toggle Drive
                    string curDrive = StorageService.GetDrivePrefix(_openDialogPath);
                    if (string.Equals(curDrive, "RAM:\\", StringComparison.OrdinalIgnoreCase))
                    {
                        _openDialogPath = "0:\\";
                    }
                    else
                    {
                        _openDialogPath = "RAM:\\";
                    }
                    RefreshOpenDialogItems();
                    _lastStatus = $"Switched to {_openDialogPath}";
                }
                else if (keyEvent.KeyChar == 'm' || keyEvent.KeyChar == 'M')
                {
                    _isOpenDialogCreatingDir = true;
                    _openDialogNewDirName = "";
                    _lastStatus = "";
                }
                else if (keyEvent.KeyChar == 'c' || keyEvent.KeyChar == 'C')
                {
                    if (_openDialogSelectedItemIndex >= 0 && _openDialogSelectedItemIndex < _openDialogItems.Count)
                    {
                        var sel = _openDialogItems[_openDialogSelectedItemIndex];
                        if (sel.Name == "..")
                        {
                            _lastStatus = "Cannot copy parent directory reference.";
                        }
                        else
                        {
                            StorageClipboard.Copy(sel.FullPath, sel.Name, sel.IsDirectory);
                            _lastStatus = $"Copied '{sel.Name}' to clipboard.";
                        }
                    }
                }
                else if (keyEvent.KeyChar == 'p' || keyEvent.KeyChar == 'P')
                {
                    string status;
                    bool ok = StorageService.PasteFile(_openDialogPath, out status);
                    _lastStatus = status;
                    if (ok)
                    {
                        RefreshOpenDialogItems();
                    }
                }
                else if (keyEvent.KeyChar == 'd' || keyEvent.KeyChar == 'D' || keyEvent.Key == ConsoleKeyEx.Delete)
                {
                    if (_openDialogSelectedItemIndex >= 0 && _openDialogSelectedItemIndex < _openDialogItems.Count)
                    {
                        var sel = _openDialogItems[_openDialogSelectedItemIndex];
                        if (sel.Name != "..")
                        {
                            _isOpenDialogConfirmingDelete = true;
                        }
                    }
                }
                else if (keyEvent.Key == ConsoleKeyEx.Enter)
                {
                    if (_openDialogItems != null && _openDialogItems.Count > 0 &&
                        _openDialogSelectedItemIndex >= 0 && _openDialogSelectedItemIndex < _openDialogItems.Count)
                    {
                        var picked = _openDialogItems[_openDialogSelectedItemIndex];
                        if (picked.IsDirectory)
                        {
                            _openDialogPath = picked.FullPath;
                            if (!_openDialogPath.EndsWith("\\")) _openDialogPath += "\\";
                            RefreshOpenDialogItems();
                        }
                        else
                        {
                            string content = StorageService.ReadFileContent(picked.FullPath);
                            _notepadFilename = picked.Name;
                            _editorState.Text = content ?? "";
                            _editorState.CursorPos = _editorState.Text.Length;
                            _editorState.ClearSelection();
                            _notepadState = 1; // Opens directly in editor!
                            _lastStatus = $"Opened {_notepadFilename} [OK]";
                        }
                    }
                    else
                    {
                        _lastStatus = "No file available to open in this directory.";
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
            if (string.IsNullOrEmpty(_openDialogPath))
            {
                _openDialogPath = "RAM:\\";
            }
            _isOpenDialogCreatingDir = false;
            _isOpenDialogConfirmingDelete = false;
            _openDialogNewDirName = "";
            RefreshOpenDialogItems();
            _notepadState = 2;
        }

        private void RefreshOpenDialogItems()
        {
            _openDialogItems = StorageService.GetDirectoryEntries(_openDialogPath);
            _openDialogSelectedItemIndex = 0;
            _openDialogScrollOffset = 0;

            if (_openDialogItems != null && !string.IsNullOrEmpty(_notepadFilename))
            {
                for (int i = 0; i < _openDialogItems.Count; i++)
                {
                    if (string.Equals(_openDialogItems[i].Name, _notepadFilename, StringComparison.OrdinalIgnoreCase))
                    {
                        _openDialogSelectedItemIndex = i;
                        if (_openDialogSelectedItemIndex >= 8) _openDialogScrollOffset = _openDialogSelectedItemIndex - 7;
                        break;
                    }
                }
            }
        }

        private void DrawOpenFileDialog(TuiDesktop desktop)
        {
            var win = desktop.CreateWindow("OPEN FILE  |  STORAGE & FILE EXPLORER", WindowSize.FullScreen);

            // Header / Look In:
            string driveBox = $"[ < {_openDialogPath} > ]";
            if (driveBox.Length > 44) driveBox = driveBox.Substring(0, 41) + "... > ]";

            win.WriteRow(1, "Path: " + driveBox + "  [Tab: Switch Drive]", ConsoleColor.Yellow, 2);

            // File table header
            string th = "  Name                    Size        Type            Location        ";
            win.WriteRow(2, th, ConsoleColor.Yellow, 2);
            win.WriteSeparator(3);

            // File list (8 visible rows: relY = 4 to 11)
            int visibleRows = 8;
            if (_openDialogItems == null || _openDialogItems.Count == 0)
            {
                win.WriteRow(5, "(No files or folders found. Press [M] to create folder, [Tab] for Drive)", ConsoleColor.Yellow, 2);
            }
            else
            {
                for (int r = 0; r < visibleRows; r++)
                {
                    int itemIdx = _openDialogScrollOffset + r;

                    if (itemIdx < _openDialogItems.Count)
                    {
                        var it = _openDialogItems[itemIdx];
                        bool isSelected = (itemIdx == _openDialogSelectedItemIndex);

                        string namePart = it.Name ?? "";
                        if (namePart.Length > 20) namePart = namePart.Substring(0, 17) + "...";

                        string sizePart = it.IsDirectory ? "<DIR>" : FormatFileSize(it.Size);
                        string typePart = it.IsDirectory ? "Folder" : FormatFileType(it.Name);
                        string locPart = it.FullPath ?? "";
                        if (locPart.Length > 16) locPart = locPart.Substring(0, 14) + "..";

                        string rowText = namePart.PadRight(22) + sizePart.PadRight(12) + typePart.PadRight(16) + locPart.PadRight(16);
                        win.WriteSelectionRow(4 + r, rowText, isSelected, 1);
                    }
                }
            }

            win.WriteSeparator(12);

            // Bottom Selected File Box or Modal Prompts
            if (_isOpenDialogCreatingDir)
            {
                win.WriteRow(13, $"CREATE FOLDER: [ {_openDialogNewDirName}_ ]", ConsoleColor.Green, 2);
                win.WriteRow(14, "Type new folder name and press [Enter] to create, or [ESC] to cancel.", ConsoleColor.White, 2);
            }
            else if (_isOpenDialogConfirmingDelete && _openDialogSelectedItemIndex >= 0 && _openDialogSelectedItemIndex < _openDialogItems.Count)
            {
                var sel = _openDialogItems[_openDialogSelectedItemIndex];
                win.WriteRow(13, $"CONFIRM DELETE: '{sel.Name}'?", ConsoleColor.Red, 2);
                win.WriteRow(14, "Press [Y] to permanently delete or [N] to cancel.", ConsoleColor.White, 2);
            }
            else
            {
                string selectedFileName = "";
                if (_openDialogItems != null && _openDialogSelectedItemIndex >= 0 && _openDialogSelectedItemIndex < _openDialogItems.Count)
                {
                    selectedFileName = _openDialogItems[_openDialogSelectedItemIndex].Name;
                }
                win.WriteRow(13, $"File name:     [ {selectedFileName.PadRight(47)} ]", ConsoleColor.Yellow, 2);
                string clipInfo = StorageClipboard.HasData ? $"Clipboard: {StorageClipboard.CopiedName}" : "Clipboard: empty";
                win.WriteRow(14, $"Tools: [M] New Folder | [C] Copy | [P] Paste | [D] Delete | {clipInfo}", ConsoleColor.Cyan, 2);
            }

            if (!string.IsNullOrEmpty(_lastStatus))
            {
                win.WriteRow(15, "Status: " + _lastStatus, ConsoleColor.Cyan, 2);
            }

            // Status / instructions footer bar
            if (_isOpenDialogCreatingDir)
            {
                win.WriteFooter("[Enter] Confirm Creation  |  [ESC] Cancel", ConsoleColor.Yellow);
            }
            else if (_isOpenDialogConfirmingDelete)
            {
                win.WriteFooter("[Y] Confirm Delete  |  [N/ESC] Cancel", ConsoleColor.Yellow);
            }
            else
            {
                win.WriteFooter("[Enter] Open/Browse  |  [M] New Folder  |  [C] Copy  |  [P] Paste  |  [D] Del  |  [ESC] Cancel", ConsoleColor.Yellow);
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
