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
            win.WriteRow(1, "Adapter    : " + (NetworkDevice.Devices.Count > 0 ? "RTL8102E Bare-Metal" : "None"), ConsoleColor.Cyan, 2);

            string ipText = desktop.ShowDebugLogs ? RawTcpHttp.GetAssignedIp() : RawTcpHttp.GetMaskedIp();
            win.WriteRow(3, "IP Address : " + ipText, ConsoleColor.Cyan, 2);

            string gwText = desktop.ShowDebugLogs ? RawTcpHttp.GetGatewayIp() : "***.***.***.*** (Secured)";
            win.WriteRow(4, "Gateway    : " + gwText, ConsoleColor.Cyan, 2);

            win.WriteRow(5, "Subnet Mask: 255.255.255.0", ConsoleColor.Cyan, 2);
            win.WriteFooter("[ESC] Return to Desktop", ConsoleColor.Yellow);
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                desktop.SetCurrentApp("launcher");
            }
        }
    }
}
