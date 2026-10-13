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
        public bool IsRealTime => true;

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
        }

        private void DrawSelectDisk(TuiDesktop desktop)
        {
            if (_availableDrives == null) RefreshDrives();

            var win = desktop.CreateWindow("WEATHEROS HARD DISK INSTALLER  |  SELECT TARGET DRIVE", WindowSize.Large);

            win.WriteRow(0, "STEP 1: SELECT PHYSICAL DISK FOR INSTALLATION", ConsoleColor.Cyan, 2, fillRemaining: true);
            win.WriteRow(1, "WeatherOS will be installed as the primary operating system on this drive.", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteSeparator(2);

            win.WriteRow(3, "DETECTED STORAGE CONTROLLERS & DRIVES:", ConsoleColor.Yellow, 2, fillRemaining: true);

            if (_availableDrives.Count == 0)
            {
                win.WriteRow(5, "[!] No physical hard drives detected. Check IDE/SATA/AHCI controller.", ConsoleColor.Red, 2, fillRemaining: true);
                win.WriteRow(6, "You can continue running WeatherOS in Live OS (RAM) mode.", ConsoleColor.Gray, 2, fillRemaining: true);
            }
            else
            {
                for (int i = 0; i < _availableDrives.Count && i < 5; i++)
                {
                    var d = _availableDrives[i];
                    bool isSel = (i == _selectedDriveIndex);
                    bool isUsb = d.Name.Contains("Live USB") || d.Name.Contains("Boot Medium");
                    string tag = isUsb ? "[BOOT PEN DRIVE]" : "[INTERNAL DISK - RECOMMENDED]";
                    string line = $"[{i}] {d.Name}  |  {d.SizeDisplay}  {tag}";
                    win.WriteSelectionRow(5 + (i * 2), line, isSel, 2);
                }
            }

            win.WriteSeparator(12);
            win.WriteRow(13, "[i] TIP: Choose your internal hard disk (Disk 0) to boot without the pen drive.", ConsoleColor.DarkCyan, 2, fillRemaining: true);
            win.WriteRow(14, "    Future updates are applied in-place to this drive without wiping data!", ConsoleColor.DarkCyan, 2, fillRemaining: true);

            win.WriteFooter("[Up/Dn] Select Drive   |   [ENTER] Next   |   [R] Refresh   |   [ESC] Exit", ConsoleColor.Yellow);
        }

        private void DrawConfirmWarning(TuiDesktop desktop)
        {
            var win = desktop.CreateWindow("INSTALLATION CONFIRMATION  |  WARNING", WindowSize.Large);

            var d = (_availableDrives != null && _selectedDriveIndex < _availableDrives.Count)
                ? _availableDrives[_selectedDriveIndex]
                : null;

            string targetName = d != null ? d.Name : "Target Drive";
            string targetSize = d != null ? d.SizeDisplay : "Unknown";
            bool isUsbTarget = d != null && (d.Name.Contains("Live USB") || d.Name.Contains("Boot Medium"));

            win.WriteRow(0, "STEP 2: CONFIRM PARTITION INITIALIZATION", ConsoleColor.Red, 2, fillRemaining: true);
            win.WriteRow(1, "Review target drive parameters before proceeding with installation:", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteSeparator(2);

            win.WriteRow(3, $"  Target Drive : {targetName}", ConsoleColor.Yellow, 2, fillRemaining: true);
            win.WriteRow(4, $"  Capacity     : {targetSize}", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteRow(5,  "  File System  : FAT32 LBA Partition (Volume: WEATHEROS)", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteRow(6,  "  Boot File    : 0:\\boot\\WeatherOS.bin (x86 MBR Bootloader)", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteSeparator(7);

            if (isUsbTarget)
            {
                win.WriteRow(8, "[!] CAUTION: You have selected the Live USB Pen Drive!", ConsoleColor.Red, 2, fillRemaining: true);
                win.WriteRow(9, "To install permanently to your computer, press [ESC] and choose", ConsoleColor.Yellow, 2, fillRemaining: true);
                win.WriteRow(10, "the internal Hard Disk (Disk 0) instead of the pen drive.", ConsoleColor.Yellow, 2, fillRemaining: true);
                win.WriteRow(11, "If you proceed, this will format the Live USB pen drive.", ConsoleColor.Red, 2, fillRemaining: true);
            }
            else
            {
                win.WriteRow(8, "[!] NOTICE: INITIAL HARD DISK INSTALLATION & BOOTLOADER SETUP:", ConsoleColor.Yellow, 2, fillRemaining: true);
                win.WriteRow(9, " - Partition 1 will be initialized as FAT32 on this internal disk.", ConsoleColor.White, 2, fillRemaining: true);
                win.WriteRow(10, " - Executable x86 MBR bootloader written to Sector 0 for PC BIOS boot.", ConsoleColor.Green, 2, fillRemaining: true);
                win.WriteRow(11, " - Subsequent updates will NEVER wipe this disk! Personal files preserved.", ConsoleColor.Green, 2, fillRemaining: true);
            }
            win.WriteSeparator(12);

            win.WriteRow(13, "Press [Y] or [ENTER] to Proceed with Installation   |   [N / ESC] Cancel", ConsoleColor.Yellow, 2, fillRemaining: true);
            win.WriteFooter("[Y / ENTER] Confirm Install   |   [N / ESC] Cancel", ConsoleColor.Yellow);
        }

        private void DrawInstalling(TuiDesktop desktop)
        {
            var win = desktop.CreateWindow("INSTALLING WEATHEROS TO DISK  |  PLEASE WAIT", WindowSize.Large);

            win.WriteRow(0, "STEP 3: DEPLOYING SYSTEM FILES & BOOTLOADER", ConsoleColor.Cyan, 2, fillRemaining: true);
            win.WriteRow(1, "Writing operating system structures to physical disk...", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteSeparator(2);

            win.WriteRow(4, "CURRENT OPERATION:", ConsoleColor.Yellow, 2, fillRemaining: true);
            win.WriteRow(5, "  " + _progressText, ConsoleColor.White, 2, fillRemaining: true);

            // Progress bar
            int barWidth = win.InnerWidth - 8;
            if (barWidth < 20) barWidth = 40;
            int filled = (_progressPercent * barWidth) / 100;
            string bar = "[" + new string('#', filled) + new string('-', Math.Max(0, barWidth - filled)) + $"] {_progressPercent}%";
            win.WriteRow(7, "  " + bar, ConsoleColor.Green, 2, fillRemaining: true);

            win.WriteSeparator(10);
            win.WriteRow(11, "Do not power off or disconnect the machine during installation.", ConsoleColor.DarkGray, 2, fillRemaining: true);
            win.WriteFooter("Installation in progress... Please wait.", ConsoleColor.Yellow);

            // Trigger installation worker on first frame
            if (!_installationTriggered)
            {
                _installationTriggered = true;
                ExecuteInstall();
            }
        }

        private void ExecuteInstall()
        {
            bool ok = UpdateManagerService.InstallToDisk(_selectedDriveIndex, (msg, pct) =>
            {
                _progressText = msg;
                _progressPercent = pct;
            }, out _statusResult);

            if (ok)
            {
                _step = InstallerStep.Complete;
            }
            else
            {
                _step = InstallerStep.Error;
            }
        }

        private void DrawComplete(TuiDesktop desktop)
        {
            var win = desktop.CreateWindow("INSTALLATION COMPLETED SUCCESSFULLY", WindowSize.Large);

            win.WriteRow(0, "[SUCCESS] WEATHEROS HAS BEEN INSTALLED TO YOUR HARD DISK!", ConsoleColor.Green, 2, fillRemaining: true);
            win.WriteRow(1, "The bootable operating system environment is now permanent.", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteSeparator(2);

            win.WriteRow(3, "SYSTEM SUMMARY:", ConsoleColor.Yellow, 2, fillRemaining: true);
            win.WriteRow(4, $"  Boot Target  : 0:\\boot\\WeatherOS.bin [ACTIVE]", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteRow(5, $"  File System  : FAT32 (WEATHEROS) on Local Drive", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteRow(6, $"  User Storage : 0:\\users\\ and 0:\\ root documents", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteRow(7, $"  System State : Ready for permanent boot and OTA in-place updates", ConsoleColor.Green, 2, fillRemaining: true);
            win.WriteSeparator(8);

            win.WriteRow(9,  "HOW TO UPDATE IN THE FUTURE:", ConsoleColor.Yellow, 2, fillRemaining: true);
            win.WriteRow(10, " - Transfer new builds (.bin / .pkg) via WeatherTransfer or file picker.", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteRow(11, " - System Information will replace 0:\\boot\\WeatherOS.bin seamlessly.", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteRow(12, " - Your personal files and configs will NEVER be erased!", ConsoleColor.Green, 2, fillRemaining: true);
            win.WriteSeparator(13);

            win.WriteFooter("[R / ENTER] Reboot Now to Installed OS   |   [ESC] Return to Desktop", ConsoleColor.Yellow);
        }

        private void DrawError(TuiDesktop desktop)
        {
            var win = desktop.CreateWindow("INSTALLATION FAILED  |  ERROR", WindowSize.Large);

            win.WriteRow(0, "[ERROR] FAILED TO INSTALL WEATHEROS TO DISK", ConsoleColor.Red, 2, fillRemaining: true);
            win.WriteRow(1, "An error occurred during partition formatting or file copying:", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteSeparator(2);

            win.WriteRow(4, "DETAILS:", ConsoleColor.Yellow, 2, fillRemaining: true);
            win.WriteRow(5, "  " + _statusResult, ConsoleColor.Red, 2, fillRemaining: true);
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
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                desktop.SetCurrentApp("launcher");
            }
        }
    }
}
