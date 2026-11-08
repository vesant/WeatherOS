using System;
using Cosmos.HAL;
using Cosmos.System;
using WeatherOS.Graphics;
using WeatherOS.Services;

namespace WeatherOS.Apps
{
    public class NetworkConfigApp : IApp
    {
        public string Id => "ipconfig";
        public string Name => "Network Config";
        public bool IsRealTime => false;

        public void Draw(TuiDesktop desktop)
        {
            var win = desktop.CreateWindow("NETWORK CONFIGURATION", WindowSize.Medium);

            string activeNicName = Kernel.ActiveNetDevice != null ? Kernel.ActiveNetDevice.Name : "Auto/None";
            bool isWifiActive = Kernel.ActiveNetDevice is Cosmos.HAL.Network.AtherosAR2425Driver;

            string ethStatus = (Kernel.CustomNIC != null && Kernel.CustomNIC.Ready) ? "RTL8102E Fast Ethernet [Detected]" : "Not Found";
            string wifiStatus = (Kernel.CustomWifi != null && Kernel.CustomWifi.Ready) 
                ? (WiFiService.IsConnected ? "Atheros AR2425 [Connected to '" + WiFiService.CurrentSsid + "']" : "Atheros AR2425 [Disconnected]") 
                : "Not Found";

            win.WriteRow(1, "Active Interface: " + (isWifiActive ? "Wi-Fi (Wireless LAN)" : "Ethernet (Wired LAN)"), ConsoleColor.Green, 2);
            win.WriteRow(2, "Ethernet Adapter: " + ethStatus, ConsoleColor.Cyan, 2);
            win.WriteRow(3, "Wireless Adapter: " + wifiStatus, ConsoleColor.Cyan, 2);

            string ipText = desktop.ShowDebugLogs ? RawTcpHttp.GetAssignedIp() : RawTcpHttp.GetMaskedIp();
            win.WriteRow(5, "IP Address      : " + ipText, ConsoleColor.White, 2);

            string gwText = desktop.ShowDebugLogs ? RawTcpHttp.GetGatewayIp() : "***.***.***.*** (Secured)";
            win.WriteRow(6, "Default Gateway : " + gwText, ConsoleColor.White, 2);

            win.WriteRow(7, "Subnet Mask     : 255.255.255.0", ConsoleColor.White, 2);
            win.WriteFooter("[W] Open Wi-Fi Manager  |  [F1] Commands  |  [ESC] Return to Desktop", ConsoleColor.Yellow);
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                desktop.SetCurrentApp("launcher");
                return;
            }

            if (keyEvent.Key == ConsoleKeyEx.W)
            {
                desktop.SetCurrentApp("wifi");
                return;
            }
        }
    }
}
