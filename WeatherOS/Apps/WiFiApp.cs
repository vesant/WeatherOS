using System;
using System.Collections.Generic;
using Cosmos.HAL.Network;
using Cosmos.System;
using WeatherOS.Graphics;
using WeatherOS.Services;

namespace WeatherOS.Apps
{
    public class WiFiApp : IApp
    {
        public string Id => "wifi";
        public string Name => "Wi-Fi Manager";
        public bool IsRealTime => true;

        private int _selectedIdx = 0;
        private bool _isPasswordModalOpen = false;
        private bool _isConnecting = false;
        private string _connectProgressMsg = "";
        private string _passwordInput = "";
        private TextEditorState _passwordEditor = new TextEditorState { AllowMultiline = false, MaxLength = 63 };
        private uint _autoScanTick = 0;
        private bool _initialScanDone = false;

        public void Draw(TuiDesktop desktop)
        {
            var win = desktop.CreateWindow("WI-FI WIRELESS NETWORK MANAGER", WindowSize.Large);

            // Ensure Atheros radio is active when viewing Wi-Fi Manager
            if (WiFiService.Driver != null && WiFiService.Driver.IsInitialized && !WiFiService.Driver.IsRadioActive)
            {
                WiFiService.Driver.EnsureRadioActive();
            }

            // 1. Adapter & Connection Status Ribbon
            string devSummary = WiFiService.IsHardwareAvailable
                ? "Atheros AR2425 802.11b/g (" + WiFiService.Driver.GetMacString() + ")"
                : "No Atheros AR2425 Wi-Fi Hardware Detected";

            string connStatus = WiFiService.IsConnected
                ? "CONNECTED to '" + WiFiService.CurrentSsid + "' (" + WiFiService.CurrentRssi + " dBm)"
                : "DISCONNECTED";

            ConsoleColor statusColor = WiFiService.IsConnected ? ConsoleColor.Green : ConsoleColor.Yellow;

            win.WriteRow(1, "Adapter : " + devSummary, ConsoleColor.Cyan, 2);
            win.WriteRow(2, "Status  : " + connStatus + " | IP: " + RawTcpHttp.GetAssignedIp(), statusColor, 2);
            win.WriteRow(3, new string('=', 74), ConsoleColor.DarkGray, 2);

            // 2. Wi-Fi Access Points Table
            var aps = WiFiService.GetDetectedNetworks();

            // Auto-scan on app entry so networks appear immediately without pressing [R]
            if (!_initialScanDone && WiFiService.IsHardwareAvailable)
            {
                _initialScanDone = true;
                WiFiService.ScanNetworks();
                aps = WiFiService.GetDetectedNetworks();
            }

            if (aps.Count == 0)
            {
                win.WriteRow(6, "No Wi-Fi networks in range. Atheros radio is listening...", ConsoleColor.Yellow, 4);
                win.WriteRow(8, "-> Press [R] to sweep RF Channels 1 to 13 and transmit active probes.", ConsoleColor.White, 4);
                win.WriteRow(9, "-> Press [A] to add a hidden network (SSID) manually.", ConsoleColor.White, 4);
                win.WriteRow(11, "Atheros Driver Ready: " + (WiFiService.IsHardwareAvailable ? "YES" : "NO"), ConsoleColor.DarkGray, 4);
            }
            else
            {
                if (_selectedIdx >= aps.Count) _selectedIdx = aps.Count - 1;
                if (_selectedIdx < 0) _selectedIdx = 0;

                int pageSize = 6;
                int totalPages = (aps.Count + pageSize - 1) / pageSize;
                int currentPage = _selectedIdx / pageSize;
                int start = currentPage * pageSize;
                int end = Math.Min(start + pageSize, aps.Count);

                // Table Header
                string th = "   SIGNAL    RSSI   CHAN  SECURITY    BSSID              SSID";
                win.WriteRow(4, th, ConsoleColor.DarkCyan, 2);

                for (int i = start; i < end; i++)
                {
                    var ap = aps[i];
                    bool isSel = (i == _selectedIdx);

                    string cursor = isSel ? " > " : "   ";
                    string bars = WiFiService.FormatSignalBars(ap.RssiDbm);
                    string rssi = (ap.RssiDbm + "dBm").PadRight(7);
                    string ch = ("Ch " + ap.Channel).PadRight(6);
                    string sec = (ap.Security ?? "OPEN").PadRight(11);
                    if (sec.Length > 11) sec = sec.Substring(0, 11);
                    string bssid = (ap.Bssid ?? "").PadRight(18);
                    string ssid = ap.Ssid ?? "<Hidden>";
                    if (ap.IsConnected) ssid += " [CONNECTED]";

                    string row = cursor + bars + " " + rssi + " " + ch + " " + sec + " " + bssid + " " + ssid;
                    if (row.Length > 74) row = row.Substring(0, 74);

                    ConsoleColor fg = isSel ? ConsoleColor.White : (ap.IsConnected ? ConsoleColor.Green : ConsoleColor.Gray);
                    win.WriteRow(5 + (i - start), row, fg, 2);
                }

                win.WriteRow(11, "Page " + (currentPage + 1) + "/" + totalPages + " (Total: " + aps.Count + " networks) | Use Arrows [Up/Down]", ConsoleColor.DarkGray, 2);

                // 3. Detailed Technical Telemetry for selected AP
                if (_selectedIdx >= 0 && _selectedIdx < aps.Count)
                {
                    var sel = aps[_selectedIdx];
                    int quality = WiFiService.CalculateSignalPercent(sel.RssiDbm);
                    int snr = sel.RssiDbm - sel.NoiseDbm;

                    win.WriteRow(12, new string('-', 74), ConsoleColor.DarkGray, 2);
                    string det1 = "Target : " + sel.Ssid + " | Ch " + sel.Channel + " (" + sel.FrequencyMHz + " MHz) | Signal Quality: " + quality + "% (SNR: +" + snr + " dB)";
                    win.WriteRow(13, det1, ConsoleColor.Yellow, 2);

                    string det2 = "Cipher : " + sel.Cipher + " (" + sel.AuthType + ") | Antennas: Main " + sel.Antenna1Dbm + " dBm / Aux " + sel.Antenna2Dbm + " dBm";
                    win.WriteRow(14, det2, ConsoleColor.Gray, 2);
                }
            }

            win.WriteFooter("[Enter] Connect  |  [R] Rescan  |  [D] Disconnect  |  [F1] Commands  |  [ESC] Exit", ConsoleColor.Yellow);

            // Password Modal Overlay
            if (_isPasswordModalOpen && _selectedIdx >= 0 && _selectedIdx < aps.Count)
            {
                var sel = aps[_selectedIdx];
                int left, top;
                int modalW = 54;
                int modalH = 8;
                desktop.DrawCenteredWindow(modalW, modalH, "ENTER WI-FI PASSWORD", out left, out top);
                ConsoleColor bg = desktop.IsDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;

                desktop.WriteInsideWindow(left, modalW, top + 2, 2, "Network : " + sel.Ssid + " (" + sel.Security + ")", ConsoleColor.Yellow, bg);
                desktop.WriteInsideWindow(left, modalW, top + 4, 2, "Password: ", ConsoleColor.White, bg);
                _passwordEditor.DrawInline(left + 12, top + 4, modalW - 16, bg, ConsoleColor.White);
                desktop.WriteInsideWindow(left, modalW, top + 6, 2, "[Enter] Connect  |  [ESC] Cancel", ConsoleColor.Cyan, bg);
            }

            // Connecting Modal Overlay
            if (_isConnecting)
            {
                int left, top;
                int modalW = 56;
                int modalH = 7;
                desktop.DrawCenteredWindow(modalW, modalH, "AUTHENTICATING 802.11 WI-FI", out left, out top);
                ConsoleColor bg = desktop.IsDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;

                desktop.WriteInsideWindow(left, modalW, top + 2, 2, "Please wait, negotiating handshake...", ConsoleColor.Yellow, bg);
                string pMsg = _connectProgressMsg;
                if (pMsg.Length > modalW - 6) pMsg = pMsg.Substring(0, modalW - 6);
                desktop.WriteInsideWindow(left, modalW, top + 4, 2, pMsg, ConsoleColor.Green, bg);
            }
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (_isConnecting) return;

            // 1. Password Modal Input
            if (_isPasswordModalOpen)
            {
                if (keyEvent.Key == ConsoleKeyEx.Escape)
                {
                    _isPasswordModalOpen = false;
                    _passwordEditor.Text = "";
                    _passwordEditor.CursorPos = 0;
                    return;
                }

                if (keyEvent.Key == ConsoleKeyEx.Enter)
                {
                    string pwd = _passwordEditor.Text ?? "";
                    var aps = WiFiService.GetDetectedNetworks();
                    if (_selectedIdx >= 0 && _selectedIdx < aps.Count)
                    {
                        var targetAp = aps[_selectedIdx];
                        _isPasswordModalOpen = false;
                        _isConnecting = true;
                        _connectProgressMsg = "Starting handshake with " + targetAp.Ssid + "...";

                        // Connect and update state
                        ExecuteConnect(targetAp, pwd);
                        _isConnecting = false;
                        _passwordEditor.Text = "";
                        _passwordEditor.CursorPos = 0;
                    }
                    return;
                }

                _passwordEditor.HandleKey(keyEvent);
                return;
            }

            // 2. Normal List Navigation
            var netList = WiFiService.GetDetectedNetworks();

            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                desktop.SetCurrentApp("launcher");
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.UpArrow)
            {
                if (netList.Count > 0)
                {
                    _selectedIdx--;
                    if (_selectedIdx < 0) _selectedIdx = netList.Count - 1;
                }
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.DownArrow)
            {
                if (netList.Count > 0)
                {
                    _selectedIdx++;
                    if (_selectedIdx >= netList.Count) _selectedIdx = 0;
                }
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.R)
            {
                // Rescan all channels
                WiFiService.ScanNetworks();
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.D)
            {
                // Disconnect
                WiFiService.Disconnect();
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.Enter || keyEvent.Key == ConsoleKeyEx.C)
            {
                if (_selectedIdx >= 0 && _selectedIdx < netList.Count)
                {
                    var ap = netList[_selectedIdx];
                    if (ap.IsOpen)
                    {
                        _isConnecting = true;
                        _connectProgressMsg = "Associating to open network...";
                        ExecuteConnect(ap, "");
                        _isConnecting = false;
                    }
                    else
                    {
                        // Check if we already have saved password for this network
                        string savedSsid, savedPwd;
                        bool auto;
                        WiFiService.LoadSavedCredentials(out savedSsid, out savedPwd, out auto);

                        if (string.Equals(savedSsid, ap.Ssid, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(savedPwd))
                        {
                            _passwordEditor.Text = savedPwd;
                            _passwordEditor.CursorPos = savedPwd.Length;
                        }
                        else
                        {
                            _passwordEditor.Text = "";
                            _passwordEditor.CursorPos = 0;
                        }
                        _isPasswordModalOpen = true;
                    }
                }
                return;
            }
        }

        private void ExecuteConnect(WifiAccessPoint ap, string password)
        {
            WiFiService.Connect(ap, password, msg =>
            {
                _connectProgressMsg = msg;
            });
        }
    }
}
