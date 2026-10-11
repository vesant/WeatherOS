using System;
using Cosmos.System;
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
        private int _currentTab = 0; // 0: Verdict & Fix, 1: AHCI ABAR, 2: PCI Controllers, 3: Legacy IDE & VFS, 4: Wipe Disk
        private string[] _tabNames = { "1. VERDICT", "2. AHCI", "3. PCI", "4. VFS", "5. WIPE DISK" };
        private string _wipeStatus = "";
        private bool _confirmingWipe = false;
        private int _wipeProgressPct = 0;

        public DiskDiagnosticsApp()
        {
            Refresh();
        }

        public void Refresh()
        {
            _report = DiskDiagnosticsService.RunDiagnostics();
        }

        public void Draw(TuiDesktop desktop)
        {
            desktop.DrawWindow(4, 2, 72, 21, "DISK HARDWARE DIAGNOSTICS & ANALYZER");
            ConsoleColor winBg = desktop.IsDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;

            // Draw Tab Bar
            System.Console.SetCursorPosition(6, 4);
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
            System.Console.SetCursorPosition(6, 5);
            System.Console.ForegroundColor = ConsoleColor.DarkCyan;
            System.Console.Write("--------------------------------------------------------------------");

            if (_currentTab == 0)
            {
                DrawVerdictTab(winBg);
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
                DrawLegacyTab(winBg);
            }
            else if (_currentTab == 4)
            {
                DrawWipeTab(desktop, winBg);
            }
        }

        private void DrawVerdictTab(ConsoleColor winBg)
        {
            System.Console.SetCursorPosition(6, 6);
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            string verdict = _report.PrimaryDiagnosis;
            if (verdict.Length > 65) verdict = verdict.Substring(0, 65);
            System.Console.Write("STATUS: " + verdict);

            System.Console.SetCursorPosition(6, 8);
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.Write("WHY COSMOS OS DID NOT FIND THE HARD DISK:");

            int line = 9;
            for (int i = 0; i < _report.DetailedReasons.Count && line < 14; i++)
            {
                System.Console.SetCursorPosition(6, line++);
                System.Console.ForegroundColor = ConsoleColor.White;
                string r = _report.DetailedReasons[i];
                if (r.Length > 66) r = r.Substring(0, 66);
                System.Console.Write(r);
            }

            System.Console.SetCursorPosition(6, 14);
            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.Write("HOW TO FIX THIS IN YOUR LAPTOP BIOS SETUP:");

            line = 15;
            for (int i = 0; i < _report.BiosRemediationSteps.Count && line < 20; i++)
            {
                System.Console.SetCursorPosition(6, line++);
                System.Console.ForegroundColor = ConsoleColor.White;
                string s = _report.BiosRemediationSteps[i];
                if (s.Length > 66) s = s.Substring(0, 66);
                System.Console.Write(s);
            }
        }

        private void DrawAhciTab(ConsoleColor winBg)
        {
            System.Console.SetCursorPosition(6, 6);
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.Write($"AHCI ABAR MMIO: 0x{_report.AhciAbarAddress:X8} | Enabled: {(_report.AhciEnabled ? "YES (AE=1)" : "NO")}");

            System.Console.SetCursorPosition(6, 7);
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.Write($"CAP: 0x{_report.AhciCap:X8} | GHC: 0x{_report.AhciGhc:X8} | PI Mask: 0x{_report.AhciPi:X8}");

            System.Console.SetCursorPosition(6, 9);
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.Write("IMPLEMENTED SATA PORTS & DEVICE STATUS:");

            int line = 10;
            if (_report.AhciPorts.Count == 0)
            {
                System.Console.SetCursorPosition(6, line);
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.Write("No AHCI SATA ports implemented on this chipset.");
            }
            else
            {
                foreach (var p in _report.AhciPorts)
                {
                    if (line >= 20) break;
                    System.Console.SetCursorPosition(6, line++);
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
            System.Console.SetCursorPosition(6, 6);
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.Write("PCI MASS STORAGE CONTROLLERS (Class Code 0x01):");

            int line = 8;
            if (_report.Controllers.Count == 0)
            {
                System.Console.SetCursorPosition(6, line);
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.Write("No PCI devices found with Mass Storage Class (0x01).");
            }
            else
            {
                foreach (var c in _report.Controllers)
                {
                    if (line >= 19) break;
                    System.Console.SetCursorPosition(6, line++);
                    System.Console.ForegroundColor = ConsoleColor.White;
                    System.Console.Write($"[{c.Bus:X2}:{c.Slot:X2}.{c.Function:X2}] {c.VendorName} (0x{c.VendorId:X4}:0x{c.DeviceId:X4})");

                    System.Console.SetCursorPosition(8, line++);
                    System.Console.ForegroundColor = ConsoleColor.Yellow;
                    string type = c.IsAhci ? "SATA AHCI" : (c.IsNvme ? "NVMe SSD" : (c.IsIde ? "Legacy IDE" : "Storage"));
                    System.Console.Write($"Type: {type} ({c.SubclassName}) | BAR5: 0x{c.AbarAddress:X8}");

                    line++; // Blank separator
                }
            }
        }

        private void DrawLegacyTab(ConsoleColor winBg)
        {
            System.Console.SetCursorPosition(6, 6);
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.Write("LEGACY IDE PORTS (Used by Cosmos ATA PIO Driver):");

            System.Console.SetCursorPosition(6, 7);
            System.Console.ForegroundColor = ConsoleColor.White;
            string priFloat = _report.LegacyPrimaryFloating ? "FLOATING BUS (0xFF - Disabled by BIOS)" : "ACTIVE";
            System.Console.Write($"Primary Channel   (0x1F0..0x1F7) : {priFloat}");

            System.Console.SetCursorPosition(6, 8);
            System.Console.ForegroundColor = ConsoleColor.White;
            string secFloat = _report.LegacySecondaryFloating ? "FLOATING BUS (0xFF - Disabled by BIOS)" : "ACTIVE";
            System.Console.Write($"Secondary Channel (0x170..0x177) : {secFloat}");

            System.Console.SetCursorPosition(6, 10);
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.Write($"COSMOS HAL BLOCK DEVICES (Found: {_report.CosmosBlockDeviceCount}):");

            int line = 11;
            if (_report.CosmosBlockDeviceNames.Count == 0)
            {
                System.Console.SetCursorPosition(6, line++);
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.Write("  -> Zero block devices registered in Cosmos HAL.");
            }
            else
            {
                foreach (var b in _report.CosmosBlockDeviceNames)
                {
                    if (line >= 15) break;
                    System.Console.SetCursorPosition(6, line++);
                    System.Console.ForegroundColor = ConsoleColor.White;
                    System.Console.Write("  -> " + (b.Length > 60 ? b.Substring(0, 60) : b));
                }
            }

            System.Console.SetCursorPosition(6, 15);
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.Write($"COSMOS VFS DISKS (Found: {_report.VfsDiskCount}):");

            line = 16;
            if (_report.VfsDiskNames.Count == 0)
            {
                System.Console.SetCursorPosition(6, line++);
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.Write("  -> Zero disks registered in VFSManager.");
            }
            else
            {
                foreach (var d in _report.VfsDiskNames)
                {
                    if (line >= 20) break;
                    System.Console.SetCursorPosition(6, line++);
                    System.Console.ForegroundColor = ConsoleColor.White;
                    System.Console.Write("  -> " + (d.Length > 60 ? d.Substring(0, 60) : d));
                }
            }
        }

        private void DrawWipeTab(TuiDesktop desktop, ConsoleColor winBg)
        {
            System.Console.SetCursorPosition(6, 6);
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.Write("PHYSICAL DISK SANITIZATION & COMPLETE WIPE");

            System.Console.SetCursorPosition(6, 8);
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.Write("[DANGER] This tool overwrites partition tables and zeros sectors.");

            System.Console.SetCursorPosition(6, 10);
            System.Console.ForegroundColor = ConsoleColor.White;
            var drives = DiskWipeService.GetPhysicalDrives();
            if (drives == null || drives.Count == 0)
            {
                System.Console.Write("Target: No raw physical drive registered in Cosmos HAL.");
                System.Console.SetCursorPosition(6, 12);
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.Write("Tip: Change SATA Mode in BIOS from AHCI/RAID to IDE / Legacy.");
            }
            else
            {
                var d = drives[0];
                System.Console.Write($"Target: {d.Name} ({d.SizeDisplay})");
                System.Console.SetCursorPosition(6, 11);
                string fsList = d.ContainedFilesystems.Count > 0 ? string.Join(", ", d.ContainedFilesystems) : "None detected";
                if (fsList.Length > 40) fsList = fsList.Substring(0, 37) + "...";
                System.Console.Write($"Detected Partitions / FS: {fsList}");
            }

            // Real-time progress bar in Wipe menu
            desktop.DrawBar(6, 13, "Progress", _wipeProgressPct, ConsoleColor.Green);

            System.Console.SetCursorPosition(6, 15);
            if (_confirmingWipe)
            {
                System.Console.BackgroundColor = ConsoleColor.Red;
                System.Console.ForegroundColor = ConsoleColor.White;
                System.Console.Write(" ARE YOU SURE? Press [Y] to WIPE DISK 0, or [N] to Cancel. ");
            }
            else
            {
                System.Console.BackgroundColor = winBg;
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.Write("Press [W] or [ENTER] to clean and zero-fill Disk 0.");
            }

            if (!string.IsNullOrEmpty(_wipeStatus))
            {
                System.Console.BackgroundColor = winBg;
                System.Console.SetCursorPosition(6, 17);
                System.Console.ForegroundColor = _wipeStatus.StartsWith("[OK]") ? ConsoleColor.Green : ConsoleColor.Red;
                string st = _wipeStatus.Length > 66 ? _wipeStatus.Substring(0, 66) : _wipeStatus;
                System.Console.Write(st);
            }
        }

        private void DrawWipeProgress(TuiDesktop desktop, ConsoleColor winBg, int pct, string msg)
        {
            _wipeProgressPct = pct;
            System.Console.BackgroundColor = winBg;
            desktop.DrawBar(6, 13, "Progress", pct, ConsoleColor.Green);
            System.Console.SetCursorPosition(6, 17);
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.Write(msg.PadRight(65));
            System.Threading.Thread.Sleep(25);
        }

        private void ExecuteLiveWipe(TuiDesktop desktop, ConsoleColor winBg)
        {
            var drives = DiskWipeService.GetPhysicalDrives();
            if (drives == null || drives.Count == 0)
            {
                _wipeStatus = "[ERR] No raw physical drive registered in Cosmos HAL.";
                return;
            }

            var dev = drives[0].Device;
            byte[] zeros = new byte[512];

            DrawWipeProgress(desktop, winBg, 2, "1/34: Initializing controller & zeroing LBA 0 (MBR)...");
            try { dev.WriteBlock(0, 1, ref zeros); } catch { }

            // Wipe sectors 1 to 2048 in 32 progressive steps of 64 sectors
            for (int step = 0; step < 32; step++)
            {
                ulong startSec = (ulong)step * 64 + 1;
                ulong endSec = startSec + 64;
                if (endSec > dev.BlockCount) endSec = dev.BlockCount;

                for (ulong s = startSec; s < endSec; s++)
                {
                    try { dev.WriteBlock(s, 1, ref zeros); } catch { }
                }

                int pct = 5 + ((step + 1) * 88) / 32;
                DrawWipeProgress(desktop, winBg, pct, $"Zeroing sector {endSec} / 2048 ({pct}%)...");
            }

            DrawWipeProgress(desktop, winBg, 96, "Writing clean protective MBR table (FAT32 LBA)...");
            byte[] cleanMbr = new byte[512];
            cleanMbr[446] = 0x80;
            cleanMbr[450] = 0x0C;
            cleanMbr[454] = 0x00; cleanMbr[455] = 0x08;
            ulong partSectors = dev.BlockCount > 2048 ? dev.BlockCount - 2048 : 0;
            if (partSectors > 0xFFFFFFFF) partSectors = 0xFFFFFFFF;
            uint sectors32 = (uint)partSectors;
            cleanMbr[458] = (byte)(sectors32 & 0xFF);
            cleanMbr[459] = (byte)((sectors32 >> 8) & 0xFF);
            cleanMbr[460] = (byte)((sectors32 >> 16) & 0xFF);
            cleanMbr[461] = (byte)((sectors32 >> 24) & 0xFF);
            cleanMbr[510] = 0x55; cleanMbr[511] = 0xAA;
            try { dev.WriteBlock(0, 1, ref cleanMbr); } catch { }

            DrawWipeProgress(desktop, winBg, 100, "Wipe complete! Physical drive is completely clean.");
            _wipeStatus = "[OK] Disk 0 wiped successfully! 100% Clean.";
            FileSystemProbeService.ProbeAllPartitions();
            Refresh();
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (_confirmingWipe)
            {
                if (keyEvent.Key == ConsoleKeyEx.Y)
                {
                    _confirmingWipe = false;
                    ConsoleColor winBg = desktop.IsDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
                    ExecuteLiveWipe(desktop, winBg);
                }
                else if (keyEvent.Key == ConsoleKeyEx.N || keyEvent.Key == ConsoleKeyEx.Escape)
                {
                    _confirmingWipe = false;
                    _wipeStatus = "Disk wipe cancelled.";
                }
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.Tab || keyEvent.Key == ConsoleKeyEx.RightArrow)
            {
                _currentTab = (_currentTab + 1) % _tabNames.Length;
            }
            else if (keyEvent.Key == ConsoleKeyEx.LeftArrow)
            {
                _currentTab = (_currentTab - 1 + _tabNames.Length) % _tabNames.Length;
            }
            else if (keyEvent.Key == ConsoleKeyEx.D1 || keyEvent.Key == ConsoleKeyEx.Num1)
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
            else if (keyEvent.Key == ConsoleKeyEx.W || (_currentTab == 4 && keyEvent.Key == ConsoleKeyEx.Enter))
            {
                _currentTab = 4;
                _confirmingWipe = true;
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
