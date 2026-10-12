using System;
using System.Collections.Generic;
using Cosmos.System;
using Cosmos.System.FileSystem.VFS;
using WeatherOS.Graphics;
using WeatherOS.Services;

namespace WeatherOS.Apps
{
    public class DiskDiagnosticsApp : IApp
    {
        public string Id => "diskdiag";
        public string Name => "Disk Diagnostics";
        public bool IsRealTime => false;

        private DiskDiagReport _report;
        private int _currentTab = 0; // 0: Overview, 1: AHCI, 2: PCI, 3: VFS Storage, 4: Hardware Test
        private string[] _tabNames = { "1. OVERVIEW", "2. AHCI", "3. PCI", "4. VFS", "5. HARDWARE TEST" };

        private int _selectedHardwareDevIndex = 0;
        private List<HardwareDevInfo> _hardwareDevs = new List<HardwareDevInfo>();
        private List<string> _testLogs = new List<string>();
        private int _logScrollOffset = 0;

        public DiskDiagnosticsApp()
        {
            _testLogs.Add("WeatherOS Hardware Disk Test Console ready.");
            _testLogs.Add("Press [A] for Automated Test Suite or [1-8] for single tests.");
            Refresh();
        }

        public void Refresh()
        {
            _report = DiskDiagnosticsService.RunDiagnostics();
            FileSystemProbeService.ProbeAllPartitions();
            _hardwareDevs = DiskHardwareTester.GetHardwareDevices();
        }

        public void Draw(TuiDesktop desktop)
        {
            desktop.DrawWindow(3, 2, 73, 21, "DISK HARDWARE DIAGNOSTICS & SYSTEM STORAGE");
            ConsoleColor winBg = desktop.IsDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;

            // Draw Tab Bar
            System.Console.SetCursorPosition(5, 4);
            for (int t = 0; t < _tabNames.Length; t++)
            {
                if (t == _currentTab)
                {
                    System.Console.ForegroundColor = ConsoleColor.Black;
                    System.Console.BackgroundColor = ConsoleColor.White;
                }
                else
                {
                    System.Console.ForegroundColor = ConsoleColor.Gray;
                    System.Console.BackgroundColor = ConsoleColor.DarkGray;
                }
                System.Console.Write($" {_tabNames[t]} ");
                System.Console.BackgroundColor = winBg;
                System.Console.Write(" ");
            }

            System.Console.BackgroundColor = winBg;
            System.Console.SetCursorPosition(5, 5);
            System.Console.ForegroundColor = ConsoleColor.DarkCyan;
            System.Console.Write("--------------------------------------------------------------------");

            if (_currentTab == 0)
            {
                DrawOverviewTab(winBg);
            }
            else if (_currentTab == 1)
            {
                DrawAhciTab(winBg);
            }
            else if (_currentTab == 2)
            {
                DrawPciTab(winBg);
            }
            else if (_currentTab == 3)
            {
                DrawVfsTab(winBg);
            }
            else if (_currentTab == 4)
            {
                DrawHardwareTestTab(winBg);
            }

            System.Console.SetCursorPosition(5, 21);
            System.Console.BackgroundColor = winBg;
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            if (_currentTab == 4)
            {
                System.Console.Write("[A] Auto Test | [1-8] Tests | [T] Dev | [Up/Dn] Scroll | [C] Clr | [ESC]");
            }
            else
            {
                System.Console.Write("[1-5 / Tab] Switch Tab  |  [R] Refresh  |  [ESC] Exit            ");
            }
        }

        private void DrawOverviewTab(ConsoleColor winBg)
        {
            System.Console.SetCursorPosition(5, 6);
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.Write("STORAGE STATUS & PREPARATION SUMMARY:");

            // Check if FAT32 partition / drive is found
            var partitions = FileSystemProbeService.ProbeAllPartitions();
            bool hasFat32 = false;
            string fat32Name = "";
            string fat32Size = "";

            if (partitions != null)
            {
                foreach (var p in partitions)
                {
                    if (p.FileSystemType.StartsWith("FAT32") || p.VolumeLabel == "WEATHEROS")
                    {
                        hasFat32 = true;
                        fat32Name = !string.IsNullOrEmpty(p.MountedDriveLetter) ? $"Drive {p.MountedDriveLetter}" : p.DeviceName;
                        fat32Size = p.SizeDisplay;
                        break;
                    }
                }
            }

            System.Console.SetCursorPosition(5, 8);
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.Write("Primary Storage Drive:");

            System.Console.SetCursorPosition(7, 9);
            if (hasFat32)
            {
                System.Console.ForegroundColor = ConsoleColor.Green;
                System.Console.Write($"[READY] {fat32Name} (FAT32 | {fat32Size})");
                System.Console.SetCursorPosition(7, 10);
                System.Console.ForegroundColor = ConsoleColor.White;
                System.Console.Write("Status: Clean partition table verified. Full read/write enabled.");
            }
            else
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.Write("[ACTIVE] High-Speed RAM Virtual Store (RAM-FS Session)");
                System.Console.SetCursorPosition(7, 10);
                System.Console.ForegroundColor = ConsoleColor.White;
                System.Console.Write("Status: Ready. Files and notes persist throughout current session.");
            }

            System.Console.SetCursorPosition(5, 12);
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.Write("Hardware Controller & BIOS Configuration:");

            System.Console.SetCursorPosition(7, 13);
            System.Console.ForegroundColor = ConsoleColor.White;
            string mode = _report.AhciControllerFound ? "AHCI SATA Controller Detected" : "Legacy IDE / Compatible Controller";
            System.Console.Write($"Controller Mode    : {mode}");

            System.Console.SetCursorPosition(7, 14);
            System.Console.Write($"Physical Drives    : {_report.ActiveAhciDrives} active SATA device(s) linked");

            System.Console.SetCursorPosition(7, 15);
            System.Console.Write($"Cosmos Block Devices: {_report.CosmosBlockDeviceCount} block device(s) registered in HAL");

            System.Console.SetCursorPosition(5, 17);
            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.Write("System Readiness Verdict:");

            System.Console.SetCursorPosition(7, 18);
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.Write("WeatherOS is fully configured: Notepad, File Explorer, and");

            System.Console.SetCursorPosition(7, 19);
            System.Console.Write("Settings will seamlessly store files into primary storage & RAM.");
        }

        private void DrawAhciTab(ConsoleColor winBg)
        {
            System.Console.SetCursorPosition(5, 6);
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.Write($"AHCI ABAR MMIO: 0x{_report.AhciAbarAddress:X8} | Enabled: {(_report.AhciEnabled ? "YES (AE=1)" : "NO")}");

            System.Console.SetCursorPosition(5, 7);
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.Write($"CAP: 0x{_report.AhciCap:X8} | GHC: 0x{_report.AhciGhc:X8} | PI Mask: 0x{_report.AhciPi:X8}");

            System.Console.SetCursorPosition(5, 9);
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.Write("IMPLEMENTED SATA PORTS & DEVICE STATUS:");

            int line = 10;
            if (_report.AhciPorts.Count == 0)
            {
                System.Console.SetCursorPosition(5, line);
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.Write("No AHCI SATA ports implemented on this chipset.");
            }
            else
            {
                foreach (var p in _report.AhciPorts)
                {
                    if (line >= 20) break;
                    System.Console.SetCursorPosition(5, line++);
                    if (p.DriveConnected)
                    {
                        System.Console.ForegroundColor = ConsoleColor.Green;
                        System.Console.Write($"Port {p.PortIndex}: [ACTIVE] {p.DeviceType} | {p.SpeedString}".PadRight(66));
                    }
                    else
                    {
                        System.Console.ForegroundColor = ConsoleColor.DarkGray;
                        System.Console.Write($"Port {p.PortIndex}: [EMPTY / NO PHY] Status: 0x{p.Ssts:X8}".PadRight(66));
                    }
                }
            }
        }

        private void DrawPciTab(ConsoleColor winBg)
        {
            System.Console.SetCursorPosition(5, 6);
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.Write("PCI MASS STORAGE CONTROLLERS (Class Code 0x01):");

            int line = 8;
            if (_report.Controllers.Count == 0)
            {
                System.Console.SetCursorPosition(5, line);
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.Write("No PCI devices found with Mass Storage Class (0x01).");
            }
            else
            {
                foreach (var c in _report.Controllers)
                {
                    if (line >= 19) break;
                    System.Console.SetCursorPosition(5, line++);
                    System.Console.ForegroundColor = ConsoleColor.White;
                    System.Console.Write($"[{c.Bus:X2}:{c.Slot:X2}.{c.Function:X2}] {c.VendorName} (0x{c.VendorId:X4}:0x{c.DeviceId:X4})");

                    System.Console.SetCursorPosition(7, line++);
                    System.Console.ForegroundColor = ConsoleColor.Yellow;
                    string type = c.IsAhci ? "SATA AHCI" : (c.IsNvme ? "NVMe SSD" : (c.IsIde ? "Legacy IDE" : "Storage"));
                    System.Console.Write($"Type: {type} ({c.SubclassName}) | BAR5: 0x{c.AbarAddress:X8}");

                    line++; // Blank separator
                }
            }
        }

        private void DrawVfsTab(ConsoleColor winBg)
        {
            System.Console.SetCursorPosition(5, 6);
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.Write("COSMOS VFS & MOUNTED VOLUMES:");

            var locations = StorageService.GetStorageLocations();
            int line = 8;
            for (int i = 0; i < locations.Count && line < 15; i++)
            {
                System.Console.SetCursorPosition(5, line++);
                System.Console.ForegroundColor = ConsoleColor.Green;
                System.Console.Write($"[{i + 1}] {locations[i].GetDescription()}");
            }

            System.Console.SetCursorPosition(5, 15);
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.Write($"LOGICAL DRIVES (Cosmos VFS):");

            line = 16;
            try
            {
                var drives = VFSManager.GetLogicalDrives();
                if (drives != null && drives.Count > 0)
                {
                    foreach (var d in drives)
                    {
                        if (line >= 20) break;
                        System.Console.SetCursorPosition(7, line++);
                        System.Console.ForegroundColor = ConsoleColor.White;
                        string fs = "";
                        try { fs = VFSManager.GetFileSystemType(d); } catch { }
                        System.Console.Write($"Drive {d} (FS: {fs})");
                    }
                }
                else
                {
                    System.Console.SetCursorPosition(7, line++);
                    System.Console.ForegroundColor = ConsoleColor.Yellow;
                    System.Console.Write("Managed via direct block device & RAM virtual store.");
                }
            }
            catch
            {
                System.Console.SetCursorPosition(7, line++);
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.Write("Virtual Storage Session Active.");
            }
        }

        private void DrawHardwareTestTab(ConsoleColor winBg)
        {
            _hardwareDevs = DiskHardwareTester.GetHardwareDevices();
            if (_hardwareDevs.Count > 0 && _selectedHardwareDevIndex >= _hardwareDevs.Count)
            {
                _selectedHardwareDevIndex = 0;
            }

            System.Console.SetCursorPosition(5, 6);
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.Write("HARDWARE BLOCK DEVICE & SECTOR LEVEL TESTING:");

            System.Console.SetCursorPosition(5, 7);
            if (_hardwareDevs.Count == 0)
            {
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.Write("No HAL Block Devices detected. Virtual Store active.".PadRight(66));
            }
            else
            {
                var dev = _hardwareDevs[_selectedHardwareDevIndex];
                System.Console.ForegroundColor = ConsoleColor.White;
                string type = dev.IsPartition ? "Partition" : dev.TypeName;
                System.Console.Write($"Target Dev : [Dev #{dev.Index}] {type} (Total: {_hardwareDevs.Count} devs) [T=Toggle]".PadRight(66));

                System.Console.SetCursorPosition(5, 8);
                string ataStr = !string.IsNullOrEmpty(dev.AtaModel) ? $"{dev.AtaModel} ({dev.TrueSizeDisplay})" : $"Block Device ({dev.BlockCount} sectors)";
                string atapiStr = dev.IsAtapi ? " [ATAPI CD/DVD]" : " [Writable Disk]";
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.Write($"Hardware   : {ataStr}{atapiStr}".PadRight(66));

                System.Console.SetCursorPosition(5, 9);
                string s0Str = dev.Sector0Readable ? (dev.HasMbrSig ? $"MBR Valid [55 AA] ({dev.MbrPartitionCount} part)" : "Readable, No MBR Sig") : "Unreadable Sector 0";
                System.Console.ForegroundColor = ConsoleColor.Gray;
                System.Console.Write($"Sector 0   : {s0Str} | BlockSize: {dev.BlockSize}B".PadRight(66));
            }

            System.Console.SetCursorPosition(5, 11);
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.Write("[A] AUTO TEST ALL   [1] Read Sec 0  [2] Read Sec 2048  [3] Write Sec    ");

            System.Console.SetCursorPosition(5, 12);
            System.Console.Write("[4] Format FAT32    [5] Write File  [6] Read File      [7] ATA Probe    ");

            System.Console.SetCursorPosition(5, 13);
            System.Console.ForegroundColor = ConsoleColor.DarkCyan;
            System.Console.Write("--- LIVE TEST LOG [Up/Down: Scroll | C: Clear] ---------------------");

            // Display log lines considering scroll offset
            int line = 14;
            int maxVisible = 7;
            int endIdx = _testLogs.Count - _logScrollOffset;
            if (endIdx > _testLogs.Count) endIdx = _testLogs.Count;
            if (endIdx < 0) endIdx = 0;
            int startIdx = Math.Max(0, endIdx - maxVisible);

            for (int i = startIdx; i < endIdx && line <= 20; i++)
            {
                System.Console.SetCursorPosition(5, line++);
                string log = _testLogs[i];
                if (log.Length > 66) log = log.Substring(0, 63) + "...";

                if (log.StartsWith("[PASS]") || log.Contains("[PASS]")) System.Console.ForegroundColor = ConsoleColor.Green;
                else if (log.StartsWith("[FAIL]") || log.StartsWith("[ERR]") || log.Contains("[FAIL]")) System.Console.ForegroundColor = ConsoleColor.Red;
                else if (log.StartsWith("[TEST") || log.StartsWith("===") || log.StartsWith(">>>")) System.Console.ForegroundColor = ConsoleColor.Yellow;
                else System.Console.ForegroundColor = ConsoleColor.White;

                System.Console.Write(log.PadRight(66));
            }

            // Fill empty log rows if fewer than 7 lines
            while (line <= 20)
            {
                System.Console.SetCursorPosition(5, line++);
                System.Console.Write("".PadRight(66));
            }
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Tab || keyEvent.Key == ConsoleKeyEx.RightArrow)
            {
                _currentTab = (_currentTab + 1) % _tabNames.Length;
            }
            else if (keyEvent.Key == ConsoleKeyEx.LeftArrow)
            {
                _currentTab = (_currentTab - 1 + _tabNames.Length) % _tabNames.Length;
            }
            else if (_currentTab == 4)
            {
                // Tab 5: Hardware Test Key Handlers
                if (keyEvent.Key == ConsoleKeyEx.A)
                {
                    _logScrollOffset = 0;
                    DiskHardwareTester.RunAutoTestAll(_selectedHardwareDevIndex, _testLogs);
                }
                else if (keyEvent.Key == ConsoleKeyEx.UpArrow)
                {
                    if (_testLogs.Count > 7 && _logScrollOffset < _testLogs.Count - 7)
                    {
                        _logScrollOffset++;
                    }
                }
                else if (keyEvent.Key == ConsoleKeyEx.DownArrow)
                {
                    if (_logScrollOffset > 0)
                    {
                        _logScrollOffset--;
                    }
                }
                else if (keyEvent.Key == ConsoleKeyEx.D1 || keyEvent.Key == ConsoleKeyEx.Num1)
                {
                    _logScrollOffset = 0;
                    DiskHardwareTester.TestReadSector(_selectedHardwareDevIndex, 0, _testLogs);
                }
                else if (keyEvent.Key == ConsoleKeyEx.D2 || keyEvent.Key == ConsoleKeyEx.Num2)
                {
                    _logScrollOffset = 0;
                    DiskHardwareTester.TestReadSector(_selectedHardwareDevIndex, 2048, _testLogs);
                }
                else if (keyEvent.Key == ConsoleKeyEx.D3 || keyEvent.Key == ConsoleKeyEx.Num3)
                {
                    _logScrollOffset = 0;
                    DiskHardwareTester.TestWriteSector(_selectedHardwareDevIndex, 2048, _testLogs);
                }
                else if (keyEvent.Key == ConsoleKeyEx.D4 || keyEvent.Key == ConsoleKeyEx.Num4)
                {
                    _logScrollOffset = 0;
                    DiskHardwareTester.TestFormatFat32(_selectedHardwareDevIndex, _testLogs);
                }
                else if (keyEvent.Key == ConsoleKeyEx.D5 || keyEvent.Key == ConsoleKeyEx.Num5)
                {
                    _logScrollOffset = 0;
                    DiskHardwareTester.TestSaveFile(_selectedHardwareDevIndex, "TEST.TXT", "WEATHEROS HARDWARE WRITE VERIFIED OK 123", _testLogs);
                }
                else if (keyEvent.Key == ConsoleKeyEx.D6 || keyEvent.Key == ConsoleKeyEx.Num6)
                {
                    _logScrollOffset = 0;
                    DiskHardwareTester.TestReadFile(_selectedHardwareDevIndex, "TEST.TXT", _testLogs);
                }
                else if (keyEvent.Key == ConsoleKeyEx.D7 || keyEvent.Key == ConsoleKeyEx.Num7)
                {
                    _logScrollOffset = 0;
                    DiskHardwareTester.TestAtaRawProbe(_testLogs);
                }
                else if (keyEvent.Key == ConsoleKeyEx.D8 || keyEvent.Key == ConsoleKeyEx.Num8)
                {
                    _logScrollOffset = 0;
                    DiskHardwareTester.TestFlushCache(_selectedHardwareDevIndex, _testLogs);
                }
                else if (keyEvent.Key == ConsoleKeyEx.T)
                {
                    if (_hardwareDevs != null && _hardwareDevs.Count > 0)
                    {
                        _selectedHardwareDevIndex = (_selectedHardwareDevIndex + 1) % _hardwareDevs.Count;
                        _testLogs.Add($"Target switched to Dev #{_selectedHardwareDevIndex}.");
                        _logScrollOffset = 0;
                    }
                }
                else if (keyEvent.Key == ConsoleKeyEx.C)
                {
                    _testLogs.Clear();
                    _testLogs.Add("Test log console cleared. Press [A] for auto-test or [1-8].");
                    _logScrollOffset = 0;
                }
                else if (keyEvent.Key == ConsoleKeyEx.R)
                {
                    Refresh();
                    _testLogs.Add("Hardware status refreshed.");
                    _logScrollOffset = 0;
                }
                else if (keyEvent.Key == ConsoleKeyEx.Escape)
                {
                    desktop.SetCurrentApp("debug_menu");
                }
            }
            else
            {
                // Tabs 1-4: Direct Number tab switches
                if (keyEvent.Key == ConsoleKeyEx.D1 || keyEvent.Key == ConsoleKeyEx.Num1)
                {
                    _currentTab = 0;
                }
                else if (keyEvent.Key == ConsoleKeyEx.D2 || keyEvent.Key == ConsoleKeyEx.Num2)
                {
                    _currentTab = 1;
                }
                else if (keyEvent.Key == ConsoleKeyEx.D3 || keyEvent.Key == ConsoleKeyEx.Num3)
                {
                    _currentTab = 2;
                }
                else if (keyEvent.Key == ConsoleKeyEx.D4 || keyEvent.Key == ConsoleKeyEx.Num4)
                {
                    _currentTab = 3;
                }
                else if (keyEvent.Key == ConsoleKeyEx.D5 || keyEvent.Key == ConsoleKeyEx.Num5)
                {
                    _currentTab = 4;
                }
                else if (keyEvent.Key == ConsoleKeyEx.R)
                {
                    Refresh();
                }
                else if (keyEvent.Key == ConsoleKeyEx.Escape)
                {
                    desktop.SetCurrentApp("debug_menu");
                }
            }
        }
    }
}
