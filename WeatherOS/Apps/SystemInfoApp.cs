using System;
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
        OtaUpdate
    }

    public class SystemInfoApp : IApp
    {
        public string Id => "about";
        public string Name => "System Information";
        public bool IsRealTime => false;

        private SysInfoMode _mode = SysInfoMode.Overview;
        private int _selectedOption = 0; // 0 = Changelog, 1 = OS Update
        private int _changelogScroll = 0;

        private static readonly string[] ChangelogLines = new string[]
        {
            "# WeatherOS Software Release History",
            "> Document: RELEASE.md (Tracked Engineering Changelog)",
            "> Target Architecture: x86 32-bit (Cosmos IL2CPU Bare-Metal Runtime)",
            "",
            "## [v3.0.0] - 2026-10-13 (Current Release)",
            "### Added",
            "  * Network File Transfer Engine (WOSF Protocol over UDP port 7331).",
            "  * Windows Companion CLI sync tool (WeatherTransfer) with node auto-discovery.",
            "  * Interactive Dual-Mode Bootloader (Live OS in RAM vs Disk Setup Wizard).",
            "  * Dynamic Display Sizing & Coordinate Clipping engine in TuiDesktop.",
            "  * System Information Changelog (.md viewer) with vertical scroll buffer.",
            "  * Firmware Update Manager (OTA Staging Architecture specification).",
            "### Changed",
            "  * Decoupled lunar phase telemetry from 7-day weather forecast.",
            "  * Replaced manual IP broadcast with active unicast ARP resolution.",
            "  * Re-architected application grid into 4 dedicated launcher categories.",
            "### Fixed",
            "  * Resolved UDP broadcast MAC collision causing packet drops on real NICs.",
            "  * Fixed window coordinate border overflow and text bleed on 80x25 VGA.",
            "  * Fixed Notepad file dialog crash when switching between virtual and ATA drives.",
            "",
            "## [v2.5.0] - 2026-10-12",
            "### Added",
            "  * RawTcpHttp bare-metal socket stack bypassing Cosmos DHCP hang.",
            "  * Realtek RTL8102E / RTL8139 PCI Network Interface Card driver.",
            "  * Custom PCI ATA IDE controller initialization and sector probing.",
            "  * File Explorer dialog in Notepad with drive selection and directory tree.",
            "  * Disk Diagnostics app with sector read benchmark and integrity checks.",
            "### Changed",
            "  * Migrated system settings persistence from volatile RAM to settings.ini.",
            "  * Optimized font rendering and palette switching for Dark and Light modes.",
            "### Fixed",
            "  * Fixed infinite loop during static IP fallback when DHCP server is absent.",
            "  * Corrected FAT32 cluster chain calculation on partitioned secondary disks.",
            "",
            "## [v2.0.0] - 2026-10-09",
            "### Added",
            "  * Full TUI Desktop windowing subsystem with focus management.",
            "  * Weather Dashboard with real-time station metrics (temp, humidity, pressure).",
            "  * 7-Day synoptic weather forecasting engine with trend indicator.",
            "  * Lunar Observatory calculating phase angles, illumination and moonrise.",
            "  * Serial sensor telemetry daemon for external UART hardware stations.",
            "  * System Resources monitor with CPU telemetry and heap allocation bars.",
            "### Changed",
            "  * Replaced legacy CLI prompt with graphical launcher shell.",
            "### Fixed",
            "  * Resolved memory leak caused by repeated JSON string allocations.",
            "",
            "## [v1.0.0] - 2026-10-04",
            "### Added",
            "  * Initial bootstrap kernel on Cosmos x86 framework.",
            "  * VGA text-mode 80x25 character buffer and color attributes.",
            "  * Basic keyboard input driver and command interpreter shell.",
            "  * Built-in diagnostic commands (sysinfo, memory, ping, reboot).",
            "",
            "----------------------------------------------------------------------",
            "End of changelog. WeatherOS Project (c) 2026. All rights reserved."
        };

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
                case SysInfoMode.OtaUpdate:
                    DrawOtaUpdate(desktop);
                    break;
            }
        }

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
            win.WriteRow(7, $"  Storage   : {storageInfo}", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteRow(8, "  Boot Mode : Live OS  [RAM execution / ISO9660 Read-Only]", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteSeparator(9);

            // Section 3: Sub-actions / Sub-apps
            win.WriteRow(10, "AVAILABLE SYSTEM MODULES:", ConsoleColor.Yellow, 2, fillRemaining: true);
            win.WriteSelectionRow(11, "[1] View Updates & Changelog (.md release notes)", _selectedOption == 0, 2);
            win.WriteSelectionRow(12, "[2] OS Firmware Update Manager (OTA Staging)", _selectedOption == 1, 2);
            win.WriteSeparator(13);

            // Section 4: Dynamic hint
            if (_selectedOption == 0)
            {
                win.WriteRow(14, "Description: Scrollable engineering release notes & version history.", ConsoleColor.DarkCyan, 2, fillRemaining: true);
            }
            else
            {
                win.WriteRow(14, "Description: Bare-metal A/B dual-partition OTA firmware update manager.", ConsoleColor.DarkCyan, 2, fillRemaining: true);
            }

            win.WriteFooter("[Up/Dn] Select   [ENTER] Open   [1/L] Changelog   [2/U] OTA   [ESC] Exit", ConsoleColor.Yellow);
        }

        private void DrawChangelog(TuiDesktop desktop)
        {
            var win = desktop.CreateWindow("WEATHEROS RELEASE HISTORY (RELEASE.md)", WindowSize.Large);

            win.WriteRow(0, "Tracked Engineering Release Log - Arrow keys to scroll", ConsoleColor.Cyan, 2, fillRemaining: true);
            win.WriteSeparator(1);

            int viewHeight = win.InnerHeight - 3;
            if (viewHeight <= 0) viewHeight = 12;

            int maxScroll = Math.Max(0, ChangelogLines.Length - viewHeight);
            if (_changelogScroll > maxScroll) _changelogScroll = maxScroll;
            if (_changelogScroll < 0) _changelogScroll = 0;

            for (int r = 0; r < viewHeight; r++)
            {
                int lineIdx = _changelogScroll + r;
                if (lineIdx < ChangelogLines.Length)
                {
                    string line = ChangelogLines[lineIdx];
                    ConsoleColor fg = ConsoleColor.White;

                    if (line.StartsWith("# "))
                    {
                        fg = ConsoleColor.Cyan;
                    }
                    else if (line.StartsWith("## "))
                    {
                        fg = ConsoleColor.Yellow;
                    }
                    else if (line.StartsWith("### "))
                    {
                        fg = ConsoleColor.Green;
                    }
                    else if (line.StartsWith("  * ") || line.StartsWith("* "))
                    {
                        fg = ConsoleColor.White;
                    }
                    else if (line.StartsWith("    - ") || line.StartsWith("- "))
                    {
                        fg = ConsoleColor.Gray;
                    }
                    else if (line.StartsWith("> "))
                    {
                        fg = ConsoleColor.DarkGray;
                    }
                    else if (line.StartsWith("---"))
                    {
                        fg = ConsoleColor.DarkCyan;
                    }

                    win.WriteRow(2 + r, line, fg, 2, fillRemaining: true);
                }
                else
                {
                    win.WriteRow(2 + r, "", ConsoleColor.Black, 2, fillRemaining: true);
                }
            }

            int currentLine = _changelogScroll + 1;
            int totalLines = ChangelogLines.Length;
            win.WriteFooter($"[Up/Dn] Scroll  [PgUp/PgDn] Page  [Home/End]  [ESC] Back  |  Line {currentLine}/{totalLines}", ConsoleColor.Yellow);
        }

        private void DrawOtaUpdate(TuiDesktop desktop)
        {
            var win = desktop.CreateWindow("FIRMWARE & OS UPDATE MANAGER (OTA)", WindowSize.Large);

            string activeIp = RawTcpHttp.GetAssignedIp();
            if (string.IsNullOrEmpty(activeIp) || activeIp == "0.0.0.0") activeIp = "Offline";

            win.WriteRow(0, "[!] NOTICE: RUNNING IN LIVE OS MODE (RAM / ISO9660)", ConsoleColor.Yellow, 2, fillRemaining: true);
            win.WriteRow(1, "Persistent kernel flashing is locked until WeatherOS is installed to disk.", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteSeparator(2);

            win.WriteRow(3, "SYSTEM FIRMWARE STATUS:", ConsoleColor.Yellow, 2, fillRemaining: true);
            win.WriteRow(4, "  Installed Version : WeatherOS v3.0.0 (Release Build)", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteRow(5, "  Partition Slot    : Slot A [Live RAM Execution - Read Only]", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteRow(6, $"  OTA UDP Listener  : Port 7331 (WOSF) on IP {activeIp} [READY]", ConsoleColor.Green, 2, fillRemaining: true);
            win.WriteSeparator(7);

            win.WriteRow(8, "REAL-WORLD A/B DUAL-SLOT OTA ARCHITECTURE:", ConsoleColor.Cyan, 2, fillRemaining: true);
            win.WriteRow(9, " 1. PC sends kernel image using: WeatherTransfer send <kernel.bin>", ConsoleColor.Gray, 2, fillRemaining: true);
            win.WriteRow(10, " 2. WOSF protocol streams binary over UDP 7331 with CRC32 packet check.", ConsoleColor.Gray, 2, fillRemaining: true);
            win.WriteRow(11, " 3. Kernel stages image in dormant Slot B on hard disk while Slot A runs.", ConsoleColor.Gray, 2, fillRemaining: true);
            win.WriteRow(12, " 4. Bootloader flips active partition pointer with automatic crash rollback.", ConsoleColor.Gray, 2, fillRemaining: true);
            win.WriteSeparator(13);

            win.WriteRow(14, "Action: To send or receive test files, launch the File Transfer app.", ConsoleColor.DarkYellow, 2, fillRemaining: true);

            win.WriteFooter("[ESC] Return to Overview   |   [F] Open File Transfer   |   [ENTER] Refresh", ConsoleColor.Yellow);
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (_mode == SysInfoMode.Overview)
            {
                if (keyEvent.Key == ConsoleKeyEx.Escape)
                {
                    desktop.SetCurrentApp("launcher");
                    return;
                }

                if (keyEvent.Key == ConsoleKeyEx.UpArrow || keyEvent.Key == ConsoleKeyEx.DownArrow || keyEvent.Key == ConsoleKeyEx.Tab)
                {
                    _selectedOption = (_selectedOption == 0) ? 1 : 0;
                    return;
                }

                if (keyEvent.Key == ConsoleKeyEx.D1 || keyEvent.Key == ConsoleKeyEx.Num1 || keyEvent.KeyChar == '1' ||
                    keyEvent.Key == ConsoleKeyEx.L || keyEvent.KeyChar == 'l' || keyEvent.KeyChar == 'L' ||
                    keyEvent.Key == ConsoleKeyEx.C || keyEvent.KeyChar == 'c' || keyEvent.KeyChar == 'C')
                {
                    _mode = SysInfoMode.Changelog;
                    _changelogScroll = 0;
                    return;
                }

                if (keyEvent.Key == ConsoleKeyEx.D2 || keyEvent.Key == ConsoleKeyEx.Num2 || keyEvent.KeyChar == '2' ||
                    keyEvent.Key == ConsoleKeyEx.U || keyEvent.KeyChar == 'u' || keyEvent.KeyChar == 'U')
                {
                    _mode = SysInfoMode.OtaUpdate;
                    return;
                }

                if (keyEvent.Key == ConsoleKeyEx.Enter)
                {
                    if (_selectedOption == 0)
                    {
                        _mode = SysInfoMode.Changelog;
                        _changelogScroll = 0;
                    }
                    else
                    {
                        _mode = SysInfoMode.OtaUpdate;
                    }
                    return;
                }
            }
            else if (_mode == SysInfoMode.Changelog)
            {
                int viewHeight = 14;
                int maxScroll = Math.Max(0, ChangelogLines.Length - viewHeight);

                if (keyEvent.Key == ConsoleKeyEx.Escape)
                {
                    _mode = SysInfoMode.Overview;
                    return;
                }

                if (keyEvent.Key == ConsoleKeyEx.UpArrow)
                {
                    if (_changelogScroll > 0) _changelogScroll--;
                    return;
                }

                if (keyEvent.Key == ConsoleKeyEx.DownArrow)
                {
                    if (_changelogScroll < maxScroll) _changelogScroll++;
                    return;
                }

                if (keyEvent.Key == ConsoleKeyEx.PageUp)
                {
                    _changelogScroll = Math.Max(0, _changelogScroll - 8);
                    return;
                }

                if (keyEvent.Key == ConsoleKeyEx.PageDown)
                {
                    _changelogScroll = Math.Min(maxScroll, _changelogScroll + 8);
                    return;
                }

                if (keyEvent.Key == ConsoleKeyEx.Home)
                {
                    _changelogScroll = 0;
                    return;
                }

                if (keyEvent.Key == ConsoleKeyEx.End)
                {
                    _changelogScroll = maxScroll;
                    return;
                }
            }
            else if (_mode == SysInfoMode.OtaUpdate)
            {
                if (keyEvent.Key == ConsoleKeyEx.Escape)
                {
                    _mode = SysInfoMode.Overview;
                    return;
                }

                if (keyEvent.Key == ConsoleKeyEx.F || keyEvent.KeyChar == 'f' || keyEvent.KeyChar == 'F')
                {
                    desktop.SetCurrentApp("transfer");
                    return;
                }

                if (keyEvent.Key == ConsoleKeyEx.Enter)
                {
                    WeatherOS.Kernel.CustomNIC?.HackPoll();
                    return;
                }
            }
        }
    }
}
