using System;
using System.Collections.Generic;
using System.IO;
using Cosmos.System;
using Cosmos.System.FileSystem.VFS;
using WeatherOS.Graphics;
using WeatherOS.Services;

namespace WeatherOS.Apps
{
    public class FileExplorerItem
    {
        public string Name;
        public bool IsDirectory;
        public long Size;
        public string FullPath;
        public string Tag;
    }

    public class DiskManagerApp : IApp
    {
        public string Id => "diskmgr";
        public string Name => "File & Storage Explorer";
        public bool IsRealTime => false;

        private string _currentDrive = "0:\\";
        private string _currentPath = "0:\\";
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

        public DiskManagerApp()
        {
            InitDefaultDrive();
            Refresh();
        }

        private void InitDefaultDrive()
        {
            try
            {
                var drives = VFSManager.GetLogicalDrives();
                if (drives != null && drives.Count > 0)
                {
                    _currentDrive = drives[0];
                    if (!_currentDrive.EndsWith("\\")) _currentDrive += "\\";
                    _currentPath = _currentDrive;
                }
                else
                {
                    _currentDrive = "0:\\";
                    _currentPath = "0:\\";
                }
            }
            catch
            {
                _currentDrive = "0:\\";
                _currentPath = "0:\\";
            }
        }

        public void Refresh()
        {
            _items.Clear();
            _selectedIndex = 0;
            _scrollOffset = 0;
            _isPreviewActive = false;
            _isConfirmingDelete = false;

            // 1. Parent folder option if not at drive root
            if (!string.Equals(_currentPath, _currentDrive, StringComparison.OrdinalIgnoreCase) && 
                _currentPath.Length > _currentDrive.Length)
            {
                string parent = GetParentDirectory(_currentPath);
                _items.Add(new FileExplorerItem
                {
                    Name = "..",
                    IsDirectory = true,
                    Size = 0,
                    FullPath = parent,
                    Tag = "[UP]"
                });
            }

            // 2. If in RAM Virtual Drive
            if (string.Equals(_currentDrive, "RAM:\\", StringComparison.OrdinalIgnoreCase))
            {
                var mem = StorageService.GetMemoryStore();
                if (mem != null)
                {
                    foreach (var kvp in mem)
                    {
                        _items.Add(new FileExplorerItem
                        {
                            Name = kvp.Key,
                            IsDirectory = false,
                            Size = kvp.Value != null ? kvp.Value.Length : 0,
                            FullPath = "RAM:\\" + kvp.Key,
                            Tag = GetTagForFile(kvp.Key)
                        });
                    }
                }
                return;
            }

            // 3. Scan physical disk VFS
            bool vfsSuccess = false;
            try
            {
                if (Directory.Exists(_currentPath))
                {
                    // Directories
                    string[] dirs = Directory.GetDirectories(_currentPath);
                    if (dirs != null)
                    {
                        for (int i = 0; i < dirs.Length; i++)
                        {
                            string d = dirs[i];
                            string dirName = Path.GetFileName(d);
                            if (string.IsNullOrEmpty(dirName)) dirName = d;
                            _items.Add(new FileExplorerItem
                            {
                                Name = dirName,
                                IsDirectory = true,
                                Size = 0,
                                FullPath = d,
                                Tag = "[DIR]"
                            });
                        }
                    }

                    // Files
                    string[] files = Directory.GetFiles(_currentPath);
                    if (files != null)
                    {
                        for (int i = 0; i < files.Length; i++)
                        {
                            string f = files[i];
                            string fileName = Path.GetFileName(f);
                            if (string.IsNullOrEmpty(fileName)) fileName = f;
                            long sz = 0;
                            try { sz = new FileInfo(f).Length; } catch { }
                            _items.Add(new FileExplorerItem
                            {
                                Name = fileName,
                                IsDirectory = false,
                                Size = sz,
                                FullPath = f,
                                Tag = GetTagForFile(fileName)
                            });
                        }
                    }
                    vfsSuccess = true;
                }
            }
            catch { }

            // 4. Fallback or merge with RAM store files if at root so settings.ini is visible
            if (!vfsSuccess || string.Equals(_currentPath, _currentDrive, StringComparison.OrdinalIgnoreCase))
            {
                var mem = StorageService.GetMemoryStore();
                if (mem != null)
                {
                    foreach (var kvp in mem)
                    {
                        bool exists = false;
                        for (int i = 0; i < _items.Count; i++)
                        {
                            if (string.Equals(_items[i].Name, kvp.Key, StringComparison.OrdinalIgnoreCase))
                            {
                                exists = true;
                                break;
                            }
                        }
                        if (!exists)
                        {
                            _items.Add(new FileExplorerItem
                            {
                                Name = kvp.Key,
                                IsDirectory = false,
                                Size = kvp.Value != null ? kvp.Value.Length : 0,
                                FullPath = _currentDrive + kvp.Key,
                                Tag = GetTagForFile(kvp.Key)
                            });
                        }
                    }
                }
            }
        }

        private static string GetTagForFile(string name)
        {
            if (string.IsNullOrEmpty(name)) return "[FILE]";
            string n = name.ToUpper();
            if (n.EndsWith(".TXT")) return "[TXT]";
            if (n.EndsWith(".INI")) return "[INI]";
            if (n.EndsWith(".DAT")) return "[DAT]";
            if (n.EndsWith(".BIN")) return "[BIN]";
            if (n.EndsWith(".CFG")) return "[CFG]";
            if (n.EndsWith(".LOG")) return "[LOG]";
            return "[FILE]";
        }

        private static string GetParentDirectory(string path)
        {
            if (string.IsNullOrEmpty(path)) return "0:\\";
            int idx = path.LastIndexOf('\\');
            if (idx <= 2) return path.Substring(0, Math.Min(path.Length, 3));
            return path.Substring(0, idx);
        }

        public void Draw(TuiDesktop desktop)
        {
            desktop.DrawWindow(3, 2, 74, 21, "WEATHER OS - FILE & STORAGE EXPLORER");
            ConsoleColor winBg = desktop.IsDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;

            // Top Status & Navigation Bar
            System.Console.SetCursorPosition(5, 4);
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            string topBar = $" Drive: {_currentDrive} | Path: {_currentPath} | Items: {_items.Count}";
            if (topBar.Length > 70) topBar = topBar.Substring(0, 67) + "...";
            System.Console.Write(topBar.PadRight(70));

            System.Console.SetCursorPosition(5, 5);
            System.Console.ForegroundColor = ConsoleColor.DarkCyan;
            System.Console.Write("----------------------------------------------------------------------");

            // Main Items Area (Lines 6 to 16 = 11 visible items)
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
                System.Console.Write("  Press [N] to create a new file in Notepad, or [Tab] to switch drive.");
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
                        string nameDisplay = it.Name;
                        if (nameDisplay.Length > 44) nameDisplay = nameDisplay.Substring(0, 41) + "...";

                        string row = string.Format(" {0} {1,-44} {2,12} ", it.Tag, nameDisplay, sizeStr);
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
            if (_isConfirmingDelete && _selectedIndex >= 0 && _selectedIndex < _items.Count)
            {
                System.Console.BackgroundColor = ConsoleColor.Red;
                System.Console.ForegroundColor = ConsoleColor.White;
                var sel = _items[_selectedIndex];
                string delPrompt = $" DELETE '{sel.Name}'? Press [Y] to Confirm Delete or [N] to Cancel ";
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
                System.Console.BackgroundColor = winBg;
                System.Console.ForegroundColor = ConsoleColor.Gray;
                System.Console.Write(" [F2] Partition Diag  |  [Tab] Switch Drive  |  [N] Notepad  |  [R] Reload ".PadRight(70));
            }

            // Controls Footer
            System.Console.BackgroundColor = winBg;
            System.Console.SetCursorPosition(5, 20);
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.Write("[Enter] Open/View  |  [V] Preview  |  [D] Delete  |  [ESC] Exit");

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
            int pLeft = 8;
            int pTop = 4;
            int pWidth = 64;
            int pHeight = 16;

            desktop.DrawWindow(pLeft, pTop, pWidth, pHeight, $"PREVIEW: {_previewFileName}");
            ConsoleColor bg = ConsoleColor.DarkBlue;
            System.Console.BackgroundColor = bg;
            System.Console.ForegroundColor = ConsoleColor.White;

            if (_previewLines == null || _previewLines.Length == 0)
            {
                System.Console.SetCursorPosition(pLeft + 3, pTop + 4);
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.Write("[Empty File or Unreadable Content]");
            }
            else
            {
                for (int l = 0; l < 11; l++)
                {
                    int lineIdx = _previewScroll + l;
                    System.Console.SetCursorPosition(pLeft + 3, pTop + 2 + l);

                    if (lineIdx < _previewLines.Length)
                    {
                        string ln = _previewLines[lineIdx];
                        if (ln.Length > pWidth - 6) ln = ln.Substring(0, pWidth - 9) + "...";
                        System.Console.Write(ln.PadRight(pWidth - 6));
                    }
                    else
                    {
                        System.Console.Write(new string(' ', pWidth - 6));
                    }
                }
            }

            System.Console.SetCursorPosition(pLeft + 3, pTop + 14);
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.Write("[Up/Down] Scroll  |  [ESC / Enter] Close Preview");
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

            // If in delete confirmation modal
            if (_isConfirmingDelete)
            {
                if (keyEvent.Key == ConsoleKeyEx.Y)
                {
                    if (_selectedIndex >= 0 && _selectedIndex < _items.Count)
                    {
                        var sel = _items[_selectedIndex];
                        string stat;
                        bool ok = StorageService.DeleteFile(sel.Name, out stat);
                        try
                        {
                            if (File.Exists(sel.FullPath)) File.Delete(sel.FullPath);
                        }
                        catch { }
                        _statusMessage = ok ? $"Deleted '{sel.Name}' [OK]" : $"Could not delete '{sel.Name}'";
                        Refresh();
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
                        Refresh();
                    }
                    else
                    {
                        OpenFilePreview(sel);
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
            else if (keyEvent.KeyChar == 'd' || keyEvent.KeyChar == 'D' || keyEvent.Key == ConsoleKeyEx.Delete)
            {
                if (_selectedIndex >= 0 && _selectedIndex < _items.Count && !_items[_selectedIndex].IsDirectory)
                {
                    _isConfirmingDelete = true;
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

        private void ToggleDrive()
        {
            if (string.Equals(_currentDrive, "0:\\", StringComparison.OrdinalIgnoreCase))
            {
                _currentDrive = "RAM:\\";
                _currentPath = "RAM:\\";
                _statusMessage = "Switched to RAM Virtual Store.";
            }
            else
            {
                _currentDrive = "0:\\";
                _currentPath = "0:\\";
                _statusMessage = "Switched to Drive 0:\\.";
            }
        }

        private void OpenFilePreview(FileExplorerItem item)
        {
            _previewFileName = item.Name;
            _previewScroll = 0;
            string content;
            string status;

            if (StorageService.LoadFile(item.Name, out content, out status))
            {
                _previewLines = content.Split('\n');
            }
            else
            {
                try
                {
                    if (File.Exists(item.FullPath))
                    {
                        _previewLines = File.ReadAllLines(item.FullPath);
                    }
                    else
                    {
                        _previewLines = new string[] { "[Empty or Unreadable File]" };
                    }
                }
                catch
                {
                    _previewLines = new string[] { "[Error reading file]" };
                }
            }

            _isPreviewActive = true;
        }
    }
}
