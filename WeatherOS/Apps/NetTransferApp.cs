using System;
using System.Collections.Generic;
using Cosmos.System;
using WeatherOS.Graphics;
using WeatherOS.Services;

namespace WeatherOS.Apps
{
    public enum NetTransferMode
    {
        Menu,
        Receive,
        SendSelectFile,
        SendEnterIp,
        SendingProgress,
        Ping
    }

    public class NetTransferApp : IApp
    {
        public string Id => "transfer";
        public string Name => "File Transfer & OTA";
        public bool IsRealTime => false;

        private NetTransferMode _mode = NetTransferMode.Menu;
        private int _menuIndex = 0; // 0=Receive, 1=Send, 2=Ping
        private int _discoveryTick = 0;

        // Send state
        private List<string> _fileList = new List<string>();
        private int _fileSelectIndex = 0;
        private string _selectedFile = "";
        private TextEditorState _ipEditor = new TextEditorState { AllowMultiline = false, MaxLength = 15 };
        private TextEditorState _manualFileEditor = new TextEditorState { AllowMultiline = false, MaxLength = 32 };
        private bool _isManualFileEntry = false;
        private bool _sendCompleted = false;
        private bool _sendSuccess = false;
        private string _sendResult = "";

        // Ping state
        private string _pingResult = "";

        public NetTransferApp()
        {
            InitTargetIp();
        }

        private void InitTargetIp()
        {
            _ipEditor.Text = !string.IsNullOrWhiteSpace(FileTransferService.LastPeerIp)
                ? FileTransferService.LastPeerIp
                : "";
            _ipEditor.CursorPos = _ipEditor.Text.Length;
        }

        public void Draw(TuiDesktop desktop)
        {
            // Background periodic auto-discovery probe every ~2 seconds if peer is not yet known
            _discoveryTick++;
            if (_mode == NetTransferMode.Menu && !FileTransferService.IsPeerConnected && (_discoveryTick % 10 == 0))
            {
                FileTransferService.DiscoverWindowsPeer(100);
            }

            var win = desktop.CreateWindow("NETWORK FILE TRANSFER & OTA SYNC", WindowSize.Large);

            switch (_mode)
            {
                case NetTransferMode.Menu:
                    DrawMenu(win, desktop);
                    break;
                case NetTransferMode.Receive:
                    DrawReceive(win, desktop);
                    break;
                case NetTransferMode.SendSelectFile:
                    DrawSendSelectFile(win, desktop);
                    break;
                case NetTransferMode.SendEnterIp:
                    DrawSendEnterIp(win, desktop);
                    break;
                case NetTransferMode.SendingProgress:
                    DrawSendingProgress(win, desktop);
                    break;
                case NetTransferMode.Ping:
                    DrawPing(win, desktop);
                    break;
            }
        }

        private void DrawMenu(TuiWindow win, TuiDesktop desktop)
        {
            string ip = RawTcpHttp.GetAssignedIp();
            win.WriteRow(1, "Local Node IP : " + (string.IsNullOrWhiteSpace(ip) ? "0.0.0.0 (Unconfigured)" : ip), ConsoleColor.Cyan, 2);

            if (FileTransferService.IsPeerConnected)
            {
                string peerName = string.IsNullOrWhiteSpace(FileTransferService.LastPeerName) ? "Windows PC" : FileTransferService.LastPeerName;
                win.WriteRow(2, $"Remote Peer   : [CONNECTED] {peerName} ({FileTransferService.LastPeerIp})", ConsoleColor.Green, 2);
            }
            else if (FileTransferService.HasDiscoveredPeer)
            {
                win.WriteRow(2, $"Remote Peer   : [DETECTED: {FileTransferService.LastPeerIp}] Awaiting verification", ConsoleColor.Yellow, 2);
            }
            else
            {
                win.WriteRow(2, "Remote Peer   : [SEARCHING LAN...] Auto-discovery active (UDP 7331)", ConsoleColor.Yellow, 2);
            }

            win.WriteRow(3, "Protocol Port : UDP 7331 (WOSF Streaming OTA)", ConsoleColor.Cyan, 2);
            win.WriteSeparator(4);

            string[] opts = {
                "1. Receive Files from Windows PC (Start Listening)",
                "2. Send File to Windows PC (Auto-Discovers PC & Sends)",
                "3. Ping & Connection Diagnostic Test"
            };

            for (int i = 0; i < opts.Length; i++)
            {
                win.WriteSelectionRow(6 + (i * 2), opts[i], i == _menuIndex, 2);
            }

            win.WriteSeparator(12);
            string lastMsg = FileTransferService.StatusMessage;
            if (string.IsNullOrWhiteSpace(lastMsg)) lastMsg = "Ready for network transfer operations.";
            ConsoleColor msgCol = lastMsg.Contains("[SUCCESS]") ? ConsoleColor.Green : (lastMsg.Contains("[ERROR]") ? ConsoleColor.Red : ConsoleColor.Gray);
            win.WriteRow(13, "Status: " + lastMsg, msgCol, 2);

            win.WriteFooter("[Up/Dn/1-3] Select Mode  |  [Enter] Confirm  |  [ESC] Desktop", ConsoleColor.Yellow);
        }

        private void DrawReceive(TuiWindow win, TuiDesktop desktop)
        {
            win.WriteRow(1, "--- RECEIVE MODE (LISTENING ON UDP PORT 7331) ---", ConsoleColor.Yellow, 2, fillRemaining: true);
            string ip = RawTcpHttp.GetAssignedIp();
            win.WriteRow(2, "Local IP Address : " + ip + " : 7331", ConsoleColor.Cyan, 2, fillRemaining: true);

            if (FileTransferService.IsPeerConnected)
            {
                win.WriteRow(3, $"Paired Peer      : Windows PC at {FileTransferService.LastPeerIp} [ONLINE]", ConsoleColor.Green, 2, fillRemaining: true);
            }
            else
            {
                win.WriteRow(3, "Peer State       : Ready on LAN (Broadcasting UDP 7331)...", ConsoleColor.DarkCyan, 2, fillRemaining: true);
            }
            win.WriteSeparator(4);
            win.WriteRow(6, "Waiting for transmission from companion Windows app...", ConsoleColor.White, 2, fillRemaining: true);
            win.WriteRow(7, "On Windows, choose option [1] Send File in 'run-transfer.bat'.", ConsoleColor.Cyan, 2, fillRemaining: true);
            win.WriteRow(8, "", ConsoleColor.Black, 2, fillRemaining: true);
            win.DrawBar(2, 9, "Listening", 0, ConsoleColor.DarkGray);
            win.WriteFooter("[R] Restart Listener  |  [ESC] Return to Menu", ConsoleColor.Yellow);

            int lastDrawnChunk = -1;
            int lastDrawnPct = -1;
            TransferStatus lastDrawnStatus = FileTransferService.Status;

            while (_mode == NetTransferMode.Receive)
            {
                // Active bare-metal hardware packet pump
                WeatherOS.Kernel.CustomNIC?.HackPoll();

                // Non-blocking keyboard poll
                if (TuiDesktop.TryReadDesktopKey(out var keyEvent))
                {
                    if (keyEvent.Key == ConsoleKeyEx.Escape)
                    {
                        FileTransferService.StopListening();
                        _mode = NetTransferMode.Menu;
                        break;
                    }
                    if (keyEvent.KeyChar == 'r' || keyEvent.KeyChar == 'R')
                    {
                        FileTransferService.StartListening();
                        lastDrawnChunk = -1;
                        lastDrawnPct = -1;
                        lastDrawnStatus = TransferStatus.Idle;
                        win.WriteRow(6, "Waiting for transmission from companion Windows app...", ConsoleColor.White, 2, fillRemaining: true);
                        win.WriteRow(7, "On Windows, choose option [1] Send File in 'run-transfer.bat'.", ConsoleColor.Cyan, 2, fillRemaining: true);
                        win.WriteRow(8, "", ConsoleColor.Black, 2, fillRemaining: true);
                        win.WriteRow(9, "", ConsoleColor.Black, 2, fillRemaining: true);
                        win.WriteRow(10, "", ConsoleColor.Black, 2, fillRemaining: true);
                        win.WriteRow(11, "", ConsoleColor.Black, 2, fillRemaining: true);
                        win.WriteRow(13, "", ConsoleColor.Black, 2, fillRemaining: true);
                        win.DrawBar(2, 9, "Listening", 0, ConsoleColor.DarkGray);
                    }
                }

                var curStatus = FileTransferService.Status;
                int curChunk = FileTransferService.CurrentChunk;

                if (curStatus == TransferStatus.Receiving)
                {
                    int totalChunks = FileTransferService.TotalChunks;
                    int pct = totalChunks > 0 ? (curChunk * 100) / totalChunks : 0;

                    if (curStatus != lastDrawnStatus)
                    {
                        lastDrawnStatus = curStatus;
                        win.WriteRow(6, $"Receiving File: {FileTransferService.CurrentFileName}", ConsoleColor.Green, 2, fillRemaining: true);
                        win.WriteRow(7, $"From Remote PC: {FileTransferService.RemoteIpStr}", ConsoleColor.Cyan, 2, fillRemaining: true);
                    }

                    if (pct != lastDrawnPct || curChunk == totalChunks)
                    {
                        lastDrawnPct = pct;
                        lastDrawnChunk = curChunk;

                        win.WriteRow(8, $"Chunk: {curChunk} / {totalChunks} ({FileTransferService.TransferredBytes} / {FileTransferService.TotalFileSize} B)", ConsoleColor.White, 2, fillRemaining: true);
                        win.DrawBar(2, 10, "Transfer", pct, ConsoleColor.Green);
                    }
                }
                else if (curStatus != lastDrawnStatus)
                {
                    lastDrawnStatus = curStatus;
                    if (curStatus == TransferStatus.Success)
                    {
                        win.WriteRow(6, "[SUCCESS] Transfer completed successfully!", ConsoleColor.Green, 2, fillRemaining: true);
                        win.WriteRow(7, $"File Name : {FileTransferService.CurrentFileName}", ConsoleColor.White, 2, fillRemaining: true);
                        win.WriteRow(8, $"File Size : {FileTransferService.TotalFileSize} bytes", ConsoleColor.White, 2, fillRemaining: true);
                        win.WriteRow(9, "Saved to  : FAT32 Storage / RAM Virtual Disk", ConsoleColor.Cyan, 2, fillRemaining: true);
                        win.DrawBar(2, 11, "Finished", 100, ConsoleColor.Green);
                        win.WriteRow(13, "Press [R] to listen for another file.", ConsoleColor.Yellow, 2, fillRemaining: true);
                    }
                    else if (curStatus == TransferStatus.Error)
                    {
                        win.WriteRow(6, "[ERROR] Transfer failed or interrupted.", ConsoleColor.Red, 2, fillRemaining: true);
                        win.WriteRow(7, FileTransferService.StatusMessage, ConsoleColor.Red, 2, fillRemaining: true);
                        win.WriteRow(9, "Press [R] to retry listening.", ConsoleColor.Yellow, 2, fillRemaining: true);
                    }
                }

                if (curStatus != TransferStatus.Receiving)
                {
                    TuiDesktop.HardwareDelayMs(1);
                }
            }
        }

        private void DrawSendSelectFile(TuiWindow win, TuiDesktop desktop)
        {
            win.WriteRow(1, "--- SEND FILE: SELECT FILE FROM STORAGE ---", ConsoleColor.Yellow, 2);
            win.WriteSeparator(2);

            if (_isManualFileEntry)
            {
                win.WriteRow(4, "Enter file name manually:", ConsoleColor.White, 2);
                win.WriteRow(6, "> ", ConsoleColor.Yellow, 2);
                _manualFileEditor.DrawInline(win.InnerLeft + 4, win.InnerTop + 6, win.InnerWidth - 8, win.Background, ConsoleColor.White);
                win.WriteFooter("[Enter] Accept Name  |  [ESC] Cancel Manual Input", ConsoleColor.Yellow);
                return;
            }

            if (_fileList.Count == 0)
            {
                win.WriteRow(4, "No files found in storage (disk0 or RAM).", ConsoleColor.DarkYellow, 2);
                win.WriteRow(6, "Press [M] to enter a custom filename manually.", ConsoleColor.Cyan, 2);
            }
            else
            {
                int maxVisible = 7;
                int start = 0;
                if (_fileSelectIndex >= maxVisible) start = _fileSelectIndex - maxVisible + 1;

                for (int i = 0; i < maxVisible && (start + i) < _fileList.Count; i++)
                {
                    int idx = start + i;
                    bool isSel = (idx == _fileSelectIndex);
                    win.WriteSelectionRow(4 + i, _fileList[idx], isSel, 2);
                }
            }

            win.WriteSeparator(12);
            win.WriteRow(13, "[M] Enter Filename Manually", ConsoleColor.Cyan, 2);
            win.WriteFooter("[Up/Dn] Navigate  |  [Enter] Select & Auto-Send  |  [ESC] Menu", ConsoleColor.Yellow);
        }

        private void DrawSendEnterIp(TuiWindow win, TuiDesktop desktop)
        {
            win.WriteRow(1, "--- SEND FILE: PEER CONNECTION ---", ConsoleColor.Yellow, 2);
            win.WriteSeparator(2);

            win.WriteRow(4, "File Selected : " + _selectedFile, ConsoleColor.Green, 2);

            if (FileTransferService.IsPeerConnected)
            {
                win.WriteRow(6, $"Paired Peer   : Windows PC ({FileTransferService.LastPeerIp})", ConsoleColor.Cyan, 2);
                win.WriteRow(7, "Ready to transmit directly to discovered peer.", ConsoleColor.White, 2);
                win.WriteRow(9, "Press [Enter] to start sending.", ConsoleColor.Yellow, 2);
            }
            else
            {
                win.WriteRow(6, "Windows Companion not detected via auto-discovery.", ConsoleColor.DarkYellow, 2);
                win.WriteRow(7, "Make sure 'run-transfer.bat' is open on your Windows PC.", ConsoleColor.White, 2);
                win.WriteRow(9, "Press [R] to search the network again automatically.", ConsoleColor.Yellow, 2);
                win.WriteRow(11, "Or enter IP manually: ", ConsoleColor.Gray, 2);
                _ipEditor.DrawInline(win.InnerLeft + 24, win.InnerTop + 11, 16, win.Background, ConsoleColor.White);
            }

            win.WriteFooter("[R] Retry Discovery  |  [Enter] Send  |  [ESC] Cancel", ConsoleColor.Yellow);
        }

        private void DrawSendingProgress(TuiWindow win, TuiDesktop desktop)
        {
            win.WriteRow(1, "--- SENDING FILE STREAM ---", ConsoleColor.Yellow, 2);
            win.WriteSeparator(2);

            string targetDisp = !string.IsNullOrWhiteSpace(FileTransferService.LastPeerIp) ? FileTransferService.LastPeerIp : _ipEditor.Text.Trim();
            win.WriteRow(4, "File Name : " + _selectedFile, ConsoleColor.White, 2);
            win.WriteRow(5, "Target IP : " + targetDisp + " : 7331", ConsoleColor.Cyan, 2);

            if (!_sendCompleted)
            {
                win.WriteRow(7, "Transmitting data chunks over network...", ConsoleColor.Yellow, 2);
                int pct = 0;
                if (FileTransferService.TotalChunks > 0)
                {
                    pct = (FileTransferService.CurrentChunk * 100) / FileTransferService.TotalChunks;
                }
                win.DrawBar(2, 9, "Sending", pct, ConsoleColor.Cyan);
            }
            else
            {
                if (_sendSuccess)
                {
                    win.WriteRow(7, "[SUCCESS] Transmission finished!", ConsoleColor.Green, 2);
                    win.WriteRow(8, _sendResult, ConsoleColor.White, 2);
                    win.DrawBar(2, 10, "Complete", 100, ConsoleColor.Green);
                }
                else
                {
                    win.WriteRow(7, "[ERROR] Transmission failed!", ConsoleColor.Red, 2);
                    win.WriteRow(8, _sendResult, ConsoleColor.Red, 2);
                    win.DrawBar(2, 10, "Failed", 0, ConsoleColor.Red);
                }
                win.WriteRow(12, "Press [Enter] or [ESC] to return to menu.", ConsoleColor.Yellow, 2);
            }

            win.WriteFooter("[ESC] Return to Menu", ConsoleColor.Yellow);
        }

        private void DrawPing(TuiWindow win, TuiDesktop desktop)
        {
            win.WriteRow(1, "--- NETWORK PING & PEER TEST ---", ConsoleColor.Yellow, 2);
            win.WriteSeparator(2);

            if (string.IsNullOrWhiteSpace(_ipEditor.Text) && !string.IsNullOrWhiteSpace(FileTransferService.LastPeerIp))
            {
                _ipEditor.Text = FileTransferService.LastPeerIp;
                _ipEditor.CursorPos = _ipEditor.Text.Length;
            }

            win.WriteRow(4, "Target PC IP : ", ConsoleColor.White, 2);
            _ipEditor.DrawInline(win.InnerLeft + 16, win.InnerTop + 4, 18, win.Background, ConsoleColor.White);

            if (!string.IsNullOrWhiteSpace(FileTransferService.LastPeerIp))
            {
                if (FileTransferService.IsPeerVerified)
                {
                    string peerName = string.IsNullOrWhiteSpace(FileTransferService.LastPeerName) ? "Windows PC" : FileTransferService.LastPeerName;
                    win.WriteRow(5, $"Peer status  : [ONLINE & VERIFIED] {peerName}", ConsoleColor.Green, 2);
                }
                else if (!string.IsNullOrWhiteSpace(_pingResult) && _pingResult.Contains("FAILED"))
                {
                    win.WriteRow(5, "Peer status  : [UNREACHABLE / TIMED OUT]", ConsoleColor.Red, 2);
                }
                else
                {
                    win.WriteRow(5, "Peer status  : [DETECTED ON LAN - Press Enter to verify]", ConsoleColor.Yellow, 2);
                }
            }
            else
            {
                win.WriteRow(5, "Peer status  : [SEARCHING LAN... Press R to rescan]", ConsoleColor.DarkYellow, 2);
            }

            win.WriteSeparator(7);
            if (!string.IsNullOrWhiteSpace(_pingResult))
            {
                ConsoleColor col = _pingResult.Contains("SUCCESS") || _pingResult.Contains("ms") ? ConsoleColor.Green : ConsoleColor.Red;
                win.WriteRow(9, "Result: " + _pingResult, col, 2);
            }
            else
            {
                win.WriteRow(9, "Press [Enter] to send test UDP ping to target IP.", ConsoleColor.Gray, 2);
            }

            win.WriteFooter("[Enter] Ping Host  |  [R] Rescan Network  |  [ESC] Return to Menu", ConsoleColor.Yellow);
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                HandleEscape(desktop);
                return;
            }

            switch (_mode)
            {
                case NetTransferMode.Menu:
                    HandleMenuInput(desktop, keyEvent);
                    break;
                case NetTransferMode.Receive:
                    HandleReceiveInput(desktop, keyEvent);
                    break;
                case NetTransferMode.SendSelectFile:
                    HandleSendSelectFileInput(desktop, keyEvent);
                    break;
                case NetTransferMode.SendEnterIp:
                    HandleSendEnterIpInput(desktop, keyEvent);
                    break;
                case NetTransferMode.SendingProgress:
                    if (keyEvent.Key == ConsoleKeyEx.Enter) _mode = NetTransferMode.Menu;
                    break;
                case NetTransferMode.Ping:
                    HandlePingInput(desktop, keyEvent);
                    break;
            }
        }

        private void HandleEscape(TuiDesktop desktop)
        {
            if (_mode == NetTransferMode.Menu)
            {
                desktop.SetCurrentApp("launcher");
            }
            else if (_mode == NetTransferMode.Receive)
            {
                FileTransferService.StopListening();
                _mode = NetTransferMode.Menu;
            }
            else if (_mode == NetTransferMode.SendSelectFile)
            {
                if (_isManualFileEntry)
                {
                    _isManualFileEntry = false;
                }
                else
                {
                    _mode = NetTransferMode.Menu;
                }
            }
            else if (_mode == NetTransferMode.SendEnterIp)
            {
                _mode = NetTransferMode.SendSelectFile;
            }
            else if (_mode == NetTransferMode.SendingProgress)
            {
                _mode = NetTransferMode.Menu;
            }
            else if (_mode == NetTransferMode.Ping)
            {
                _mode = NetTransferMode.Menu;
            }
        }

        private void HandleMenuInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.UpArrow)
            {
                _menuIndex--;
                if (_menuIndex < 0) _menuIndex = 2;
            }
            else if (keyEvent.Key == ConsoleKeyEx.DownArrow)
            {
                _menuIndex++;
                if (_menuIndex > 2) _menuIndex = 0;
            }
            else if (keyEvent.KeyChar == '1')
            {
                _menuIndex = 0;
                ActivateMenuOption(desktop);
            }
            else if (keyEvent.KeyChar == '2')
            {
                _menuIndex = 1;
                ActivateMenuOption(desktop);
            }
            else if (keyEvent.KeyChar == '3')
            {
                _menuIndex = 2;
                ActivateMenuOption(desktop);
            }
            else if (keyEvent.Key == ConsoleKeyEx.Enter)
            {
                ActivateMenuOption(desktop);
            }
        }

        private void ActivateMenuOption(TuiDesktop desktop)
        {
            if (_menuIndex == 0)
            {
                // Start Receive
                _mode = NetTransferMode.Receive;
                FileTransferService.StartListening();
            }
            else if (_menuIndex == 1)
            {
                // Start Send
                _mode = NetTransferMode.SendSelectFile;
                _fileList = StorageService.GetFileNamesOnly();
                _fileSelectIndex = 0;
                _isManualFileEntry = false;
                InitTargetIp();
            }
            else if (_menuIndex == 2)
            {
                // Ping
                _mode = NetTransferMode.Ping;
                _pingResult = "";
                InitTargetIp();
            }
        }

        private void HandleReceiveInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.KeyChar == 'r' || keyEvent.KeyChar == 'R')
            {
                FileTransferService.StartListening();
            }
        }

        private void HandleSendSelectFileInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (_isManualFileEntry)
            {
                if (keyEvent.Key == ConsoleKeyEx.Enter)
                {
                    if (!string.IsNullOrWhiteSpace(_manualFileEditor.Text))
                    {
                        _selectedFile = _manualFileEditor.Text.Trim();
                        _isManualFileEntry = false;
                        ProceedAfterFileSelected();
                    }
                }
                else
                {
                    _manualFileEditor.HandleKey(keyEvent);
                }
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.UpArrow)
            {
                if (_fileList.Count > 0)
                {
                    _fileSelectIndex--;
                    if (_fileSelectIndex < 0) _fileSelectIndex = _fileList.Count - 1;
                }
            }
            else if (keyEvent.Key == ConsoleKeyEx.DownArrow)
            {
                if (_fileList.Count > 0)
                {
                    _fileSelectIndex++;
                    if (_fileSelectIndex >= _fileList.Count) _fileSelectIndex = 0;
                }
            }
            else if (keyEvent.KeyChar == 'm' || keyEvent.KeyChar == 'M')
            {
                _isManualFileEntry = true;
                _manualFileEditor.Text = "";
                _manualFileEditor.CursorPos = 0;
            }
            else if (keyEvent.Key == ConsoleKeyEx.Enter)
            {
                if (_fileList.Count > 0 && _fileSelectIndex >= 0 && _fileSelectIndex < _fileList.Count)
                {
                    _selectedFile = _fileList[_fileSelectIndex];
                    ProceedAfterFileSelected();
                }
            }
        }

        private void ProceedAfterFileSelected()
        {
            // Auto-discovery: If peer is not known yet, try quick network probe
            if (string.IsNullOrWhiteSpace(FileTransferService.LastPeerIp))
            {
                FileTransferService.DiscoverWindowsPeer(1200);
            }

            if (!string.IsNullOrWhiteSpace(FileTransferService.LastPeerIp))
            {
                // Peer found! Send immediately without manual input!
                StartSendingFile(FileTransferService.LastPeerIp);
            }
            else
            {
                // Peer not found yet, allow retry or optional manual input
                _mode = NetTransferMode.SendEnterIp;
            }
        }

        private void HandleSendEnterIpInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.KeyChar == 'r' || keyEvent.KeyChar == 'R')
            {
                // Retry auto-discovery
                FileTransferService.DiscoverWindowsPeer(1500);
                if (!string.IsNullOrWhiteSpace(FileTransferService.LastPeerIp))
                {
                    StartSendingFile(FileTransferService.LastPeerIp);
                }
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.Enter)
            {
                string targetIp = !string.IsNullOrWhiteSpace(_ipEditor.Text)
                    ? _ipEditor.Text.Trim()
                    : FileTransferService.LastPeerIp;

                if (!string.IsNullOrWhiteSpace(targetIp))
                {
                    StartSendingFile(targetIp);
                }
            }
            else
            {
                _ipEditor.HandleKey(keyEvent);
            }
        }

        private void StartSendingFile(string targetIp)
        {
            _mode = NetTransferMode.SendingProgress;
            _sendCompleted = false;
            _sendSuccess = false;

            // Load binary file
            byte[] fileBytes;
            string loadStatus;
            bool loaded = StorageService.LoadBinaryFileFromLocation(_selectedFile, "disk0", out fileBytes, out loadStatus);
            if (!loaded || fileBytes == null || fileBytes.Length == 0)
            {
                loaded = StorageService.TryGetMemoryBinaryFile(_selectedFile, out fileBytes);
            }

            if (!loaded || fileBytes == null || fileBytes.Length == 0)
            {
                _sendCompleted = true;
                _sendSuccess = false;
                _sendResult = $"Could not read '{_selectedFile}': not found or empty.";
                return;
            }

            // Execute Send
            string status;
            bool ok = FileTransferService.SendFile(targetIp, 7331, _selectedFile, fileBytes, (chunk, total) => { }, out status);

            _sendCompleted = true;
            _sendSuccess = ok;
            _sendResult = status;
        }

        private void HandlePingInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.KeyChar == 'r' || keyEvent.KeyChar == 'R')
            {
                _pingResult = "Scanning network...";
                FileTransferService.DiscoverWindowsPeer(1500);
                if (!string.IsNullOrWhiteSpace(FileTransferService.LastPeerIp))
                {
                    _ipEditor.Text = FileTransferService.LastPeerIp;
                    _ipEditor.CursorPos = _ipEditor.Text.Length;
                    _pingResult = $"[FOUND] Auto-discovered Windows PC at {FileTransferService.LastPeerIp}!";
                }
                else
                {
                    _pingResult = "[NOT FOUND] No peers responded to broadcast discovery.";
                }
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.Enter)
            {
                string targetIp = !string.IsNullOrWhiteSpace(_ipEditor.Text)
                    ? _ipEditor.Text.Trim()
                    : FileTransferService.LastPeerIp;

                if (string.IsNullOrWhiteSpace(targetIp))
                {
                    _pingResult = "Searching LAN for peers...";
                    FileTransferService.DiscoverWindowsPeer(1500);
                    targetIp = FileTransferService.LastPeerIp;
                    if (!string.IsNullOrWhiteSpace(targetIp))
                    {
                        _ipEditor.Text = targetIp;
                        _ipEditor.CursorPos = _ipEditor.Text.Length;
                    }
                }

                if (string.IsNullOrWhiteSpace(targetIp))
                {
                    _pingResult = "[FAILED] No peer found on local network to ping.";
                    return;
                }

                _pingResult = "Sending WOSF ping to " + targetIp + "...";
                int rtt;
                string reply;
                bool ok = FileTransferService.SendPing(targetIp, 7331, out rtt, out reply);
                if (ok)
                {
                    FileTransferService.LastPeerIp = targetIp;
                    _pingResult = $"[SUCCESS] Reply from {targetIp} in {rtt} ms! Info: {reply}";
                }
                else
                {
                    _pingResult = "[FAILED] Request timed out. No response from " + targetIp;
                }
            }
            else
            {
                _ipEditor.HandleKey(keyEvent);
            }
        }
    }
}
