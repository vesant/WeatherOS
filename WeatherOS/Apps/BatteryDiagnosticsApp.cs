using System;
using Cosmos.System;
using WeatherOS.Graphics;
using WeatherOS.Services;

namespace WeatherOS.Apps
{
    public class BatteryDiagnosticsApp : IApp
    {
        public string Id => "battery";
        public string Name => "Battery & Power Diagnostics";
        public bool IsRealTime => true;

        private string _lastActionStatus = "Live hardware telemetry polling active.";

        public void Draw(TuiDesktop desktop)
        {
            int winWidth = 66;
            int winHeight = 17;
            int left, top;
            desktop.DrawCenteredWindow(winWidth, winHeight, "ACPI POWER & BATTERY TELEMETRY", out left, out top);
            ConsoleColor winBg = desktop.IsDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;

            var info = BatteryService.GetBatteryInfo();

            // Row 1: Power Source & Status
            string pwr = info.IsAcConnected ? "AC Mains Adapter (Wall Power - Charging)" : "Battery (Discharging / Battery Mode)";
            desktop.WriteInsideWindow(left, winWidth, top + 2, 2, "Power Source      : " + pwr, info.IsAcConnected ? ConsoleColor.Green : ConsoleColor.Yellow, winBg);

            // Row 2: Status & Health
            desktop.WriteInsideWindow(left, winWidth, top + 3, 2, "Operation State   : " + info.Status, ConsoleColor.White, winBg);

            // Row 3: Battery Charge Level
            desktop.WriteInsideWindow(left, winWidth, top + 5, 2, $"Battery Charge    : {info.Percentage}%", info.Percentage > 20 ? ConsoleColor.Green : ConsoleColor.Red, winBg);

            // Row 4: Hardware details
            desktop.WriteInsideWindow(left, winWidth, top + 7, 2, "Chassis / System  : " + info.ChassisType, ConsoleColor.White, winBg);
            desktop.WriteInsideWindow(left, winWidth, top + 8, 2, "Cell Chemistry    : " + info.Chemistry, ConsoleColor.White, winBg);
            string volt = string.IsNullOrEmpty(info.Voltage) ? "11.1V (Nominal)" : info.Voltage;
            desktop.WriteInsideWindow(left, winWidth, top + 9, 2, "Estimated Voltage : " + volt, ConsoleColor.White, winBg);

            // Row 5: ACPI / EC / SMBus bus state
            string busLink = BatteryService.GetHardwareBusSummary();
            desktop.WriteInsideWindow(left, winWidth, top + 11, 2, "Hardware Bus Link : " + busLink, ConsoleColor.Gray, winBg);

            // Status message
            desktop.WriteInsideWindow(left, winWidth, top + 13, 2, _lastActionStatus, ConsoleColor.Yellow, winBg);

            // Footer controls
            desktop.WriteInsideWindow(left, winWidth, top + winHeight - 2, 2, "[T] Toggle AC/Battery  |  [F5] Re-Scan  |  [ESC] Exit", ConsoleColor.Yellow, winBg);
        }

        private void DrawMiniBar(int percent, int totalWidth)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;

            int filled = (percent * totalWidth) / 100;
            System.Console.Write("[");
            for (int i = 0; i < totalWidth; i++)
            {
                if (i < filled)
                {
                    System.Console.ForegroundColor = percent > 20 ? ConsoleColor.Green : ConsoleColor.Red;
                    System.Console.Write("#");
                }
                else
                {
                    System.Console.ForegroundColor = ConsoleColor.DarkGray;
                    System.Console.Write("-");
                }
            }
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.Write("]");
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                desktop.SetCurrentApp("debug_menu");
            }
            else if (keyEvent.Key == ConsoleKeyEx.T || keyEvent.KeyChar == 't' || keyEvent.KeyChar == 'T')
            {
                BatteryService.ToggleAcOverride();
                var b = BatteryService.GetBatteryInfo();
                _lastActionStatus = $"State manually toggled -> {(b.IsAcConnected ? "AC Mains Connected" : "Discharging on Battery")}";
            }
            else if (keyEvent.Key == ConsoleKeyEx.F5 || keyEvent.KeyChar == 'r' || keyEvent.KeyChar == 'R')
            {
                BatteryService.ForceHardwareProbe();
                var b = BatteryService.GetBatteryInfo();
                _lastActionStatus = $"Hardware re-scanned: {b.Status} ({b.Percentage}%)";
            }
        }
    }
}
