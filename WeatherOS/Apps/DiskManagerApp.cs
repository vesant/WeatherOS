using System;
using System.Collections.Generic;
using System.IO;
using Cosmos.System;
using Cosmos.System.FileSystem.VFS;
using WeatherOS.Graphics;
using WeatherOS.Services;

namespace WeatherOS.Apps
{
    public class DiskManagerApp : IApp
    {
        public string Id => "diskmgr";
        public string Name => "File & Storage Explorer";
        public bool IsRealTime => false;

        private string _currentDrive = "RAM:\\";
        private string _currentPath = "RAM:\\";
        private List<FileExplorerItem> _items = new List<FileExplorerItem>();
        private int _selectedIndex = 0;
        private int _scrollOffset = 0;
        private string _statusMessage = "";

        // Preview State
        private bool _isPreviewActive = false;
        private string _previewFileName = "";
        private string[] _previewLines = null;
        private int _previewScroll = 0;

        // Delete State
        private bool _isConfirmingDelete = false;

        // Create Directory State
        private bool _isCreatingDirectory = false;
        private string _newDirName = "";

        public DiskManagerApp()
        {
            InitDefaultDrive();
            Refresh();
        }

        private void InitDefaultDrive()
        {
            _currentDrive = "RAM:\\";
            _currentPath = "RAM:\\";
        }

        public void Refresh()
        {
            _items = StorageService.GetDirectoryEntries(_currentPath);
            _selectedIndex = 0;
            _scrollOffset = 0;
            _isPreviewActive = false;
            _isConfirmingDelete = false;
            _isCreatingDirectory = false;
            _newDirName = "";
        }

        private int _lastMemoryStoreCount = -1;

        public void Draw(TuiDesktop desktop)
        {
            var mem = StorageService.GetMemoryStore();
            int currentMemCount = mem != null ? mem.Count : 0;
            if (_lastMemoryStoreCount != currentMemCount)
            {
                _lastMemoryStoreCount = currentMemCount;
                _items = StorageService.GetDirectoryEntries(_currentPath);
            }

            var win = desktop.CreateWindow("WEATHER OS - FILE & STORAGE EXPLORER", WindowSize.FullScreen);
            ConsoleColor winBg = desktop.IsDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;

            // Top Status & Navigation Bar
            string topBar = $" Drive: {_currentDrive} | Path: {_currentPath} | Items: {_items.Count}";
            win.WriteRow(1, topBar, ConsoleColor.Yellow, 2);
            win.WriteSeparator(2);

            // Main Items Area (Lines 6 to 15 = 10 visible items)
            int maxVisible = 10;
            if (_selectedIndex < _scrollOffset) _scrollOffset = _selectedIndex;
            if (_selectedIndex >= _scrollOffset + maxVisible) _scrollOffset = _selectedIndex - maxVisible + 1;

            if (_items.Count == 0)
            {
                System.Console.SetCursorPosition(5, 8);
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.Write("  [Empty Directory] No files or folders found.");

                System.Console.SetCursorPosition(5, 10);
                System.Console.ForegroundColor = ConsoleColor.White;
                System.Console.Write("  Press [M] to create a folder, [P] to paste, or [Tab] to switch drive.");
            }
            else
            {
                for (int i = 0; i < maxVisible; i++)
                {
                    int itemIdx = _scrollOffset + i;
                    int lineY = 6 + i;
                    System.Console.SetCursorPosition(5, lineY);

                    if (itemIdx < _items.Count)
                    {
                        var it = _items[itemIdx];
                        bool isSel = (itemIdx == _selectedIndex);

                        if (isSel)
                        {
                            System.Console.BackgroundColor = ConsoleColor.White;
                            System.Console.ForegroundColor = ConsoleColor.Black;
                        }
                        else
                        {
                            System.Console.BackgroundColor = winBg;
                            if (it.IsDirectory) System.Console.ForegroundColor = ConsoleColor.Yellow;
                            else if (it.Tag == "[INI]") System.Console.ForegroundColor = ConsoleColor.Green;
                            else if (it.Tag == "[TXT]") System.Console.ForegroundColor = ConsoleColor.Cyan;
                            else System.Console.ForegroundColor = ConsoleColor.White;
                        }

                        string sizeStr = it.IsDirectory ? "<DIR>" : FormatSize(it.Size);
                        string nameDisplay = it.Name ?? "";
                        if (string.IsNullOrEmpty(nameDisplay)) nameDisplay = "(unnamed)";
                        if (nameDisplay.Length > 42) nameDisplay = nameDisplay.Substring(0, 39) + "...";

                        string leftPart = " " + it.Tag + " " + nameDisplay;
                        string row = leftPart.PadRight(56) + sizeStr.PadLeft(12) + " ";
                        if (row.Length > 70) row = row.Substring(0, 70);
                        System.Console.Write(row.PadRight(70));
                    }
                    else
                    {
                        System.Console.BackgroundColor = winBg;
                        System.Console.Write(new string(' ', 70));
                    }
                }
            }

            // Divider before footer
            System.Console.BackgroundColor = winBg;
            System.Console.SetCursorPosition(5, 17);
            System.Console.ForegroundColor = ConsoleColor.DarkCyan;
            System.Console.Write("----------------------------------------------------------------------");

            // Status or prompt message line
            System.Console.SetCursorPosition(5, 18);
            if (_isCreatingDirectory)
            {
                System.Console.BackgroundColor = ConsoleColor.DarkBlue;
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                string prompt = " CREATE FOLDER: [" + _newDirName + "_]";
                if (prompt.Length > 70) prompt = prompt.Substring(0, 70);
                System.Console.Write(prompt.PadRight(70));
            }
            else if (_isConfirmingDelete && _selectedIndex >= 0 && _selectedIndex < _items.Count)
            {
                System.Console.BackgroundColor = ConsoleColor.Red;
                System.Console.ForegroundColor = ConsoleColor.White;
                var sel = _items[_selectedIndex];
                string delPrompt = $" DELETE '{sel.Name}'? Press [Y] to Confirm or [N] to Cancel ";
                if (delPrompt.Length > 70) delPrompt = delPrompt.Substring(0, 70);
                System.Console.Write(delPrompt.PadRight(70));
            }
            else if (!string.IsNullOrEmpty(_statusMessage))
            {
                System.Console.BackgroundColor = winBg;
                System.Console.ForegroundColor = ConsoleColor.Green;
                string st = _statusMessage.Length > 70 ? _statusMessage.Substring(0, 70) : _statusMessage;
                System.Console.Write(st.PadRight(70));
            }
            else
            {
                string clipInfo = StorageClipboard.HasData ? $"Clipboard: {StorageClipboard.CopiedName}" : "Clipboard: empty";
                win.WriteRow(17, $"[M] New Folder  |  [C] Copy  |  [P] Paste  |  [Tab] Drive  |  {clipInfo}", ConsoleColor.Gray, 2);
            }

            // Controls Footer
            if (_isCreatingDirectory)
            {
                win.WriteFooter("[Enter] Confirm Creation  |  [ESC] Cancel", ConsoleColor.Yellow);
            }
            else
            {
                win.WriteFooter("[Enter] Open/Browse  |  [O/E] Open in Notepad  |  [V] Preview  |  [D] Delete  |  [ESC] Exit", ConsoleColor.Yellow);
            }

            // Modal Preview Window overlay if active
            if (_isPreviewActive)
            {
                DrawPreviewModal(desktop);
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024} KB";
            return $"{bytes / (1024 * 1024)} MB";
        }

        private void DrawPreviewModal(TuiDesktop desktop)
        {
            var pWin = desktop.CreateWindow($"PREVIEW: {_previewFileName}", WindowSize.Medium);

            if (_previewLines == null || _previewLines.Length == 0)
            {
                pWin.WriteRow(3, "[Empty File or Unreadable Content]", ConsoleColor.Yellow, 2);
            }
            else
            {
                for (int l = 0; l < 9; l++)
                {
                    int lineIdx = _previewScroll + l;

                    if (lineIdx < _previewLines.Length)
                    {
                        pWin.WriteRow(1 + l, _previewLines[lineIdx], ConsoleColor.White, 2);
                    }
                }
            }

            pWin.WriteFooter("[Up/Dn] Scroll  |  [ESC/Enter] Close Preview", ConsoleColor.Yellow);
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            // If in file preview modal
            if (_isPreviewActive)
            {
                if (keyEvent.Key == ConsoleKeyEx.Escape || keyEvent.Key == ConsoleKeyEx.Enter)
                {
                    _isPreviewActive = false;
                }
                else if (keyEvent.Key == ConsoleKeyEx.DownArrow)
                {
                    if (_previewLines != null && _previewScroll + 10 < _previewLines.Length)
                    {
                        _previewScroll++;
                    }
                }
                else if (keyEvent.Key == ConsoleKeyEx.UpArrow)
                {
                    if (_previewScroll > 0) _previewScroll--;
                }
                return;
            }

            // If creating directory
            if (_isCreatingDirectory)
            {
                if (keyEvent.Key == ConsoleKeyEx.Escape)
                {
                    _isCreatingDirectory = false;
                    _newDirName = "";
                    _statusMessage = "Folder creation cancelled.";
                }
                else if (keyEvent.Key == ConsoleKeyEx.Enter)
                {
                    string status;
                    bool ok = StorageService.CreateDirectory(_currentPath, _newDirName, out status);
                    _statusMessage = status;
                    _isCreatingDirectory = false;
                    _newDirName = "";
                    Refresh();
                }
                else if (keyEvent.Key == ConsoleKeyEx.Backspace)
                {
                    if (_newDirName.Length > 0)
                    {
                        _newDirName = _newDirName.Substring(0, _newDirName.Length - 1);
                    }
                }
                else if (keyEvent.KeyChar >= 32 && keyEvent.KeyChar <= 126)
                {
                    char c = keyEvent.KeyChar;
                    if (c != '/' && c != '\\' && c != ':' && c != '*' && c != '?' && c != '"' && c != '<' && c != '>' && c != '|')
                    {
                        if (_newDirName.Length < 32)
                        {
                            _newDirName += c;
                        }
                    }
                }
                return;
            }

            // If in delete confirmation modal
            if (_isConfirmingDelete)
            {
                if (keyEvent.Key == ConsoleKeyEx.Y)
                {
                    if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
                    {
                        var sel = _items[_selectedIndex];
                        if (sel.Name == "..")
                        {
                            _statusMessage = "Cannot delete parent directory reference.";
                        }
                        else
                        {
                            string stat;
                            bool ok = StorageService.DeletePath(sel.FullPath, sel.IsDirectory, out stat);
                            _statusMessage = stat;
                            Refresh();
                        }
                    }
                    _isConfirmingDelete = false;
                }
                else if (keyEvent.Key == ConsoleKeyEx.N || keyEvent.Key == ConsoleKeyEx.Escape)
                {
                    _isConfirmingDelete = false;
                    _statusMessage = "Delete cancelled.";
                }
                return;
            }

            // Main navigation
            if (keyEvent.Key == ConsoleKeyEx.UpArrow)
            {
                if (_items.Count > 0)
                {
                    _selectedIndex--;
                    if (_selectedIndex < 0) _selectedIndex = _items.Count - 1;
                }
            }
            else if (keyEvent.Key == ConsoleKeyEx.DownArrow)
            {
                if (_items.Count > 0)
                {
                    _selectedIndex++;
                    if (_selectedIndex >= _items.Count) _selectedIndex = 0;
                }
            }
            else if (keyEvent.Key == ConsoleKeyEx.Enter)
            {
                if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
                {
                    var sel = _items[_selectedIndex];
                    if (sel.IsDirectory)
                    {
                        _currentPath = sel.FullPath;
                        if (!_currentPath.EndsWith("\\")) _currentPath += "\\";
                        _currentDrive = StorageService.GetDrivePrefix(_currentPath);
                        Refresh();
                    }
                    else
                    {
                        OpenFileInNotepad(desktop, sel);
                    }
                }
            }
            else if (keyEvent.KeyChar == 'v' || keyEvent.KeyChar == 'V' || keyEvent.Key == ConsoleKeyEx.Spacebar)
            {
                if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
                {
                    var sel = _items[_selectedIndex];
                    if (!sel.IsDirectory)
                    {
                        OpenFilePreview(sel);
                    }
                }
            }
            else if (keyEvent.KeyChar == 'm' || keyEvent.KeyChar == 'M')
            {
                _isCreatingDirectory = true;
                _newDirName = "";
                _statusMessage = "";
            }
            else if (keyEvent.KeyChar == 'c' || keyEvent.KeyChar == 'C')
            {
                if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
                {
                    var sel = _items[_selectedIndex];
                    if (sel.Name == "..")
                    {
                        _statusMessage = "Cannot copy parent directory reference.";
                    }
                    else
                    {
                        StorageClipboard.Copy(sel.FullPath, sel.Name, sel.IsDirectory);
                        _statusMessage = $"Copied '{sel.Name}' to clipboard.";
                    }
                }
            }
            else if (keyEvent.KeyChar == 'p' || keyEvent.KeyChar == 'P')
            {
                string status;
                bool ok = StorageService.PasteFile(_currentPath, out status);
                _statusMessage = status;
                if (ok)
                {
                    Refresh();
                }
            }
            else if (keyEvent.KeyChar == 'd' || keyEvent.KeyChar == 'D' || keyEvent.Key == ConsoleKeyEx.Delete)
            {
                if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
                {
                    var sel = _items[_selectedIndex];
                    if (sel.Name != "..")
                    {
                        _isConfirmingDelete = true;
                    }
                }
            }
            else if (keyEvent.KeyChar == 'o' || keyEvent.KeyChar == 'O' || keyEvent.KeyChar == 'e' || keyEvent.KeyChar == 'E')
            {
                if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
                {
                    var sel = _items[_selectedIndex];
                    if (!sel.IsDirectory)
                    {
                        OpenFileInNotepad(desktop, sel);
                    }
                }
            }
            else if (keyEvent.KeyChar == 'n' || keyEvent.KeyChar == 'N')
            {
                desktop.SetCurrentApp("notepad");
            }
            else if (keyEvent.Key == ConsoleKeyEx.Tab)
            {
                // Switch Drive
                ToggleDrive();
                Refresh();
            }
            else if (keyEvent.Key == ConsoleKeyEx.F2)
            {
                desktop.SetCurrentApp("diskdiag");
            }
            else if (keyEvent.KeyChar == 'r' || keyEvent.KeyChar == 'R')
            {
                Refresh();
                _statusMessage = "Directory reloaded.";
            }
            else if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                desktop.SetCurrentApp("launcher");
            }
        }

        private void OpenFileInNotepad(TuiDesktop desktop, FileExplorerItem sel)
        {
            string content = StorageService.ReadFileContent(sel.FullPath);
            var np = desktop.GetApp("notepad") as NotepadApp;
            if (np != null)
            {
                np.OpenDirect(sel.Name, content);
                desktop.SetCurrentApp("notepad");
            }
        }

        private void ToggleDrive()
        {
            var driveList = new List<string>();
            driveList.Add("RAM:\\");

            try
            {
                var vfsDrives = VFSManager.GetLogicalDrives();
                if (vfsDrives != null)
                {
                    foreach (var d in vfsDrives)
                    {
                        string nd = d.EndsWith("\\") ? d : d + "\\";
                        if (!driveList.Contains(nd)) driveList.Add(nd);
                    }
                }
            }
            catch { }

            if (!driveList.Contains("0:\\")) driveList.Add("0:\\");

            int currentIdx = driveList.FindIndex(d => string.Equals(d, _currentDrive, StringComparison.OrdinalIgnoreCase));
            int nextIdx = (currentIdx + 1) % driveList.Count;
            _currentDrive = driveList[nextIdx];
            _currentPath = _currentDrive;
            _statusMessage = $"Switched to Drive {_currentDrive}";
        }

        private void OpenFilePreview(FileExplorerItem item)
        {
            _previewFileName = item.Name;
            _previewScroll = 0;
            string content = StorageService.ReadFileContent(item.FullPath);
            if (string.IsNullOrEmpty(content))
            {
                _previewLines = new string[] { "[Empty or Unreadable Content]" };
            }
            else
            {
                _previewLines = content.Split('\n');
            }
            _isPreviewActive = true;
        }
    }
}
