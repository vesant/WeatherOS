using System;
using System.Collections.Generic;

namespace WeatherOS.Services
{
    public class GitHubCommit
    {
        public string Sha { get; set; } = "";
        public string Date { get; set; } = "";
        public string Author { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public static class GitHubCommitsService
    {
        private static List<GitHubCommit> _cachedCommits = null;

        public static List<GitHubCommit> GetCommits()
        {
            if (_cachedCommits != null) return _cachedCommits;
            _cachedCommits = LoadEmbeddedCommits();
            return _cachedCommits;
        }

        public static bool FetchLiveFromGitHub(out string statusMessage)
        {
            try
            {
                var gitHubIp = new Cosmos.System.Network.IPv4.Address(20, 250, 119, 72);
                string json = TlsClient.FetchHttps(gitHubIp, "api.github.com", "/repos/vesant/WeatherOS/commits?sha=32bit", 4500);

                if (!string.IsNullOrEmpty(json))
                {
                    var parsed = ParseCommitsJson(json);
                    if (parsed != null && parsed.Count > 0)
                    {
                        _cachedCommits = parsed;
                        statusMessage = $"Live TLS 1.2: {parsed.Count} commits fetched directly from GitHub!";
                        return true;
                    }
                }
                statusMessage = "TLS connected but response payload was empty.";
                return false;
            }
            catch (Exception ex)
            {
                statusMessage = "TLS error: " + ex.Message;
                return false;
            }
        }

        public static void ReloadFromJson(byte[] data)
        {
            if (data == null || data.Length == 0) return;
            try
            {
                string json = System.Text.Encoding.ASCII.GetString(data);
                var parsed = ParseCommitsJson(json);
                if (parsed != null && parsed.Count > 0)
                {
                    _cachedCommits = parsed;
                }
            }
            catch { }
        }

        private static List<GitHubCommit> ParseCommitsJson(string json)
        {
            var list = new List<GitHubCommit>();
            if (string.IsNullOrEmpty(json)) return list;

            int cur = 0;
            while (cur < json.Length)
            {
                int shaIdx = json.IndexOf("\"sha\":", cur);
                if (shaIdx < 0) break;

                int shaValStart = json.IndexOf('"', shaIdx + 6);
                if (shaValStart < 0) break;
                int shaValEnd = json.IndexOf('"', shaValStart + 1);
                if (shaValEnd < 0) break;
                string fullSha = json.Substring(shaValStart + 1, shaValEnd - shaValStart - 1);
                string sha = fullSha.Length > 7 ? fullSha.Substring(0, 7) : fullSha;

                // Locate the commit message block
                int commitBlockIdx = json.IndexOf("\"commit\":", shaValEnd);
                if (commitBlockIdx < 0) break;

                string title = "Update";
                string desc = "";
                int msgIdx = json.IndexOf("\"message\":", commitBlockIdx);
                if (msgIdx >= 0)
                {
                    int msgStart = json.IndexOf('"', msgIdx + 10);
                    if (msgStart >= 0)
                    {
                        int msgEnd = json.IndexOf('"', msgStart + 1);
                        while (msgEnd > 0 && json[msgEnd - 1] == '\\')
                        {
                            msgEnd = json.IndexOf('"', msgEnd + 1);
                        }
                        if (msgEnd > 0)
                        {
                            string rawMsg = json.Substring(msgStart + 1, msgEnd - msgStart - 1)
                                .Replace("\\n", "\n").Replace("\\r", "").Replace("\\\"", "\"");
                            string[] lines = rawMsg.Split('\n');
                            if (lines.Length > 0) title = lines[0].Trim();
                            if (lines.Length > 1)
                            {
                                var dLines = new List<string>();
                                for (int i = 1; i < lines.Length; i++) dLines.Add(lines[i]);
                                desc = string.Join("\n", dLines).Trim();
                            }
                        }
                    }
                }

                string date = "2026-10-13";
                int dateIdx = json.IndexOf("\"date\":", commitBlockIdx);
                if (dateIdx >= 0)
                {
                    int dateStart = json.IndexOf('"', dateIdx + 7);
                    if (dateStart >= 0)
                    {
                        int dateEnd = json.IndexOf('"', dateStart + 1);
                        if (dateEnd > 0)
                        {
                            string rawDate = json.Substring(dateStart + 1, dateEnd - dateStart - 1);
                            if (rawDate.Length >= 10) date = rawDate.Substring(0, 10);
                        }
                    }
                }

                string author = "Vicente";
                int authorIdx = json.IndexOf("\"author\":", commitBlockIdx);
                if (authorIdx >= 0)
                {
                    int nameIdx = json.IndexOf("\"name\":", authorIdx);
                    if (nameIdx >= 0 && nameIdx < authorIdx + 100)
                    {
                        int nameStart = json.IndexOf('"', nameIdx + 7);
                        if (nameStart >= 0)
                        {
                            int nameEnd = json.IndexOf('"', nameStart + 1);
                            if (nameEnd > 0) author = json.Substring(nameStart + 1, nameEnd - nameStart - 1);
                        }
                    }
                }

                list.Add(new GitHubCommit
                {
                    Sha = sha,
                    Date = date,
                    Author = author,
                    Title = title,
                    Description = desc
                });

                // Jump past "parents": [...] so tree.sha and parents.sha are NEVER matched as fake commits
                int parentsIdx = json.IndexOf("\"parents\":", shaValEnd);
                if (parentsIdx >= 0)
                {
                    int bracketEnd = json.IndexOf(']', parentsIdx);
                    if (bracketEnd > 0) cur = bracketEnd + 1;
                    else cur = shaValEnd + 1;
                }
                else
                {
                    cur = shaValEnd + 1;
                }
            }

            return list;
        }

        private static List<GitHubCommit> LoadEmbeddedCommits()
        {
            var list = new List<GitHubCommit>();
            list.Add(new GitHubCommit {
                Sha = @"72cd642",
                Date = @"2026-10-13",
                Author = @"Vicente",
                Title = @"Add installer, OTA update, and live changelog features",
                Description = @"- Add interactive hard disk installer app with multi-step UI
- Refactor SystemInfoApp: live GitHub changelog, file picker, OTA update
- Implement UpdateManagerService for install, update, rollback, CRC32
- Add GitHubCommitsService for commit history (TLS 1.2 support)
- Integrate pure C# SHA-256 and TLS 1.2 client for secure updates
- Enhance BootMenu for disk/live/installer boot options
- Register installer in launcher and desktop; improve ESC handling
- Enable robust, non-destructive updates and user data protection"
            });
            list.Add(new GitHubCommit {
                Sha = @"1a0e632",
                Date = @"2026-10-13",
                Author = @"Vicente",
                Title = @"Enhance System Info app with changelog & OTA manager",
                Description = @"Revamped the System Information app to support three interactive modes: Overview, Changelog (with scrollable release notes), and OTA Update Manager. Improved input handling for navigation and mode switching. Updated launcher description and ensured ESC key routing for consistent app exit behavior."
            });
            list.Add(new GitHubCommit {
                Sha = @"18d4f68",
                Date = @"2026-10-13",
                Author = @"Vicente",
                Title = @"Improve MAC resolution for UDP/ARP; add ResolveArp method",
                Description = @"Refactored MAC address selection for UDP sending to use a new ResolveArp method in RawTcpHttp, which actively resolves the MAC for a target IP with fallbacks to known MACs or broadcast. Added GetRouterMac for easier router MAC access. Reduced unnecessary broadcasts and improved ARP robustness."
            });
            list.Add(new GitHubCommit {
                Sha = @"79dcc4c",
                Date = @"2026-10-13",
                Author = @"Vicente",
                Title = @"Refactor peer discovery and verification logic",
                Description = @"Distinguish between discovered and verified peers in the network transfer app. Update UI to show detailed peer status and auto-fill IP editor. Add subnet filtering for discovery packets. Improve ping logic, manual IP entry, and keyboard handling for better user experience and reliability."
            });
            list.Add(new GitHubCommit {
                Sha = @"150221d",
                Date = @"2026-10-13",
                Author = @"Vicente",
                Title = @"Refactor TUI layout; add file transfer & boot menu",
                Description = @"- Centralized all TUI windowing with Screen/TuiWindow classes; removed hardcoded coordinates and improved UI consistency
- Added NetTransferApp and FileTransferService for reliable UDP file transfer, peer discovery, and OTA updates
- Expanded StorageService for binary file support in RAM and disk
- Added bootmenu, install, and transfer commands; new BootMenu UI at startup
- Updated all apps to use new windowing system and improved launcher categories
- Improved error handling, comments, and code structure throughout"
            });
            list.Add(new GitHubCommit {
                Sha = @"68cddae",
                Date = @"2026-10-12",
                Author = @"Vicente",
                Title = @"Redesign launcher, add Notepad file picker, simplify forecast",
                Description = @"- Launcher now uses categorized two-column layout with app details
- Notepad app features a Windows-style open file dialog
- StorageService supports file listing and loading by location
- Weather forecast UI simplified; lunar details removed
- Improved shortcut bars, help text, and status messages
- Removed unused lunar code for cleaner codebase"
            });
            list.Add(new GitHubCommit {
                Sha = @"5505337",
                Date = @"2026-10-12",
                Author = @"Vicente",
                Title = @"Improve disk, RAM, and network handling robustness",
                Description = @"- Read/write RAM drive files directly from memory for preview/edit
- Enhance ATA device detection, deduplication, and filtering
- Deduplicate physical drives using sector and MBR checks
- Increase ATA driver timeouts for better hardware compatibility
- Retry FAT32 write verification, report ""Sync Pending"" if needed
- Always store files in RAM after disk writes for session safety
- Sync RAM with disk on file load, handle empty files gracefully
- Reduce network timeouts, check PHY link before network ops
- WeatherService checks cable, logs better, simplifies fallback
- Refine logging and status messages for clarity"
            });
            list.Add(new GitHubCommit {
                Sha = @"51e8ced",
                Date = @"2026-10-12",
                Author = @"Vicente",
                Title = @"Add native PCI ATA/IDE driver and disk detection upgrades",
                Description = @"Introduced CustomPciAtaDriverService for direct PCI ATA/IDE controller support with PIO access. Integrated driver into system init. Enhanced DiskWipeService to prefer bare-metal devices and avoid duplicates. Improved FAT32 label extraction and disk naming. Updated DiskDiagnosticsService for PCI ATA awareness and hardware accuracy. Increased ATA probe timeouts for reliability. Refined UI: more disk details, better text alignment, and improved splash animation. Improved storage size formatting and error handling."
            });
            list.Add(new GitHubCommit {
                Sha = @"c4ac266",
                Date = @"2026-10-12",
                Author = @"Vicente",
                Title = @"Improve device selection and Live USB handling in disk apps",
                Description = @"Enhance UI to distinguish writable/internal vs. Live USB devices. Default to writable storage for operations and saving. Add logic to sort and prioritize devices, redirect file ops from Live USB to fixed drives, and improve device info display. Auto-test now chains to next device if first is read-only."
            });
            list.Add(new GitHubCommit {
                Sha = @"c7a5239",
                Date = @"2026-10-12",
                Author = @"Vicente",
                Title = @"Improve device detection, labeling, and size formatting",
                Description = @"- Use descriptive device type names in diagnostics and UI
- Detect and label ""Live USB - Read-Only"" devices
- Show decimal GB values for device sizes
- Enhance ATA device probing accuracy
- Filter out corrupted ATA model strings
- Mark ""Live USB"" storage as read-only in storage lists"
            });
            list.Add(new GitHubCommit {
                Sha = @"d93c3cb",
                Date = @"2026-10-12",
                Author = @"Vicente",
                Title = @"Add SATA AHCI driver & hardware disk diagnostics suite",
                Description = @"- Implement bare-metal SATA AHCI driver for PCI SATA disks
- Add Hardware Test tab to Disk Diagnostics with sector/file tests
- Introduce DiskHardwareTester and AtaDeviceDiagnostics for ATA probing
- Prioritize AHCI drives in DiskWipeService, improve detection/filtering
- Enhance FAT32 formatting and partition size logic
- Update FileSystemProbeService for AHCI-first probing
- Refactor StorageService for robust disk selection
- Polish splash screen animation and subtitle
- Misc bug fixes and code cleanup
- Remove obsolete current_lisbon.json binary"
            });
            list.Add(new GitHubCommit {
                Sha = @"ca33abd",
                Date = @"2026-10-12",
                Author = @"Vicente",
                Title = @"Refactor storage: direct FAT32 support, unified file access",
                Description = @"Major overhaul of storage and file management:
- All file operations now use StorageService, supporting both RAM and direct FAT32 disk access (no reliance on VFS).
- DiskDiagnosticsApp and DiskManagerApp UIs updated for clarity, with dynamic drive detection and direct disk file listing/editing.
- Added robust FAT32 read/write/delete/list logic in DiskWipeService.
- RAM file store refactored to use a StoredFile list.
- Partition detection now defaults to ""FAT32 Storage"".
- All direct file I/O replaced with hardware-agnostic methods.
- Improved UI layout and reliability on bare metal systems."
            });
            list.Add(new GitHubCommit {
                Sha = @"0b16963",
                Date = @"2026-10-12",
                Author = @"Vicente",
                Title = @"Add battery diagnostics, FAT32 format and storage upgrades",
                Description = @"Major update introducing BatteryDiagnosticsApp for real-time power telemetry, enhanced BatteryService with manual override and improved hardware probing, and automatic FAT32 formatting after disk wipe. StorageService now prioritizes physical FAT32 disk, with fallback to RAM, and supports direct FAT32 file writes. UI improvements in Notepad and DebugMenu, better partition detection, and overall code robustness."
            });
            list.Add(new GitHubCommit {
                Sha = @"b65cf54",
                Date = @"2026-10-11",
                Author = @"Vicente",
                Title = @"Add unified shortcut bars and ESC navigation to all apps",
                Description = @"Improved UI consistency by adding shortcut/help bars to all WeatherOS apps, displaying available keyboard shortcuts. The top and bottom bars in TuiDesktop now show dynamic app context and navigation hints. All main apps now handle [ESC] to return to the launcher, and additional shortcut instructions were added throughout. Minor UI adjustments were made for layout alignment."
            });
            list.Add(new GitHubCommit {
                Sha = @"749949f",
                Date = @"2026-10-11",
                Author = @"Vicente",
                Title = @"UI overhaul, new Lunar Observatory, file explorer upgrade",
                Description = @"- Standardized all UI and terminology to English (days, labels, etc.)
- Added ""Lunar Observatory"" app with 7-day moon phase forecast and ASCII art
- Refactored Disk & Storage Explorer to ""File & Storage Explorer"" with file/folder browsing, preview, delete, and drive switching
- Disk diagnostics now verify wipes with readback audit
- Unified battery status reporting and improved hardware detection
- Updated launcher and debug menu for new/renamed apps
- Disabled auto-location by default; set default city to Setubal
- Minor bug fixes and service improvements"
            });
            list.Add(new GitHubCommit {
                Sha = @"445c2fc",
                Date = @"2026-10-11",
                Author = @"Vicente",
                Title = @"Add 7-Day Weather Forecast app and WeatherAPI parsing",
                Description = @"Introduced a new 7-Day Weather Forecast app with dedicated UI and launcher integration. Extended WeatherService to fetch and parse 7-day forecasts from WeatherAPI.com, including daily weather, rain chance, and lunar data. Improved JSON parsing, moon phase icon rendering, and dashboard UI. Increased TCP buffer size for larger payloads. Enhanced disk and filesystem display logic."
            });
            list.Add(new GitHubCommit {
                Sha = @"ee74e23",
                Date = @"2026-10-11",
                Author = @"Vicente",
                Title = @"WeatherOS: Add astronomy, disk wipe progress, net fallback",
                Description = @"- Show moon phase, illumination, and night/day on dashboard
- Enhance weather icon rendering for more conditions
- Add real-time progress bar to disk wipe UI and refine logic
- Extend WeatherData with astronomy fields and night detection
- Parse astronomy data from weather JSON in WeatherService
- Improve DHCP/ARP probing and fallback to multiple gateways
- Add sample weather JSON for testing
- Minor UI/UX and error handling improvements"
            });
            list.Add(new GitHubCommit {
                Sha = @"12624e2",
                Date = @"2026-10-11",
                Author = @"Vicente",
                Title = @"Refactor UI layout, improve disk wipe & network logic",
                Description = @"Refactored UI for dynamic centering and consistent sizing across apps, removed or simplified help/status bars. Added live disk wipe progress UI. Improved DHCP/ARP logic for faster, more robust network setup and better gateway detection. Increased TCP SYN attempts and HTTP receive timeout. Suppressed console output in GUI mode."
            });
            list.Add(new GitHubCommit {
                Sha = @"2e39b7f",
                Date = @"2026-10-11",
                Author = @"Vicente",
                Title = @"UI polish, disk wipe, TCP & weather reliability improvements",
                Description = @"Improved UI alignment and truncation across multiple apps for better display. Added a ""Wipe Disk"" tab with confirmation and status to Disk Diagnostics. Refined main menu centering and window drawing. Enhanced RawTcpHttp for more reliable TCP connections. WeatherService now sends a User-Agent, improves error handling, and maps weather codes to conditions. Truncated long device/partition descriptions. Updated SplashScreen with a two-line typing effect."
            });
            list.Add(new GitHubCommit {
                Sha = @"15aa14b",
                Date = @"2026-10-11",
                Author = @"Vicente",
                Title = @"Enhance hardware probing for battery, disks, and partitions",
                Description = @"Refactored BatteryService to auto-detect AC/battery state using SMBus and ACPI EC, removing manual toggling. Improved live telemetry and status reporting in UI. Upgraded disk and partition detection, including better VFS and raw device probing, Linux swap/ext support, and more detailed diagnostics. Updated StorageService to support new partition naming. Improved overall robustness and clarity of hardware status messages."
            });
            list.Add(new GitHubCommit {
                Sha = @"b333b65",
                Date = @"2026-10-11",
                Author = @"Vicente",
                Title = @"Add hardware disk diagnostics & debug menu to WeatherOS",
                Description = @"Introduce DiskDiagnosticsService and GUI/CLI tools for probing PCI, AHCI, IDE, and Cosmos HAL storage. Add Debug Menu and Disk Diagnostics apps to TUI, with conditional main menu integration. Enhance disks command and Disk Manager with troubleshooting guidance. Improve battery simulation and add AC toggle. Refine UI for diagnostics and user navigation."
            });
            list.Add(new GitHubCommit {
                Sha = @"23d4cf8",
                Date = @"2026-10-11",
                Author = @"Vicente",
                Title = @"Refactor TUI/CLI: Modular apps, storage, and networking",
                Description = @"Major refactor to modularize TUI desktop with pluggable apps (Weather, Notepad, Disk Manager, etc.) and a new CLI command system. Adds disk probing, formatting, and wipe utilities, RAM/disk-backed file storage, improved weather telemetry (wttr.in), battery detection, and enhanced networking (DHCP, ARP, ICMP). Improves text editing, status messages, and overall code structure for maintainability and extensibility."
            });
            list.Add(new GitHubCommit {
                Sha = @"37c4698",
                Date = @"2026-10-09",
                Author = @"Vicente",
                Title = @"Refactor RTL8102E driver, add raw DHCP, UI updates",
                Description = @"Refactored the RTL8102E network driver for clearer initialization phases and improved debugging output. Added a CustomDHCP class for bare-metal DHCP auto-configuration using raw packets and polling. Updated TuiDesktop and Kernel to reflect dynamic IP/gateway assignment. Enhanced RawTcpHttp with longer timeouts, progress feedback, and better error handling. Updated WeatherService to use the new DHCP gateway and improved log messages throughout for diagnostics."
            });
            list.Add(new GitHubCommit {
                Sha = @"3040b00",
                Date = @"2026-10-09",
                Author = @"Vicente",
                Title = @"Add bare-metal DHCP/ARP autoconfig to RawTcpHttp",
                Description = @"Implement DoAutoConfig for DHCP and ARP-based network setup, updating CustomDataReceived to handle DHCP offers and ARP replies. FetchGet now uses auto-configured addresses. Add diagnostic output and two binary files for raw data samples."
            });
            list.Add(new GitHubCommit {
                Sha = @"d3b649a",
                Date = @"2026-10-09",
                Author = @"Vicente",
                Title = @"Add RTL8102E NIC driver, static IP, and ARP wakeup logic",
                Description = @"Implemented a custom RTL8102E PCI Ethernet driver for Cosmos OS with MMIO, DMA, and polling support. Network stack now uses a static IP to avoid DHCP issues. Added manual ARP wakeup for reliable TCP on real hardware. Updated MAC/gateway handling, improved TCP logic, and removed obsolete binaries and project files."
            });
            list.Add(new GitHubCommit {
                Sha = @"12cee2d",
                Date = @"2026-10-08",
                Author = @"Vicente",
                Title = @"Switch to WeatherAPI, add alert support, TCP/UX improvements",
                Description = @"- Switched weather backend from OpenWeatherMap to WeatherAPI.com, updated API key handling and JSON parsing.
- Added weather alert detection and display, including official alerts and heuristic warnings for severe conditions.
- Enhanced TUI desktop to show color alert banners.
- Improved TCP stack: larger receive buffer, TCP options on SYN, better payload handling.
- Network init now checks for NIC presence before DHCP.
- Added test/util files: chk.cs, alerts.py, and several binaries.
- Performed minor refactoring and code cleanup."
            });
            list.Add(new GitHubCommit {
                Sha = @"b99f8c7",
                Date = @"2026-10-08",
                Author = @"Vicente",
                Title = @"Add persistent Notepad and Settings with VFS support",
                Description = @"Implement file-based storage for Notepad and Settings using Cosmos VFS. Notepad now supports creating, opening, saving, and deleting files with a menu-driven UI. Settings are loaded from and saved to settings.ini. Kernel initializes VFS for file operations. UI and buffer handling updated for new features."
            });
            list.Add(new GitHubCommit {
                Sha = @"f4cd5c6",
                Date = @"2026-10-08",
                Author = @"Vicente",
                Title = @"Ensure console output lines are 79 chars wide",
                Description = @"Adjusted spacing in console output to consistently produce 79-character lines, adding comments for clarity. Added a comment explaining the 80-character width for desktop middle lines and its wrapping behavior."
            });
            list.Add(new GitHubCommit {
                Sha = @"b360e97",
                Date = @"2026-10-08",
                Author = @"Vicente",
                Title = @"Enhance resource monitor UI and add network byte counters",
                Description = @"Expanded DrawResourcesApp with dynamic CPU/RAM bars, network interface info, and live Tx/Rx byte counters. Introduced TotalRxBytes and TotalTxBytes in RawTcpHttp to track network activity. CPU usage is now simulated for display."
            });
            list.Add(new GitHubCommit {
                Sha = @"2ae3ecd",
                Date = @"2026-10-08",
                Author = @"Vicente",
                Title = @"Enhance TUI desktop, add settings, improve networking",
                Description = @"- Added new TUI apps: System Info, Network Config, Settings, Reboot, Shutdown
- Introduced dark mode, debug logs, auto-location, and custom API key support
- Improved resource monitor, theming, and input handling
- CLI can now launch TUI apps directly; help lists all apps
- API key is obfuscated and decoded at runtime
- Improved TCP/HTTP stack reliability and Cosmos compatibility
- WeatherService supports auto-location and robust JSON parsing
- Removed legacy CLI commands in favor of TUI apps"
            });
            list.Add(new GitHubCommit {
                Sha = @"2890735",
                Date = @"2026-10-08",
                Author = @"Vicente",
                Title = @"Add launcher menu and multitasking to TUI desktop",
                Description = @"Refactored TUI to introduce a launcher as the default entry point, enabling navigation between Weather, Change City, Clock, Calculator, and Notepad apps. Each app now has dedicated input handling and rendering. Enhanced weather view with ASCII icons. Updated UI, menu bar, and version to v2.0 to reflect multitasking support."
            });
            return list;
        }
    }
}
