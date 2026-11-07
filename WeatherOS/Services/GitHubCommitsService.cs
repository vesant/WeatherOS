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
                Sha = @"v3.4.0",
                Date = @"2026-11-07",
                Author = @"Vicente",
                Title = @"Add 5 meteorological map layers and global Main Commands Menu popup",
                Description = @"Added Thermal, Radar, Wind, and Pressure layers to World Weather Map and unified [F1] Main Commands Menu popup across all OS apps."
            });
            list.Add(new GitHubCommit {
                Sha = @"97e6d00",
                Date = @"2026-10-19",
                Author = @"vesant",
                Title = @"Change license from MIT to BSD 3-Clause",
                Description = @""
            });
            list.Add(new GitHubCommit {
                Sha = @"3782904",
                Date = @"2026-10-19",
                Author = @"vesant",
                Title = @"Add diagram section to README",
                Description = @"Added a diagram to the README for better visualization."
            });
            list.Add(new GitHubCommit {
                Sha = @"b693820",
                Date = @"2026-10-19",
                Author = @"vesant",
                Title = @"Diagram of WeatherOS",
                Description = @""
            });
            list.Add(new GitHubCommit {
                Sha = @"ec1dc63",
                Date = @"2026-10-18",
                Author = @"Vicente",
                Title = @"Fix FAULT 06 VMT truncation via direct raw kernel OTA detection and instant link check",
                Description = @""
            });
            list.Add(new GitHubCommit {
                Sha = @"935302c",
                Date = @"2026-10-18",
                Author = @"Vicente",
                Title = @"Fix FAULT 06 entry point corruption during OTA flash and optimize DHCP LAN timing",
                Description = @""
            });
            list.Add(new GitHubCommit {
                Sha = @"305ea7a",
                Date = @"2026-10-18",
                Author = @"Vicente",
                Title = @"Restore physical HDD sector 0 timeout in probe and deduplicate PCI IDE scan",
                Description = @""
            });
            list.Add(new GitHubCommit {
                Sha = @"35f037d",
                Date = @"2026-10-18",
                Author = @"Vicente",
                Title = @"Fix boot probe latency and eliminate 85% flash freeze via atomic PIO FIFO bursts and cache sync",
                Description = @""
            });
            list.Add(new GitHubCommit {
                Sha = @"75bfb08",
                Date = @"2026-10-18",
                Author = @"Vicente",
                Title = @"fix(ata): add 400ns recovery delay, DRDY check, and nIEN interrupt masking for mechanical disk flash stability",
                Description = @""
            });
            list.Add(new GitHubCommit {
                Sha = @"039bfd3",
                Date = @"2026-10-18",
                Author = @"Vicente",
                Title = @"fix(ota): eliminate 8.5MB heap allocation on transfer finish and bind direct flash to static buffer",
                Description = @""
            });
            list.Add(new GitHubCommit {
                Sha = @"537e341",
                Date = @"2026-10-18",
                Author = @"Vicente",
                Title = @"build: compile fresh kernel, boot images, and OTA update package",
                Description = @""
            });
            list.Add(new GitHubCommit {
                Sha = @"3b978c9",
                Date = @"2026-10-18",
                Author = @"Vicente",
                Title = @"chore(build): sync final bootable binaries",
                Description = @""
            });
            list.Add(new GitHubCommit {
                Sha = @"45fe222",
                Date = @"2026-10-18",
                Author = @"Vicente",
                Title = @"fix(ota/transfer): eliminate PIO heap allocations, decouple transfer from synchronous disk flash, and fix RAM store file visibility",
                Description = @""
            });
            list.Add(new GitHubCommit {
                Sha = @"e017b5a",
                Date = @"2026-10-18",
                Author = @"Vicente",
                Title = @"fix(ota/storage): prevent destructive auto-format, implement dynamic signature Stage2 patching, and add OTA reboot action",
                Description = @""
            });
            list.Add(new GitHubCommit {
                Sha = @"49a9992",
                Date = @"2026-10-18",
                Author = @"Vicente",
                Title = @"Eliminate all UI and heap allocations during network streaming and OTA disk deployment",
                Description = @""
            });
            list.Add(new GitHubCommit {
                Sha = @"1e24606",
                Date = @"2026-10-18",
                Author = @"Vicente",
                Title = @"Optimize ATA disk writing during OTA update: eliminate per-sector cache flush, batch PIO writes, and add live flashing feedback",
                Description = @""
            });
            list.Add(new GitHubCommit {
                Sha = @"97d4a5c",
                Date = @"2026-10-18",
                Author = @"Vicente",
                Title = @"fix(storage/ota): eliminate ATA PIO buffer overrun freeze, instant ACK on finish, and protect FAT32 VFS",
                Description = @""
            });
            list.Add(new GitHubCommit {
                Sha = @"a752eeb",
                Date = @"2026-10-18",
                Author = @"Vicente",
                Title = @"Fix IDE/ATA hard disk detection, enable IDEInit in OnBoot, remove disruptive SRST, and resolve drive enumeration in installer",
                Description = @""
            });
            list.Add(new GitHubCommit {
                Sha = @"1ad6b76",
                Date = @"2026-10-18",
                Author = @"Vicente",
                Title = @"Fix EX: 026CBDC1 VFS FileNotFoundException, add Stage2 auto-scan entry point, and prevent MBR corruption",
                Description = @""
            });
            list.Add(new GitHubCommit {
                Sha = @"485776f",
                Date = @"2026-10-18",
                Author = @"Vicente",
                Title = @"Fix 100% network transfer freeze, zero-allocation pkg extraction, and persistent VFS package deployment",
                Description = @""
            });
            list.Add(new GitHubCommit {
                Sha = @"67021e9",
                Date = @"2026-10-18",
                Author = @"Vicente",
                Title = @"Fix network transfer freeze, eliminate UI memory churn, expand NIC rings to 16 descriptors, and revert base OS for update testing",
                Description = @""
            });
            return list;
        }
    }
}
