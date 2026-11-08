using System;
using System.Collections.Generic;
using Cosmos.HAL.Network;
using WeatherOS.Services;

namespace WeatherOS.Services
{
    public static class WiFiService
    {
        private const string WIFI_CONFIG_FILE = "wifi.ini";

        public static AtherosAR2425Driver Driver => Kernel.CustomWifi;

        public static bool IsHardwareAvailable => Driver != null && Driver.IsInitialized;

        public static bool IsConnected => Driver != null && Driver.IsConnected;

        public static WifiAccessPoint CurrentNetwork => Driver?.ConnectedAp;

        public static string CurrentSsid => CurrentNetwork?.Ssid ?? (IsConnected ? "Connected" : "Disconnected");

        public static int CurrentRssi => CurrentNetwork?.RssiDbm ?? -95;

        public static string CurrentBssid => CurrentNetwork?.Bssid ?? "00:00:00:00:00:00";

        public static void ScanNetworks()
        {
            if (Driver != null && Driver.IsInitialized)
            {
                Driver.EnsureRadioActive();
                Driver.ScanAllChannels();
            }
        }

        public static List<WifiAccessPoint> GetDetectedNetworks()
        {
            var list = new List<WifiAccessPoint>();
            if (Driver == null || Driver.DetectedAps == null) return list;

            foreach (var kvp in Driver.DetectedAps)
            {
                if (kvp.Value != null)
                {
                    list.Add(kvp.Value);
                }
            }

            // Sort by signal strength (strongest first) without triggering ArraySortHelper IL2CPU generic plug
            for (int i = 0; i < list.Count - 1; i++)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    if (list[j].RssiDbm > list[i].RssiDbm)
                    {
                        var tmp = list[i];
                        list[i] = list[j];
                        list[j] = tmp;
                    }
                }
            }
            return list;
        }

        public static bool Connect(WifiAccessPoint ap, string password, Action<string> statusCallback = null)
        {
            if (Driver == null || !Driver.IsInitialized || ap == null)
            {
                statusCallback?.Invoke("[!] Atheros wireless adapter not available.");
                return false;
            }

            Driver.EnsureRadioActive();
            bool connected = Driver.Connect(ap, password, statusCallback);
            if (connected)
            {
                // Commit credentials to non-volatile wifi.ini
                SaveCredentials(ap.Ssid, password, true);

                statusCallback?.Invoke("[5/5] Settle router bridge and negotiate DHCP over 802.11...");
                Kernel.ActiveNetDevice = Driver;

                // Settle delay (500ms) to allow home router to open 802.1X bridge port to FORWARDING
                for (int s = 0; s < 20; s++)
                {
                    Driver.PollRx();
                    RawTcpHttp.HardwareDelayMs(25);
                }

                // Execute DHCP over Wi-Fi
                RawTcpHttp.DoAutoConfig(Driver);

                string assignedIp = RawTcpHttp.GetAssignedIp();
                if (assignedIp == "0.0.0.0")
                {
                    // If DHCP was slow on first attempt, retry with extra settle time
                    statusCallback?.Invoke("[5/5] Re-requesting DHCP lease from router bridge...");
                    for (int s = 0; s < 20; s++)
                    {
                        Driver.PollRx();
                        RawTcpHttp.HardwareDelayMs(25);
                    }
                    RawTcpHttp.DoAutoConfig(Driver);
                    assignedIp = RawTcpHttp.GetAssignedIp();
                }

                if (assignedIp != "0.0.0.0")
                {
                    statusCallback?.Invoke($"[OK] Online! Bound to IP {assignedIp} (Gateway: {RawTcpHttp.GetGatewayIp()})!");
                    return true;
                }
                else
                {
                    statusCallback?.Invoke("[!] Associated with Wi-Fi, but router did not offer a DHCP lease.");
                    return false;
                }
            }

            return false;
        }

        public static void Disconnect()
        {
            if (Driver != null)
            {
                Driver.Disconnect();
            }

            // Fallback to Ethernet if present
            if (Kernel.CustomNIC != null && Kernel.CustomNIC.Ready)
            {
                Kernel.ActiveNetDevice = Kernel.CustomNIC;
                RawTcpHttp.DoAutoConfig(Kernel.CustomNIC);
            }
        }

        public static void LoadSavedCredentials(out string ssid, out string password, out bool autoConnect)
        {
            ssid = "";
            password = "";
            autoConnect = false;

            try
            {
                string content = "";
                // 1. Check direct VFS filesystem
                if (StorageService.IsVfsAvailable())
                {
                    try
                    {
                        if (System.IO.File.Exists("0:\\" + WIFI_CONFIG_FILE))
                        {
                            content = System.IO.File.ReadAllText("0:\\" + WIFI_CONFIG_FILE);
                        }
                    }
                    catch { }
                }

                // 2. Check StorageService (RAM + FAT32 fallback)
                if (string.IsNullOrEmpty(content))
                {
                    string stat;
                    StorageService.LoadFile(WIFI_CONFIG_FILE, out content, out stat);
                }

                if (!string.IsNullOrEmpty(content))
                {
                    string[] lines = content.Split('\n');
                    foreach (var rawLine in lines)
                    {
                        string line = rawLine.Trim();
                        if (line.StartsWith("SSID="))
                        {
                            ssid = line.Substring(5).Trim();
                        }
                        else if (line.StartsWith("Password="))
                        {
                            password = line.Substring(9).Trim();
                        }
                        else if (line.StartsWith("AutoConnect="))
                        {
                            autoConnect = line.Substring(12).Trim().ToLower() == "true";
                        }
                    }
                }
            }
            catch { }
        }

        public static void SaveCredentials(string ssid, string password, bool autoConnect = true)
        {
            try
            {
                string content = "[WiFi]\n" +
                                 "AutoConnect=" + (autoConnect ? "true" : "false") + "\n" +
                                 "SSID=" + (ssid ?? "") + "\n" +
                                 "Password=" + (password ?? "") + "\n" +
                                 "SavedDate=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n";
                string stat;

                // 1. Write to VFS if mounted
                if (StorageService.IsVfsAvailable())
                {
                    try { System.IO.File.WriteAllText("0:\\" + WIFI_CONFIG_FILE, content); } catch { }
                }

                // 2. Write to disk0 physical partition
                StorageService.SaveFileToLocation(WIFI_CONFIG_FILE, content, "disk0", out stat);
                // 3. Keep RAM in sync
                StorageService.PutMemoryFile(WIFI_CONFIG_FILE, content);
            }
            catch { }
        }

        public static void ClearSavedCredentials()
        {
            try
            {
                string stat;
                StorageService.DeleteFile(WIFI_CONFIG_FILE, out stat);
            }
            catch { }
        }

        public static bool AttemptAutoConnect(Action<string> statusCallback = null)
        {
            if (!IsHardwareAvailable) return false;

            string ssid, password;
            bool autoConnect;
            LoadSavedCredentials(out ssid, out password, out autoConnect);

            if (!autoConnect || string.IsNullOrEmpty(ssid)) return false;

            statusCallback?.Invoke($"[net] Probing air for saved Wi-Fi '{ssid}'...");
            ScanNetworks();

            var aps = GetDetectedNetworks();
            WifiAccessPoint targetAp = null;
            foreach (var ap in aps)
            {
                if (string.Equals(ap.Ssid, ssid, StringComparison.OrdinalIgnoreCase))
                {
                    targetAp = ap;
                    break;
                }
            }

            if (targetAp != null)
            {
                statusCallback?.Invoke($"[net] Saved network '{ssid}' found (Ch {targetAp.Channel}, {targetAp.RssiDbm} dBm). Connecting...");
                return Connect(targetAp, password, statusCallback);
            }
            else
            {
                statusCallback?.Invoke($"[net] Saved network '{ssid}' not in range.");
                return false;
            }
        }

        public static string FormatSignalBars(int rssiDbm)
        {
            if (rssiDbm >= -55) return "[|||||]";
            if (rssiDbm >= -67) return "[|||| ]";
            if (rssiDbm >= -75) return "[|||  ]";
            if (rssiDbm >= -85) return "[||   ]";
            return "[|    ]";
        }

        public static int CalculateSignalPercent(int rssiDbm)
        {
            if (rssiDbm >= -50) return 100;
            if (rssiDbm <= -100) return 0;
            return 2 * (rssiDbm + 100);
        }
    }
}
