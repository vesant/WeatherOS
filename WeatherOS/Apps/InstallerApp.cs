using System;
using System.Collections.Generic;
using Cosmos.System;
using WeatherOS.Graphics;
using WeatherOS.Services;

namespace WeatherOS.Apps
{
    public enum InstallerStep
    {
        SelectDisk,
        ConfirmWarning,
        Installing,
        Complete,
        Error
    }

    public class InstallerApp : IApp
    {
        public string Id => "installer";
        public string Name => "Hard Disk Installer";
        public bool IsRealTime => false;

        private InstallerStep _step = InstallerStep.SelectDisk;
        private List<DiskDriveInfo> _availableDrives = null;
        private int _selectedDriveIndex = 0;
        private int _progressPercent = 0;
        private string _progressText = "Preparing installation...";
        private string _statusResult = "";
        private bool _installationTriggered = false;

        public void Draw(TuiDesktop desktop)
        {
            switch (_step)
            {
                case InstallerStep.SelectDisk:
                    DrawSelectDisk(desktop);
                    break;
                case InstallerStep.ConfirmWarning:
                    DrawConfirmWarning(desktop);
                    break;
                case InstallerStep.Installing:
                    DrawInstalling(desktop);
                    break;
                case InstallerStep.Complete:
                    DrawComplete(desktop);
                    break;
                case InstallerStep.Error:
                    DrawError(desktop);
                    break;
            }
        }

        private void RefreshDrives()
        {
            _availableDrives = DiskWipeService.GetPhysicalDrives();
            if (_availableDrives == null) _availableDrives = new List<DiskDriveInfo>();
            if (_selectedDriveIndex >= _availableDrives.Count) _selectedDriveIndex = Math.Max(0, _availableDrives.Count - 1);

            // Auto-select internal hard disk by default if present
            for (int i = 0; i < _availableDrives.Count; i++)
            {
                var d = _availableDrives[i];
                if (!d.Name.Contains("Live USB") && !d.Name.Contains("Boot Medium") && !d.Name.Contains("Boot Drive"))
                {
                    _selectedDriveIndex = i;
                    break;
                }
            }
        }

        private void DrawSelectDisk(TuiDesktop desktop)
        {
            if (_availableDrives == null) RefreshDrives();

            var win = desktop.CreateWindow("WEATHEROS HARD DISK INSTALLER  |  SELECT TARGET DRIVE", WindowSize.FullScreen);

            win.WriteRow(0, "STEP 1: SELECT PHYSICAL DISK FOR INSTALLATION", ConsoleColor.Cyan, 2, fillRemaining: true);
            win.WriteRow(1, "Select target drive to install WeatherOS permanently:", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteSeparator(2);

            win.WriteRow(3, "DETECTED STORAGE CONTROLLERS & DRIVES:", ConsoleColor.Yellow, 2, fillRemaining: true);

            if (_availableDrives.Count == 0)
            {
                win.WriteRow(5, "[!] No physical hard drives detected. Check IDE/SATA controller.", ConsoleColor.Red, 2, fillRemaining: true);
                win.WriteRow(6, "You can continue running WeatherOS in Live OS (RAM) mode.", ConsoleColor.Gray, 2, fillRemaining: true);
            }
            else
            {
                for (int i = 0; i < _availableDrives.Count && i < 5; i++)
                {
                    var d = _availableDrives[i];
                    bool isSel = (i == _selectedDriveIndex);
                    bool isUsb = d.Name.Contains("Live USB") || d.Name.Contains("Boot Medium") || d.Name.Contains("Boot Drive");
                    string tag = isUsb ? "[LIVE USB]" : "[TARGET DISK]";
                    string line = $"[{i}] {d.Name}  {tag}";
                    if (line.Length > 65) line = line.Substring(0, 65);
                    win.WriteSelectionRow(5 + (i * 2), line, isSel, 2);
                }
            }

            win.WriteSeparator(13);
            win.WriteRow(14, "[i] TIP: Choose internal disk (Disk 0) to boot without USB pen.", ConsoleColor.DarkCyan, 2, fillRemaining: true);
            win.WriteRow(15, "    Future updates are applied in-place without wiping data!", ConsoleColor.DarkCyan, 2, fillRemaining: true);

            win.WriteFooter("[Up/Dn] Select  |  [ENTER] Next  |  [R] Refresh  |  [ESC] Exit", ConsoleColor.Yellow);
        }

        private void DrawConfirmWarning(TuiDesktop desktop)
        {
            var win = desktop.CreateWindow("INSTALLATION CONFIRMATION  |  WARNING", WindowSize.FullScreen);

            var d = (_availableDrives != null && _selectedDriveIndex < _availableDrives.Count)
                ? _availableDrives[_selectedDriveIndex]
                : null;

            string targetName = d != null ? d.Name : "Target Drive";
            if (targetName.Length > 44) targetName = targetName.Substring(0, 44);
            string targetSize = d != null ? d.SizeDisplay : "Unknown";
            bool isUsbTarget = d != null && (d.Name.Contains("Live USB") || d.Name.Contains("Boot Medium") || d.Name.Contains("Boot Drive"));

            win.WriteRow(0, "STEP 2: CONFIRM PARTITION INITIALIZATION", ConsoleColor.Red, 2, fillRemaining: true);
            win.WriteRow(1, "Review target drive parameters before proceeding:", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteSeparator(2);

            win.WriteRow(3, $"  Target Drive : {targetName}", ConsoleColor.Yellow, 2, fillRemaining: true);
            win.WriteRow(4, $"  Capacity     : {targetSize}", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteRow(5,  "  File System  : FAT32 LBA Partition (Volume: WEATHEROS)", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteRow(6,  "  Boot File    : 0:\\boot\\WeatherOS.bin (x86 MBR Bootloader)", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteSeparator(7);

            if (isUsbTarget)
            {
                win.WriteRow(8,  "[!] CAUTION: You have selected the Live USB Pen Drive!", ConsoleColor.Red, 2, fillRemaining: true);
                win.WriteRow(9,  "To install permanently to your computer, press [ESC] and choose", ConsoleColor.Yellow, 2, fillRemaining: true);
                win.WriteRow(10, "the internal Hard Disk instead of the boot pen drive.", ConsoleColor.Yellow, 2, fillRemaining: true);
                win.WriteRow(11, "If you proceed, this will format the Live USB pen drive.", ConsoleColor.Red, 2, fillRemaining: true);
            }
            else
            {
                win.WriteRow(8,  "[!] NOTICE: HARD DISK INSTALLATION & BOOTLOADER SETUP:", ConsoleColor.Yellow, 2, fillRemaining: true);
                win.WriteRow(9,  " - Partition 1 will be initialized as FAT32 on this disk.", ConsoleColor.White, 2, fillRemaining: true);
                win.WriteRow(10, " - Executable x86 MBR bootloader written to Sector 0 for BIOS.", ConsoleColor.Green, 2, fillRemaining: true);
                win.WriteRow(11, " - Subsequent updates will NEVER wipe this disk! Data preserved.", ConsoleColor.Green, 2, fillRemaining: true);
            }
            win.WriteSeparator(12);

            if (isUsbTarget)
            {
                win.WriteRow(13, "[ESC] Go Back & Select Internal Hard Disk to Install", ConsoleColor.Yellow, 2, fillRemaining: true);
                win.WriteFooter("[ESC] Back to Disk Selection (Cannot install to Live USB)", ConsoleColor.Yellow);
            }
            else
            {
                win.WriteRow(13, "Press [Y] or [ENTER] to Install   |   [N / ESC] Cancel", ConsoleColor.Yellow, 2, fillRemaining: true);
                win.WriteFooter("[Y / ENTER] Confirm Install   |   [N / ESC] Cancel", ConsoleColor.Yellow);
            }
        }

        private int _lastDrawnPercent = -1;

        private void DrawInstalling(TuiDesktop desktop)
        {
            var win = desktop.CreateWindow("INSTALLING WEATHEROS TO DISK  |  PLEASE WAIT", WindowSize.FullScreen);

            win.WriteRow(0, "STEP 3: DEPLOYING SYSTEM FILES & BOOTLOADER", ConsoleColor.Cyan, 2, fillRemaining: true);
            win.WriteRow(1, "Writing operating system structures to physical disk...", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteSeparator(2);

            win.WriteRow(4, "CURRENT OPERATION:", ConsoleColor.Yellow, 2, fillRemaining: true);
            string progText = _progressText ?? "";
            if (progText.Length > 60) progText = progText.Substring(0, 60);
            win.WriteRow(5, "  " + progText, ConsoleColor.White, 2, fillRemaining: true);

            // Progress bar
            int barWidth = 46;
            int filled = (_progressPercent * barWidth) / 100;
            string bar = "[" + new string('#', filled) + new string('-', Math.Max(0, barWidth - filled)) + $"] {_progressPercent}%";
            win.WriteRow(7, "  " + bar, ConsoleColor.Green, 2, fillRemaining: true);

            win.WriteSeparator(10);
            win.WriteRow(11, "Do not power off or disconnect the machine during install.", ConsoleColor.DarkGray, 2, fillRemaining: true);
            win.WriteFooter("Installation in progress... Please wait.", ConsoleColor.Yellow);

            // Trigger installation worker on first frame
            if (!_installationTriggered)
            {
                _installationTriggered = true;
                _lastDrawnPercent = _progressPercent;
                ExecuteInstall(desktop, win);
            }
        }

        private void ExecuteInstall(TuiDesktop desktop, TuiWindow win)
        {
            bool ok = UpdateManagerService.InstallToDisk(_selectedDriveIndex, (msg, pct) =>
            {
                if (pct != _lastDrawnPercent)
                {
                    _lastDrawnPercent = pct;
                    _progressPercent = pct;
                    _progressText = msg;

                    // Update in-place on existing window with zero allocations
                    string prog = msg ?? "";
                    if (prog.Length > 60) prog = prog.Substring(0, 60);
                    win.WriteRow(5, "  " + prog, ConsoleColor.White, 2, fillRemaining: true);

                    int barWidth = 46;
                    int filled = (pct * barWidth) / 100;
                    string bar = "[" + new string('#', filled) + new string('-', Math.Max(0, barWidth - filled)) + $"] {pct}%";
                    win.WriteRow(7, "  " + bar, ConsoleColor.Green, 2, fillRemaining: true);
                }
            }, out _statusResult);

            if (ok)
            {
                _step = InstallerStep.Complete;
                DrawComplete(desktop);
            }
            else
            {
                _step = InstallerStep.Error;
                DrawError(desktop);
            }
        }

        private void DrawComplete(TuiDesktop desktop)
        {
            var win = desktop.CreateWindow("INSTALLATION COMPLETED SUCCESSFULLY", WindowSize.FullScreen);

            win.WriteRow(0, "[SUCCESS] WEATHEROS HAS BEEN INSTALLED TO YOUR HARD DISK!", ConsoleColor.Green, 2, fillRemaining: true);
            win.WriteRow(1, "The bootable operating system environment is now permanent.", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteSeparator(2);

            win.WriteRow(3, "SYSTEM SUMMARY:", ConsoleColor.Yellow, 2, fillRemaining: true);
            win.WriteRow(4,  "  Boot Target  : 0:\\boot\\WeatherOS.bin [ACTIVE]", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteRow(5,  "  File System  : FAT32 (WEATHEROS) on Local Drive", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteRow(6,  "  User Storage : 0:\\users\\ and root documents", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteRow(7,  "  System State : Ready for permanent boot & OTA in-place updates", ConsoleColor.Green, 2, fillRemaining: true);
            win.WriteSeparator(8);

            win.WriteRow(9,  "HOW TO UPDATE IN THE FUTURE:", ConsoleColor.Yellow, 2, fillRemaining: true);
            win.WriteRow(10, " - Transfer new builds (.bin / .pkg) via WeatherTransfer.", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteRow(11, " - System Information replaces WeatherOS.bin seamlessly.", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteRow(12, " - Your personal files and configs will NEVER be erased!", ConsoleColor.Green, 2, fillRemaining: true);
            win.WriteSeparator(13);

            win.WriteFooter("[R / ENTER] Reboot to Installed OS   |   [ESC] Return to Desktop", ConsoleColor.Yellow);
        }

        private void DrawError(TuiDesktop desktop)
        {
            var win = desktop.CreateWindow("INSTALLATION FAILED  |  ERROR", WindowSize.FullScreen);

            win.WriteRow(0, "[ERROR] FAILED TO INSTALL WEATHEROS TO DISK", ConsoleColor.Red, 2, fillRemaining: true);
            win.WriteRow(1, "An error occurred during partition formatting or file copying:", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteSeparator(2);

            win.WriteRow(4, "DETAILS:", ConsoleColor.Yellow, 2, fillRemaining: true);
            string status = _statusResult ?? "";
            if (status.Length > 60) status = status.Substring(0, 60);
            win.WriteRow(5, "  " + status, ConsoleColor.Red, 2, fillRemaining: true);
            win.WriteSeparator(8);

            win.WriteRow(10, "Your existing session in RAM has not been affected.", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteRow(11, "You can retry installation or continue using Live OS mode.", ConsoleColor.Gray, 2, fillRemaining: true);

            win.WriteFooter("[R] Retry Installation   |   [ESC] Return to Desktop", ConsoleColor.Yellow);
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            switch (_step)
            {
                case InstallerStep.SelectDisk:
                    HandleSelectDiskInput(desktop, keyEvent);
                    break;
                case InstallerStep.ConfirmWarning:
                    HandleConfirmWarningInput(desktop, keyEvent);
                    break;
                case InstallerStep.Complete:
                    HandleCompleteInput(desktop, keyEvent);
                    break;
                case InstallerStep.Error:
                    HandleErrorInput(desktop, keyEvent);
                    break;
            }
        }

        private void HandleSelectDiskInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                desktop.SetCurrentApp("launcher");
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.R || keyEvent.KeyChar == 'r' || keyEvent.KeyChar == 'R')
            {
                RefreshDrives();
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.UpArrow)
            {
                if (_selectedDriveIndex > 0) _selectedDriveIndex--;
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.DownArrow)
            {
                if (_availableDrives != null && _selectedDriveIndex < _availableDrives.Count - 1)
                {
                    _selectedDriveIndex++;
                }
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.Enter)
            {
                if (_availableDrives != null && _availableDrives.Count > 0)
                {
                    _step = InstallerStep.ConfirmWarning;
                }
            }
        }

        private void HandleConfirmWarningInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Escape || keyEvent.KeyChar == 'n' || keyEvent.KeyChar == 'N')
            {
                _step = InstallerStep.SelectDisk;
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.Enter || keyEvent.KeyChar == 'y' || keyEvent.KeyChar == 'Y')
            {
                var d = (_availableDrives != null && _selectedDriveIndex < _availableDrives.Count)
                    ? _availableDrives[_selectedDriveIndex]
                    : null;
                bool isUsbTarget = d != null && (d.Name.Contains("Live USB") || d.Name.Contains("Boot Medium") || d.Name.Contains("Boot Drive"));
                if (isUsbTarget)
                {
                    _step = InstallerStep.SelectDisk;
                    return;
                }

                _step = InstallerStep.Installing;
                _progressPercent = 5;
                _progressText = "Initializing target storage device...";
                _installationTriggered = false;
            }
        }

        private void HandleCompleteInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Enter || keyEvent.Key == ConsoleKeyEx.R || keyEvent.KeyChar == 'r' || keyEvent.KeyChar == 'R')
            {
                Power.Reboot();
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                desktop.SetCurrentApp("launcher");
            }
        }

        private void HandleErrorInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.R || keyEvent.KeyChar == 'r' || keyEvent.KeyChar == 'R')
            {
                _step = InstallerStep.SelectDisk;
                _installationTriggered = false;
                _progressPercent = 0;
                _progressText = "Preparing installation...";
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                desktop.SetCurrentApp("launcher");
            }
        }
    }
}
