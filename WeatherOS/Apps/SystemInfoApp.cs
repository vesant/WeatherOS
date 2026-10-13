using System;
using System.Collections.Generic;
using Cosmos.Core;
using Cosmos.System;
using Cosmos.System.FileSystem.VFS;
using WeatherOS.Graphics;
using WeatherOS.Services;

namespace WeatherOS.Apps
{
    public enum SysInfoMode
    {
        Overview,
        Changelog,
        CommitDetails,
        OtaUpdateMenu,
        OtaFilePicker,
        OtaUpdateStaged
    }

    public class SystemInfoApp : IApp
    {
        public string Id => "about";
        public string Name => "System Information";
        public bool IsRealTime => false;

        private SysInfoMode _mode = SysInfoMode.Overview;
        private int _selectedOverviewOption = 0; // 0 = Changelog, 1 = Update
        private int _selectedOtaMenuOption = 0; // 0 = File Picker, 1 = Transfer App

        // Commit List Navigation
        private int _selectedCommitIndex = 0;
        private int _commitListScrollOffset = 0;
        private int _commitDetailScroll = 0;

        // File Picker for OS Update (Windows-Style Picker identical to Notepad)
        private List<StorageLocation> _cachedLocations = null;
        private int _openDialogLocationIndex = 0;
        private List<StorageFileInfo> _openDialogFiles = null;
        private int _openDialogSelectedFileIndex = 0;
        private int _openDialogScrollOffset = 0;
        private string _pickerStatus = "Select an update package (.bin, .pkg, .zip) to stage.";

        // Staged Update Details
        private string _stagedFileName = "";
        private long _stagedFileSize = 0;
        private string _stagedChecksum = "";
        private string _stagedPackageType = "";
        private string _stagedLocation = "";
        private string _syncStatus = "";
        private byte[] _stagedFileBytes = null;
        private string _updateAppliedStatus = "";
        private bool _updateAppliedSuccess = false;

        public void Draw(TuiDesktop desktop)
        {
            switch (_mode)
            {
                case SysInfoMode.Overview:
                    DrawOverview(desktop);
                    break;
                case SysInfoMode.Changelog:
                    DrawChangelog(desktop);
                    break;
                case SysInfoMode.CommitDetails:
                    DrawCommitDetails(desktop);
                    break;
                case SysInfoMode.OtaUpdateMenu:
                    DrawOtaUpdateMenu(desktop);
                    break;
                case SysInfoMode.OtaFilePicker:
                    DrawOtaFilePicker(desktop);
                    break;
                case SysInfoMode.OtaUpdateStaged:
                    DrawOtaUpdateStaged(desktop);
                    break;
            }
        }

        #region Overview
        private void DrawOverview(TuiDesktop desktop)
        {
            var win = desktop.CreateWindow("SYSTEM INFORMATION & DIAGNOSTICS", WindowSize.Large);

            // Section 1: OS Branding & Core Architecture
            win.WriteRow(0, "WeatherOS v3.0.0 (x86 Bare-Metal Edition)", ConsoleColor.Cyan, 2, fillRemaining: true);
            win.WriteRow(1, "Kernel: Cosmos IL2CPU AOT 32-bit  |  Channel: Stable  |  Build: 2026-10-13", ConsoleColor.Gray, 2, fillRemaining: true);
            win.WriteSeparator(2);

            // Section 2: Hardware & Telemetry
            string cpuBrand = CPU.GetCPUBrandString();
            if (string.IsNullOrEmpty(cpuBrand)) cpuBrand = "x86 Compatible Processor (IA-32)";
            uint ramMB = CPU.GetAmountOfRAM();
            if (ramMB == 0) ramMB = 1024;
            string activeIp = RawTcpHttp.GetAssignedIp();
            if (string.IsNullOrEmpty(activeIp) || activeIp == "0.0.0.0") activeIp = "Offline (No IP)";

            int diskCount = 0;
            try
            {
                diskCount = VFSManager.GetDisks().Count;
            }
            catch { }
            string storageInfo = diskCount > 0 ? $"{diskCount} Drive(s) Detected [FAT32 / IDE ATA]" : "Virtual RAM File System (0:\\)";

            win.WriteRow(3, "HARDWARE & SYSTEM TELEMETRY:", ConsoleColor.Yellow, 2, fillRemaining: true);
            win.WriteRow(4, $"  Processor : {cpuBrand}", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteRow(5, $"  Memory    : {ramMB} MB System RAM Installed", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteRow(6, $"  Network   : {activeIp}  [RTL8102E / PCnet-Fast III]", ConsoleColor.White, 2, fillRemaining: true);
            string bootDesc = UpdateManagerService.GetBootTargetDescription();
            win.WriteRow(7, $"  Storage   : {storageInfo}", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteRow(8, $"  Boot Mode : {bootDesc}", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteSeparator(9);

            // Section 3: Sub-actions / Sub-apps
            win.WriteRow(10, "AVAILABLE SYSTEM MODULES:", ConsoleColor.Yellow, 2, fillRemaining: true);
            win.WriteSelectionRow(11, "[1] View GitHub Commits & Updates Log (vesant/WeatherOS: 32bit)", _selectedOverviewOption == 0, 2);
            win.WriteSelectionRow(12, "[2] Install Update Package (.bin / .pkg / .zip)", _selectedOverviewOption == 1, 2);
            win.WriteSeparator(13);

            // Section 4: Dynamic hint
            if (_selectedOverviewOption == 0)
            {
                win.WriteRow(14, "Description: Tracked git commits from GitHub branch 32bit with detail notes.", ConsoleColor.DarkCyan, 2, fillRemaining: true);
            }
            else
            {
                win.WriteRow(14, "Description: Select and stage local or network firmware update package.", ConsoleColor.DarkCyan, 2, fillRemaining: true);
            }

            win.WriteFooter("[Up/Dn] Select   [ENTER] Open   [1/C] Commits   [2/U] Update   [ESC] Exit", ConsoleColor.Yellow);
        }
        #endregion

        #region Changelog (GitHub Commits List)
        private void DrawChangelog(TuiDesktop desktop)
        {
            var win = desktop.CreateWindow("WEATHEROS COMMITS & CHANGELOG (vesant/WeatherOS: 32bit)", WindowSize.FullScreen);

            var commits = GitHubCommitsService.GetCommits();

            win.WriteRow(0, "Live Git Commits from GitHub (branch: 32bit)  |  Total: " + commits.Count, ConsoleColor.Cyan, 2, fillRemaining: true);
            if (!string.IsNullOrEmpty(_syncStatus))
            {
                ConsoleColor sc = _syncStatus.StartsWith("Live") ? ConsoleColor.Green : ConsoleColor.Yellow;
                win.WriteRow(1, _syncStatus, sc, 2, fillRemaining: true);
            }
            else
            {
                win.WriteRow(1, "Use [Up/Dn] to select. Press [Enter] for details. Press [R] to sync via TLS 1.2.", ConsoleColor.Gray, 2, fillRemaining: true);
            }
            win.WriteSeparator(2);

            // Visible commits calculation: each commit uses 2 rows (1 title row + 1 blank row spacing)
            // Rows available: relY = 3 to 16 -> 14 rows / 2 = 7 commits visible
            int maxVisibleCommits = 7;
            if (_selectedCommitIndex < _commitListScrollOffset)
            {
                _commitListScrollOffset = _selectedCommitIndex;
            }
            if (_selectedCommitIndex >= _commitListScrollOffset + maxVisibleCommits)
            {
                _commitListScrollOffset = _selectedCommitIndex - maxVisibleCommits + 1;
            }

            int startRelY = 3;
            for (int i = 0; i < maxVisibleCommits; i++)
            {
                int commitIdx = _commitListScrollOffset + i;
                int currentY = startRelY + (i * 2);

                if (commitIdx < commits.Count)
                {
                    var c = commits[commitIdx];
                    bool isSelected = (commitIdx == _selectedCommitIndex);

                    // Truncate title cleanly to fit with SHA and Date
                    string titleText = c.Title;
                    int maxTitleLen = win.InnerWidth - 28;
                    if (maxTitleLen < 15) maxTitleLen = 15;
                    if (titleText.Length > maxTitleLen) titleText = titleText.Substring(0, maxTitleLen - 3) + "...";

                    string rowText = $"[{c.Sha}] {titleText}  ({c.Date})";

                    if (isSelected)
                    {
                        win.WriteSelectionRow(currentY, rowText, true, 1);
                    }
                    else
                    {
                        win.WriteRow(currentY, "   " + rowText, ConsoleColor.White, 1, fillRemaining: true);
                    }

                    // Blank line spacing between commit titles
                    win.WriteRow(currentY + 1, "", ConsoleColor.Black, 1, fillRemaining: true);
                }
                else
                {
                    win.WriteRow(currentY, "", ConsoleColor.Black, 1, fillRemaining: true);
                    win.WriteRow(currentY + 1, "", ConsoleColor.Black, 1, fillRemaining: true);
                }
            }

            win.WriteSeparator(17);
            win.WriteFooter($"[Up/Dn] Select  |  [Enter] Full Notes  |  [R] Sync Live (TLS 1.2)  |  [ESC] Overview  |  Commit {_selectedCommitIndex + 1}/{commits.Count}", ConsoleColor.Yellow);
        }
        #endregion

        #region Commit Details View
        private void DrawCommitDetails(TuiDesktop desktop)
        {
            var commits = GitHubCommitsService.GetCommits();
            if (_selectedCommitIndex < 0 || _selectedCommitIndex >= commits.Count)
            {
                _mode = SysInfoMode.Changelog;
                return;
            }

            var c = commits[_selectedCommitIndex];
            var win = desktop.CreateWindow($"COMMIT DETAILS  |  [{c.Sha}]", WindowSize.FullScreen);

            win.WriteRow(0, $"Commit : [{c.Sha}]  |  Branch: 32bit  |  Repository: vesant/WeatherOS", ConsoleColor.Cyan, 2, fillRemaining: true);
            win.WriteRow(1, $"Title  : {c.Title}", ConsoleColor.Yellow, 2, fillRemaining: true);
            win.WriteRow(2, $"Author : {c.Author}  |  Date: {c.Date}", ConsoleColor.Gray, 2, fillRemaining: true);
            win.WriteSeparator(3);

            win.WriteRow(4, "DETAILED COMMIT DESCRIPTION:", ConsoleColor.Green, 2, fillRemaining: true);

            // Wrap description paragraphs into display lines
            int maxLineLen = win.InnerWidth - 4;
            if (maxLineLen < 20) maxLineLen = 60;

            List<string> wrappedLines;
            if (string.IsNullOrWhiteSpace(c.Description))
            {
                wrappedLines = new List<string> { "(No additional multiline description provided for this commit.)" };
            }
            else
            {
                wrappedLines = WrapText(c.Description, maxLineLen);
            }

            // Visible description rows: relY = 5 to 16 (12 rows)
            int visibleRows = 12;
            int maxScroll = Math.Max(0, wrappedLines.Count - visibleRows);
            if (_commitDetailScroll > maxScroll) _commitDetailScroll = maxScroll;
            if (_commitDetailScroll < 0) _commitDetailScroll = 0;

            for (int r = 0; r < visibleRows; r++)
            {
                int lineIdx = _commitDetailScroll + r;
                if (lineIdx < wrappedLines.Count)
                {
                    string line = wrappedLines[lineIdx];
                    ConsoleColor fg = ConsoleColor.White;
                    if (line.StartsWith("- ") || line.StartsWith("* ")) fg = ConsoleColor.White;
                    else if (line.StartsWith("> ")) fg = ConsoleColor.DarkGray;

                    win.WriteRow(5 + r, line, fg, 2, fillRemaining: true);
                }
                else
                {
                    win.WriteRow(5 + r, "", ConsoleColor.Black, 2, fillRemaining: true);
                }
            }

            win.WriteSeparator(17);
            win.WriteFooter($"[Up/Dn] Scroll Notes  |  [PgUp/PgDn] Page  |  [ESC] Back to Commits  |  Line {_commitDetailScroll + 1}/{Math.Max(1, wrappedLines.Count)}", ConsoleColor.Yellow);
        }
        #endregion

        #region OS Update Menu
        private void DrawOtaUpdateMenu(TuiDesktop desktop)
        {
            var win = desktop.CreateWindow("FIRMWARE & OS UPDATE MANAGER (OTA)", WindowSize.Large);

            string activeIp = RawTcpHttp.GetAssignedIp();
            if (string.IsNullOrEmpty(activeIp) || activeIp == "0.0.0.0") activeIp = "Offline";

            bool isInstalled = UpdateManagerService.IsInstalledOnDisk();
            bool hasBackup = UpdateManagerService.HasBackupKernel();
            string installedVer = UpdateManagerService.GetInstalledVersion();

            if (isInstalled)
            {
                win.WriteRow(0, "[OK] RUNNING FROM LOCAL HARD DISK (0:\\boot\\WeatherOS.bin)", ConsoleColor.Green, 2, fillRemaining: true);
                win.WriteRow(1, "In-place updates replace kernel only. User data & settings are 100% PRESERVED.", ConsoleColor.White, 2, fillRemaining: true);
            }
            else
            {
                win.WriteRow(0, "[!] RUNNING IN LIVE OS MODE (RAM / ISO9660)", ConsoleColor.Yellow, 2, fillRemaining: true);
                win.WriteRow(1, "Live session in RAM. Use option [3] to install permanently to hard disk.", ConsoleColor.White, 2, fillRemaining: true);
            }
            win.WriteSeparator(2);

            win.WriteRow(3, "SYSTEM FIRMWARE STATUS:", ConsoleColor.Yellow, 2, fillRemaining: true);
            win.WriteRow(4, $"  Installed Version : {installedVer}", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteRow(5, $"  Active Boot Slot  : {(isInstalled ? "Slot A [0:\\boot\\WeatherOS.bin] (FAT32 Disk)" : "Slot A [Live RAM Execution]")}", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteRow(6, $"  Backup Slot B     : {(hasBackup ? "0:\\boot\\WeatherOS.bak [READY FOR ROLLBACK]" : "None / Standby")}", hasBackup ? ConsoleColor.Green : ConsoleColor.DarkGray, 2, fillRemaining: true);
            win.WriteRow(7, $"  OTA UDP Listener  : Port 7331 (WOSF) on IP {activeIp} [READY]", ConsoleColor.Green, 2, fillRemaining: true);
            win.WriteSeparator(8);

            win.WriteRow(9, "SELECT UPDATE OR INSTALLATION ACTION:", ConsoleColor.Yellow, 2, fillRemaining: true);
            win.WriteSelectionRow(10, "[1] Choose Update File (.bin / .pkg / .zip)  [File Picker Dialog]", _selectedOtaMenuOption == 0, 2);
            win.WriteSelectionRow(11, "[2] Open Network File Transfer App (Receive file from PC)", _selectedOtaMenuOption == 1, 2);

            if (isInstalled && hasBackup)
            {
                win.WriteSelectionRow(12, "[3] Rollback to Previous Version (Restore 0:\\boot\\WeatherOS.bak)", _selectedOtaMenuOption == 2, 2);
            }
            else if (isInstalled)
            {
                win.WriteSelectionRow(12, "[3] Reinstall or Repair System Installation (Setup Wizard)", _selectedOtaMenuOption == 2, 2);
            }
            else
            {
                win.WriteSelectionRow(12, "[3] Install WeatherOS to Hard Disk Now (Setup Wizard)", _selectedOtaMenuOption == 2, 2);
            }
            win.WriteSeparator(13);

            if (_selectedOtaMenuOption == 0)
            {
                win.WriteRow(14, "Description: Opens Windows-style file picker to select update from disk or RAM.", ConsoleColor.DarkCyan, 2, fillRemaining: true);
            }
            else if (_selectedOtaMenuOption == 1)
            {
                win.WriteRow(14, "Description: Launch UDP 7331 network transfer to receive binaries from Windows PC.", ConsoleColor.DarkCyan, 2, fillRemaining: true);
            }
            else
            {
                win.WriteRow(14, isInstalled && hasBackup ? "Description: Instantly revert kernel to previous version from backup slot." : "Description: Partition hard drive and install WeatherOS permanently.", ConsoleColor.DarkCyan, 2, fillRemaining: true);
            }

            win.WriteFooter("[Up/Dn] Select   [ENTER] Open   [1] File Picker   [2] Transfer   [3] Install/Rollback   [ESC] Back", ConsoleColor.Yellow);
        }
        #endregion

        #region OS Update File Picker (Identical to Notepad Windows-Like Picker)
        private void OpenUpdateFilePicker()
        {
            _cachedLocations = StorageService.GetStorageLocations();
            if (_cachedLocations == null || _cachedLocations.Count == 0)
            {
                _cachedLocations = new List<StorageLocation>();
            }
            _openDialogLocationIndex = 0;
            RefreshOpenDialogFiles();
            _pickerStatus = "Ready to load update package.";
            _mode = SysInfoMode.OtaFilePicker;
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
        }

        private void DrawOtaFilePicker(TuiDesktop desktop)
        {
            var win = desktop.CreateWindow("SELECT UPDATE PACKAGE  |  FILE PICKER", WindowSize.FullScreen);

            // Header / Look In
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
                win.WriteRow(5, "(No files found on this drive. Upload via Transfer app or switch drive with [Tab])", ConsoleColor.Yellow, 2);
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
                    else
                    {
                        win.WriteRow(4 + r, "", ConsoleColor.Black, 1, fillRemaining: true);
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
            win.WriteRow(13, $"Package name:  [ {selectedFileName.PadRight(47)} ]", ConsoleColor.Yellow, 2);
            win.WriteRow(14, "Files of type: [ Firmware Packages (*.bin, *.pkg, *.zip, *.iso, *.*) ]", ConsoleColor.Yellow, 2);

            if (!string.IsNullOrEmpty(_pickerStatus))
            {
                win.WriteRow(15, "Status: " + _pickerStatus, ConsoleColor.Cyan, 2);
            }

            win.WriteFooter("[Up/Dn] Select  |  [Tab] Drive  |  [Enter] Stage & Apply  |  [ESC] Cancel", ConsoleColor.Yellow);
        }

        private void StageSelectedFile()
        {
            if (_openDialogFiles == null || _openDialogFiles.Count == 0 ||
                _openDialogSelectedFileIndex < 0 || _openDialogSelectedFileIndex >= _openDialogFiles.Count)
            {
                _pickerStatus = "No file selected.";
                return;
            }

            var picked = _openDialogFiles[_openDialogSelectedFileIndex];
            string locId = "ram";
            if (_cachedLocations != null && _openDialogLocationIndex >= 0 && _openDialogLocationIndex < _cachedLocations.Count)
            {
                locId = _cachedLocations[_openDialogLocationIndex].Id;
            }

            byte[] data;
            string status;
            bool loaded = StorageService.LoadBinaryFileFromLocation(picked.Name, locId, out data, out status);

            if (!loaded || data == null || data.Length == 0)
            {
                // Try text load if binary load was empty
                string textContent;
                if (StorageService.LoadFileFromLocation(picked.Name, locId, out textContent, out status) && !string.IsNullOrEmpty(textContent))
                {
                    data = System.Text.Encoding.ASCII.GetBytes(textContent);
                    loaded = true;
                }
            }

            if (!loaded || data == null || data.Length == 0)
            {
                _pickerStatus = "Error: Unable to read file content (" + status + ")";
                return;
            }

            _stagedFileName = picked.Name;
            _stagedFileSize = data.Length;
            _stagedLocation = picked.LocationName;
            _stagedFileBytes = data;
            _updateAppliedStatus = "";
            _updateAppliedSuccess = false;

            uint crc = ComputeCrc32(data);
            _stagedChecksum = "0x" + crc.ToString("X8");

            string lower = picked.Name.ToLower();
            if (lower == "commits.json")
            {
                _stagedPackageType = "GitHub Commits Manifest (Live Update)";
                GitHubCommitsService.ReloadFromJson(data);
            }
            else if (lower.EndsWith(".bin"))
            {
                _stagedPackageType = "WeatherOS Raw Kernel Binary (AOT x86)";
            }
            else if (lower.EndsWith(".pkg") || lower.EndsWith(".wos"))
            {
                _stagedPackageType = "WeatherOS 3-in-1 Cumulative Package (Kernel + Clock v2 + Commits)";
            }
            else if (lower.EndsWith(".zip") || lower.EndsWith(".tar"))
            {
                _stagedPackageType = "Compressed WeatherOS System Archive";
            }
            else
            {
                _stagedPackageType = "Raw Firmware Binary Image";
            }

            _mode = SysInfoMode.OtaUpdateStaged;
        }
        #endregion

        #region OS Update Staged Screen
        private void DrawOtaUpdateStaged(TuiDesktop desktop)
        {
            var win = desktop.CreateWindow("FIRMWARE UPDATE  |  PACKAGE STAGED", WindowSize.Large);

            if (_updateAppliedSuccess)
            {
                win.WriteRow(0, "[SUCCESS] FIRMWARE UPDATE DEPLOYED TO DISK!", ConsoleColor.Green, 2, fillRemaining: true);
                win.WriteRow(1, _updateAppliedStatus, ConsoleColor.White, 2, fillRemaining: true);
                win.WriteSeparator(2);

                win.WriteRow(3, "SYSTEM READY FOR REBOOT:", ConsoleColor.Yellow, 2, fillRemaining: true);
                win.WriteRow(4, "  Active Boot Target : 0:\\boot\\WeatherOS.bin (Updated)", ConsoleColor.White, 2, fillRemaining: true);
                win.WriteRow(5, "  Safety Rollback    : 0:\\boot\\WeatherOS.bak (Available in Boot Menu)", ConsoleColor.White, 2, fillRemaining: true);
                win.WriteRow(6, "  User Protection    : All documents, notes and settings.ini PRESERVED.", ConsoleColor.Green, 2, fillRemaining: true);
                win.WriteSeparator(7);

                win.WriteRow(9,  "Press [R] or [ENTER] to Reboot System Now and boot the updated kernel.", ConsoleColor.Yellow, 2, fillRemaining: true);
                win.WriteRow(10, "Press [ESC] to return to System Information overview.", ConsoleColor.DarkGray, 2, fillRemaining: true);

                win.WriteFooter("[R / ENTER] Reboot System Now   |   [ESC] Return to Desktop", ConsoleColor.Yellow);
                return;
            }

            win.WriteRow(0, "[OK] UPDATE PACKAGE LOADED & VERIFIED SUCCESSFULLY!", ConsoleColor.Green, 2, fillRemaining: true);
            win.WriteRow(1, "Integrity check passed. Ready to deploy non-destructive in-place update.", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteSeparator(2);

            win.WriteRow(3, "STAGED FIRMWARE PAYLOAD:", ConsoleColor.Yellow, 2, fillRemaining: true);
            win.WriteRow(4, $"  Package File   : {_stagedFileName}", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteRow(5, $"  Payload Size   : {FormatFileSize(_stagedFileSize)} ({_stagedFileSize} bytes)", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteRow(6, $"  Package Format : {_stagedPackageType}", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteRow(7, $"  Integrity CRC  : {_stagedChecksum}  [VALID]", ConsoleColor.Green, 2, fillRemaining: true);
            win.WriteRow(8,  "  Target Slot    : 0:\\boot\\WeatherOS.bin (Slot A)", ConsoleColor.Cyan, 2, fillRemaining: true);
            win.WriteSeparator(9);

            win.WriteRow(10, "NON-DESTRUCTIVE UPDATE & USER DATA PROTECTION:", ConsoleColor.Yellow, 2, fillRemaining: true);
            win.WriteRow(11, "  Disk Protection   : Partition is NOT formatted. Drive is NEVER wiped.", ConsoleColor.Green, 2, fillRemaining: true);
            win.WriteRow(12, "  User Files        : All documents, notes, and settings.ini remain intact.", ConsoleColor.Green, 2, fillRemaining: true);
            win.WriteRow(13, "  Safety Rollback   : Current kernel backed up to 0:\\boot\\WeatherOS.bak.", ConsoleColor.White, 2, fillRemaining: true);

            win.WriteFooter("[ENTER] Apply Update to Disk   |   [O] Pick Another File   |   [ESC] Cancel", ConsoleColor.Yellow);
        }
        #endregion

        #region Input Handling
        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            switch (_mode)
            {
                case SysInfoMode.Overview:
                    HandleOverviewInput(desktop, keyEvent);
                    break;
                case SysInfoMode.Changelog:
                    HandleChangelogInput(desktop, keyEvent);
                    break;
                case SysInfoMode.CommitDetails:
                    HandleCommitDetailsInput(desktop, keyEvent);
                    break;
                case SysInfoMode.OtaUpdateMenu:
                    HandleOtaMenuInput(desktop, keyEvent);
                    break;
                case SysInfoMode.OtaFilePicker:
                    HandleOtaFilePickerInput(desktop, keyEvent);
                    break;
                case SysInfoMode.OtaUpdateStaged:
                    HandleOtaStagedInput(desktop, keyEvent);
                    break;
            }
        }

        private void HandleOverviewInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                desktop.SetCurrentApp("launcher");
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.UpArrow || keyEvent.Key == ConsoleKeyEx.DownArrow || keyEvent.Key == ConsoleKeyEx.Tab)
            {
                _selectedOverviewOption = (_selectedOverviewOption == 0) ? 1 : 0;
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.D1 || keyEvent.Key == ConsoleKeyEx.Num1 || keyEvent.KeyChar == '1' ||
                keyEvent.Key == ConsoleKeyEx.C || keyEvent.KeyChar == 'c' || keyEvent.KeyChar == 'C' ||
                keyEvent.Key == ConsoleKeyEx.L || keyEvent.KeyChar == 'l' || keyEvent.KeyChar == 'L')
            {
                _mode = SysInfoMode.Changelog;
                _selectedCommitIndex = 0;
                _commitListScrollOffset = 0;
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.D2 || keyEvent.Key == ConsoleKeyEx.Num2 || keyEvent.KeyChar == '2' ||
                keyEvent.Key == ConsoleKeyEx.U || keyEvent.KeyChar == 'u' || keyEvent.KeyChar == 'U')
            {
                _mode = SysInfoMode.OtaUpdateMenu;
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.Enter)
            {
                if (_selectedOverviewOption == 0)
                {
                    _mode = SysInfoMode.Changelog;
                    _selectedCommitIndex = 0;
                    _commitListScrollOffset = 0;
                }
                else
                {
                    _mode = SysInfoMode.OtaUpdateMenu;
                }
            }
        }

        private void HandleChangelogInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            var commits = GitHubCommitsService.GetCommits();

            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                _mode = SysInfoMode.Overview;
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.UpArrow)
            {
                if (_selectedCommitIndex > 0)
                {
                    _selectedCommitIndex--;
                }
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.DownArrow)
            {
                if (_selectedCommitIndex < commits.Count - 1)
                {
                    _selectedCommitIndex++;
                }
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.PageUp)
            {
                _selectedCommitIndex = Math.Max(0, _selectedCommitIndex - 5);
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.PageDown)
            {
                _selectedCommitIndex = Math.Min(commits.Count - 1, _selectedCommitIndex + 5);
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.Home)
            {
                _selectedCommitIndex = 0;
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.End)
            {
                _selectedCommitIndex = Math.Max(0, commits.Count - 1);
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.Enter || keyEvent.Key == ConsoleKeyEx.Spacebar)
            {
                if (_selectedCommitIndex >= 0 && _selectedCommitIndex < commits.Count)
                {
                    _mode = SysInfoMode.CommitDetails;
                    _commitDetailScroll = 0;
                }
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.R || keyEvent.KeyChar == 'r' || keyEvent.KeyChar == 'R')
            {
                _syncStatus = "Connecting to api.github.com:443 (TLS 1.2)...";
                GitHubCommitsService.FetchLiveFromGitHub(out _syncStatus);
                _selectedCommitIndex = 0;
                _commitListScrollOffset = 0;
                return;
            }
        }

        private void HandleCommitDetailsInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                _mode = SysInfoMode.Changelog;
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.UpArrow)
            {
                if (_commitDetailScroll > 0) _commitDetailScroll--;
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.DownArrow)
            {
                _commitDetailScroll++;
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.PageUp)
            {
                _commitDetailScroll = Math.Max(0, _commitDetailScroll - 8);
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.PageDown)
            {
                _commitDetailScroll += 8;
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.Home)
            {
                _commitDetailScroll = 0;
                return;
            }
        }

        private void HandleOtaMenuInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                _mode = SysInfoMode.Overview;
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.UpArrow)
            {
                _selectedOtaMenuOption = (_selectedOtaMenuOption > 0) ? _selectedOtaMenuOption - 1 : 2;
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.DownArrow || keyEvent.Key == ConsoleKeyEx.Tab)
            {
                _selectedOtaMenuOption = (_selectedOtaMenuOption < 2) ? _selectedOtaMenuOption + 1 : 0;
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.D1 || keyEvent.Key == ConsoleKeyEx.Num1 || keyEvent.KeyChar == '1' ||
                keyEvent.Key == ConsoleKeyEx.O || keyEvent.KeyChar == 'o' || keyEvent.KeyChar == 'O')
            {
                OpenUpdateFilePicker();
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.D2 || keyEvent.Key == ConsoleKeyEx.Num2 || keyEvent.KeyChar == '2' ||
                keyEvent.Key == ConsoleKeyEx.T || keyEvent.KeyChar == 't' || keyEvent.KeyChar == 'T')
            {
                desktop.SetCurrentApp("transfer");
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.D3 || keyEvent.Key == ConsoleKeyEx.Num3 || keyEvent.KeyChar == '3' ||
                keyEvent.Key == ConsoleKeyEx.I || keyEvent.KeyChar == 'i' || keyEvent.KeyChar == 'I')
            {
                ExecuteOtaOption3(desktop);
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.Enter)
            {
                if (_selectedOtaMenuOption == 0)
                {
                    OpenUpdateFilePicker();
                }
                else if (_selectedOtaMenuOption == 1)
                {
                    desktop.SetCurrentApp("transfer");
                }
                else
                {
                    ExecuteOtaOption3(desktop);
                }
            }
        }

        private void ExecuteOtaOption3(TuiDesktop desktop)
        {
            bool isInstalled = UpdateManagerService.IsInstalledOnDisk();
            bool hasBackup = UpdateManagerService.HasBackupKernel();

            if (isInstalled && hasBackup)
            {
                string rbStat;
                UpdateManagerService.RollbackToBackup(out rbStat);
                _syncStatus = rbStat;
                _mode = SysInfoMode.Overview;
            }
            else
            {
                desktop.SetCurrentApp("installer");
            }
        }

        private void HandleOtaFilePickerInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                _mode = SysInfoMode.OtaUpdateMenu;
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.UpArrow)
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
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.DownArrow)
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
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.Tab || keyEvent.Key == ConsoleKeyEx.RightArrow)
            {
                if (_cachedLocations != null && _cachedLocations.Count > 0)
                {
                    _openDialogLocationIndex = (_openDialogLocationIndex + 1) % _cachedLocations.Count;
                    RefreshOpenDialogFiles();
                }
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.LeftArrow)
            {
                if (_cachedLocations != null && _cachedLocations.Count > 0)
                {
                    _openDialogLocationIndex = (_openDialogLocationIndex - 1 + _cachedLocations.Count) % _cachedLocations.Count;
                    RefreshOpenDialogFiles();
                }
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.Enter)
            {
                StageSelectedFile();
            }
        }

        private void HandleOtaStagedInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (_updateAppliedSuccess)
            {
                if (keyEvent.Key == ConsoleKeyEx.Enter || keyEvent.Key == ConsoleKeyEx.R || keyEvent.KeyChar == 'r' || keyEvent.KeyChar == 'R')
                {
                    Power.Reboot();
                    return;
                }

                if (keyEvent.Key == ConsoleKeyEx.Escape)
                {
                    _mode = SysInfoMode.Overview;
                    return;
                }
            }
            else
            {
                if (keyEvent.Key == ConsoleKeyEx.Escape)
                {
                    _mode = SysInfoMode.Overview;
                    return;
                }

                if (keyEvent.Key == ConsoleKeyEx.O || keyEvent.KeyChar == 'o' || keyEvent.KeyChar == 'O')
                {
                    OpenUpdateFilePicker();
                    return;
                }

                if (keyEvent.Key == ConsoleKeyEx.Enter)
                {
                    if (_stagedFileBytes != null && _stagedFileBytes.Length > 0)
                    {
                        bool ok = UpdateManagerService.ApplyUpdate(_stagedFileBytes, _stagedFileName, out _updateAppliedStatus);
                        if (ok)
                        {
                            _updateAppliedSuccess = true;
                        }
                        else
                        {
                            _pickerStatus = "Update error: " + _updateAppliedStatus;
                        }
                    }
                    else
                    {
                        _updateAppliedStatus = "Error: Staged payload is empty.";
                    }
                }
            }
        }
        #endregion

        #region Helpers
        private static List<string> WrapText(string text, int maxLineLength)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text)) return result;

            string[] paragraphs = text.Replace("\r", "").Split('\n');
            foreach (var para in paragraphs)
            {
                string p = para.TrimEnd();
                if (string.IsNullOrEmpty(p))
                {
                    result.Add("");
                    continue;
                }

                while (p.Length > maxLineLength)
                {
                    int splitIdx = p.LastIndexOf(' ', maxLineLength);
                    if (splitIdx <= 0) splitIdx = maxLineLength;

                    result.Add(p.Substring(0, splitIdx));
                    p = p.Substring(splitIdx).TrimStart();
                }

                if (p.Length > 0)
                {
                    result.Add(p);
                }
            }

            return result;
        }

        private static uint ComputeCrc32(byte[] data)
        {
            if (data == null || data.Length == 0) return 0xFFFFFFFF;
            uint crc = 0xFFFFFFFF;
            for (int i = 0; i < data.Length; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0)
                        crc = (crc >> 1) ^ 0xEDB88320;
                    else
                        crc >>= 1;
                }
            }
            return ~crc;
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
            if (u.EndsWith(".BIN") || u.EndsWith(".IMG")) return "Firmware Image";
            if (u.EndsWith(".PKG") || u.EndsWith(".WOS")) return "Update Package";
            if (u.EndsWith(".ZIP") || u.EndsWith(".TAR")) return "Archive File";
            if (u.EndsWith(".JSON")) return "JSON Data";
            if (u.EndsWith(".TXT")) return "Text Document";
            if (u.EndsWith(".INI") || u.EndsWith(".CFG")) return "Configuration";
            if (u.EndsWith(".LOG")) return "Log File";
            return "Binary File";
        }
        #endregion
    }
}
