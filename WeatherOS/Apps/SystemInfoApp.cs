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
        CommitDetails
    }

    public class SystemInfoApp : IApp
    {
        public string Id => "about";
        public string Name => "System Information";
        public bool IsRealTime => false;

        private SysInfoMode _mode = SysInfoMode.Overview;

        // Commit List Navigation
        private int _selectedCommitIndex = 0;
        private int _commitListScrollOffset = 0;
        private int _commitDetailScroll = 0;
        private string _syncStatus = "";

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
            }
        }

        #region Overview
        private void DrawOverview(TuiDesktop desktop)
        {
            var win = desktop.CreateWindow("SYSTEM SPECIFICATIONS & ABOUT WEATHEROS", WindowSize.Large);

            // Section 1: Operating System
            win.WriteRow(0, "OPERATING SYSTEM ARCHITECTURE:", ConsoleColor.Yellow, 2, fillRemaining: true);
            win.WriteRow(1, "  OS Name   : WeatherOS (Dedicated Meteorological System)", ConsoleColor.White, 2, fillRemaining: true);
            string versionStr = UpdateManagerService.GetInstalledVersion();
            win.WriteRow(2, $"  Version   : {versionStr} (x86 32-bit Protected Mode Native)", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteRow(3, "  Kernel    : Cosmos x86 Operating System Core (C# IL2CPU)", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteSeparator(4);

            // Section 2: Hardware Resources
            uint totalRamMB = CPU.GetAmountOfRAM();
            if (totalRamMB == 0) totalRamMB = 1024;
            uint usedRamBytes = GCImplementation.GetUsedRAM();
            uint usedRamMB = usedRamBytes / (1024 * 1024);
            int diskCount = DiskWipeService.GetPhysicalDrives().Count;

            win.WriteRow(5, "HARDWARE CONFIGURATION & SPECS:", ConsoleColor.Yellow, 2, fillRemaining: true);
            win.WriteRow(6, $"  Processor : {CPU.GetCPUBrandString()}", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteRow(7, $"  Memory    : {usedRamMB} MB used / {totalRamMB} MB total physical RAM", ConsoleColor.White, 2, fillRemaining: true);

            string storageInfo = diskCount > 0 ? $"{diskCount} Drive(s) Detected [FAT32 / IDE ATA]" : "Virtual RAM File System";
            string bootDesc = UpdateManagerService.GetBootTargetDescription();
            win.WriteRow(8, $"  Storage   : {storageInfo}", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteRow(9, $"  Boot Mode : {bootDesc}", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteSeparator(10);

            // Section 3: Sub-actions / Sub-apps
            win.WriteRow(11, "AVAILABLE SYSTEM MODULES:", ConsoleColor.Yellow, 2, fillRemaining: true);
            win.WriteSelectionRow(12, "[ENTER] View GitHub Commits & Changelog (vesant/WeatherOS: 32bit)", true, 2);
            win.WriteSeparator(13);

            // Section 4: Dynamic hint
            win.WriteRow(14, "Description: Tracked git commits from GitHub branch 32bit with live sync.", ConsoleColor.DarkCyan, 2, fillRemaining: true);

            win.WriteFooter("[F1 / H] Main Commands Menu  |  [ESC] Exit", ConsoleColor.Yellow);
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

                    string titleText = c.Title;
                    int maxTitleLen = win.InnerWidth - 28;
                    if (maxTitleLen < 15) maxTitleLen = 15;
                    if (titleText.Length > maxTitleLen) titleText = titleText.Substring(0, maxTitleLen - 3) + "...";

                    string rowLine = $"[{c.Sha}]  {c.Date}  {titleText}";
                    win.WriteSelectionRow(currentY, rowLine, isSelected, 1);

                    string preview = c.Description.Replace("\r", "").Replace("\n", " ");
                    int maxPrevLen = win.InnerWidth - 8;
                    if (preview.Length > maxPrevLen) preview = preview.Substring(0, maxPrevLen - 3) + "...";
                    win.WriteRow(currentY + 1, "      " + preview, ConsoleColor.DarkGray, 1, fillRemaining: true);
                }
                else
                {
                    win.WriteRow(currentY, "", ConsoleColor.Black, 1, fillRemaining: true);
                    win.WriteRow(currentY + 1, "", ConsoleColor.Black, 1, fillRemaining: true);
                }
            }

            win.WriteSeparator(17);
            win.WriteFooter($"[F1 / H] Main Commands Menu  |  [ESC] Overview  |  Commit {_selectedCommitIndex + 1}/{commits.Count}", ConsoleColor.Yellow);
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

            int maxLineLen = win.InnerWidth - 4;
            var displayLines = WrapTextToLines(c.Description, maxLineLen);

            int visibleRows = 11;
            int totalLines = displayLines.Count;
            if (_commitDetailScroll > totalLines - visibleRows) _commitDetailScroll = Math.Max(0, totalLines - visibleRows);
            if (_commitDetailScroll < 0) _commitDetailScroll = 0;

            for (int r = 0; r < visibleRows; r++)
            {
                int lineIdx = _commitDetailScroll + r;
                if (lineIdx < totalLines)
                {
                    win.WriteRow(5 + r, displayLines[lineIdx], ConsoleColor.White, 2, fillRemaining: true);
                }
                else
                {
                    win.WriteRow(5 + r, "", ConsoleColor.Black, 2, fillRemaining: true);
                }
            }

            win.WriteSeparator(16);
            string scrollHint = totalLines > visibleRows ? $"[Up/Dn Scroll {lineIdxText(totalLines, visibleRows)}]  |  " : "";
            win.WriteFooter("[F1 / H] Main Commands Menu  |  [ESC] Return to Commit List", ConsoleColor.Yellow);
        }

        private string lineIdxText(int total, int visible)
        {
            int maxTop = total - visible;
            if (maxTop <= 0) return "100%";
            int pct = (_commitDetailScroll * 100) / maxTop;
            return $"{pct}%";
        }

        private List<string> WrapTextToLines(string text, int maxLen)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;

            string[] paragraphs = text.Replace("\r", "").Split('\n');
            foreach (var p in paragraphs)
            {
                if (p.Length <= maxLen)
                {
                    lines.Add(p);
                }
                else
                {
                    int start = 0;
                    while (start < p.Length)
                    {
                        int len = Math.Min(maxLen, p.Length - start);
                        lines.Add(p.Substring(start, len));
                        start += len;
                    }
                }
            }
            return lines;
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
            }
        }

        private void HandleOverviewInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                desktop.SetCurrentApp("launcher");
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.Enter || keyEvent.Key == ConsoleKeyEx.D1 || keyEvent.Key == ConsoleKeyEx.Num1 ||
                keyEvent.Key == ConsoleKeyEx.C || keyEvent.KeyChar == 'c' || keyEvent.KeyChar == 'C')
            {
                _mode = SysInfoMode.Changelog;
                _selectedCommitIndex = 0;
                _commitListScrollOffset = 0;
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
        }
        #endregion
    }
}
