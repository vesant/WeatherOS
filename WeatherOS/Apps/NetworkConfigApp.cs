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
            desktop.DrawWindow(15, 6, 50, 10, "NETWORK CONFIGURATION");
            ConsoleColor winBg = desktop.IsDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;
            System.Console.ForegroundColor = ConsoleColor.Cyan;

            System.Console.SetCursorPosition(20, 9);
            System.Console.Write("Adapter: " + (NetworkDevice.Devices.Count > 0 ? "RTL8102E Bare-Metal" : "None"));

            System.Console.SetCursorPosition(20, 11);
            if (desktop.ShowDebugLogs)
                System.Console.Write("IP Address : " + RawTcpHttp.GetAssignedIp());
            else
                System.Console.Write("IP Address : " + RawTcpHttp.GetMaskedIp());

            System.Console.SetCursorPosition(20, 12);
            if (desktop.ShowDebugLogs)
                System.Console.Write("Gateway    : " + RawTcpHttp.GetGatewayIp());
            else
                System.Console.Write("Gateway    : ***.***.***.*** (Secured)");

            System.Console.SetCursorPosition(20, 13);
            System.Console.Write("Subnet Mask: 255.255.255.0");
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
        }
    }
}
