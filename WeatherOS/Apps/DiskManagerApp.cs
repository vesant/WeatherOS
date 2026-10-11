using System;
using System.Collections.Generic;
using Cosmos.System;
using WeatherOS.Graphics;
using WeatherOS.Services;

namespace WeatherOS.Apps
{
    public class DiskManagerApp : IApp
    {
        public string Id => "diskmgr";
        public string Name => "Disk & Storage Explorer";
        public bool IsRealTime => false;

        private List<PartitionInfo> _partitions = new List<PartitionInfo>();
        private int _selectedIndex = 0;
        private string _statusMessage = "";
        private bool _confirmingFormat = false;
        private bool _confirmingWipe = false;

        public DiskManagerApp()
        {
            Refresh();
        }

        private void Refresh()
        {
            _partitions = FileSystemProbeService.ProbeAllPartitions();
            _selectedIndex = 0;
            _confirmingFormat = false;
            _confirmingWipe = false;
        }

        public void Draw(TuiDesktop desktop)
        {
            desktop.DrawWindow(4, 3, 72, 18, "STORAGE & PARTITION EXPLORER (EXT4 / FAT32 / NTFS)");
            ConsoleColor winBg = desktop.IsDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;
            System.Console.ForegroundColor = ConsoleColor.White;

            System.Console.SetCursorPosition(6, 5);
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.Write("DETECTED HARDWARE DISKS & REAL FILESYSTEMS:");

            System.Console.SetCursorPosition(6, 6);
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.Write("--------------------------------------------------------------------");

            if (_partitions == null || _partitions.Count == 0)
            {
                System.Console.SetCursorPosition(6, 8);
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.Write("(Scanning hardware block devices...)");
            }
            else
            {
                for (int i = 0; i < _partitions.Count && i < 6; i++)
                {
                    var p = _partitions[i];
                    System.Console.SetCursorPosition(6, 7 + i * 2);

                    if (i == _selectedIndex)
                    {
                        System.Console.BackgroundColor = ConsoleColor.White;
                        System.Console.ForegroundColor = ConsoleColor.Black;
                    }
                    else
                    {
                        System.Console.BackgroundColor = winBg;
                        System.Console.ForegroundColor = p.IsLinuxSystem ? ConsoleColor.Green : (p.IsWritable ? ConsoleColor.Cyan : ConsoleColor.White);
                    }

                    string mount = !string.IsNullOrEmpty(p.MountedDriveLetter) ? $"[{p.MountedDriveLetter}]" : "[--]";
                    string line = $" {p.DeviceName} {mount} {p.FileSystemType} ({p.SizeDisplay}) ";
                    if (p.IsLinuxSystem) line += " [Linux Target]";
                    else if (p.IsWritable) line += " [Writable]";
                    else line += " [Read-Only]";

                    System.Console.Write(line.PadRight(68));
                }

                // Details of selected partition
                if (_selectedIndex >= 0 && _selectedIndex < _partitions.Count)
                {
                    var sel = _partitions[_selectedIndex];
                    System.Console.BackgroundColor = winBg;
                    System.Console.SetCursorPosition(6, 17);

                    if (_confirmingWipe)
                    {
                        System.Console.ForegroundColor = ConsoleColor.Red;
                        System.Console.Write("CONFIRM DISK WIPE: Erase ALL data & clean disk? [ENTER: Wipe | ESC: Cancel]");
                    }
                    else if (_confirmingFormat)
                    {
                        System.Console.ForegroundColor = ConsoleColor.Red;
                        System.Console.Write($"CONFIRM FORMAT: Wipe {sel.DeviceName} to FAT32? [ENTER: Yes | ESC: No]");
                    }
                    else if (!string.IsNullOrEmpty(_statusMessage))
                    {
                        System.Console.ForegroundColor = ConsoleColor.Green;
                        string st = _statusMessage.Length > 68 ? _statusMessage.Substring(0, 68) : _statusMessage;
                        System.Console.Write(st);
                    }
                    else
                    {
                        System.Console.ForegroundColor = ConsoleColor.Yellow;
                        string desc = $"Target: {sel.DeviceName} | FS: {sel.FileSystemType} | {sel.Description}";
                        if (desc.Length > 68) desc = desc.Substring(0, 68);
                        System.Console.Write(desc);
                    }
                }
            }

            System.Console.BackgroundColor = winBg;
            System.Console.SetCursorPosition(6, 19);
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.Write("[UP/DOWN: Select | F: Format Part | W: Wipe Entire Disk | ESC: Exit]");
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (_confirmingWipe)
            {
                if (keyEvent.Key == ConsoleKeyEx.Enter)
                {
                    string status;
                    DiskWipeService.WipeAndCleanDisk(0, out status);
                    _statusMessage = status;
                    Refresh();
                    _confirmingWipe = false;
                }
                else if (keyEvent.Key == ConsoleKeyEx.Escape)
                {
                    _confirmingWipe = false;
                    _statusMessage = "Disk wipe cancelled.";
                }
                return;
            }

            if (_confirmingFormat)
            {
                if (keyEvent.Key == ConsoleKeyEx.Enter)
                {
                    if (_selectedIndex >= 0 && _selectedIndex < _partitions.Count)
                    {
                        var sel = _partitions[_selectedIndex];
                        string target = !string.IsNullOrEmpty(sel.MountedDriveLetter) ? sel.MountedDriveLetter : sel.PartitionIndex.ToString();
                        string status;
                        StorageService.FormatDrive(target, "Fat32", out status);
                        _statusMessage = status;
                        Refresh();
                    }
                    _confirmingFormat = false;
                }
                else if (keyEvent.Key == ConsoleKeyEx.Escape)
                {
                    _confirmingFormat = false;
                    _statusMessage = "Format cancelled.";
                }
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                desktop.SetCurrentApp("launcher");
            }
            else if (keyEvent.Key == ConsoleKeyEx.UpArrow)
            {
                if (_partitions.Count > 0)
                {
                    _selectedIndex--;
                    if (_selectedIndex < 0) _selectedIndex = _partitions.Count - 1;
                }
            }
            else if (keyEvent.Key == ConsoleKeyEx.DownArrow)
            {
                if (_partitions.Count > 0)
                {
                    _selectedIndex++;
                    if (_selectedIndex >= _partitions.Count) _selectedIndex = 0;
                }
            }
            else if (keyEvent.KeyChar == 'f' || keyEvent.KeyChar == 'F')
            {
                if (_partitions.Count > 0)
                {
                    _confirmingFormat = true;
                }
            }
            else if (keyEvent.KeyChar == 'w' || keyEvent.KeyChar == 'W')
            {
                _confirmingWipe = true;
            }
            else if (keyEvent.KeyChar == 'r' || keyEvent.KeyChar == 'R')
            {
                Refresh();
                _statusMessage = "Partitions refreshed.";
            }
        }
    }
}
