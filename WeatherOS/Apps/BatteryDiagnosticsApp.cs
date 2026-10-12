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
            int left = (80 - winWidth) / 2;
            int top = (24 - winHeight) / 2;

            desktop.DrawWindow(left, top, winWidth, winHeight, "ACPI POWER & BATTERY TELEMETRY  |  [ESC] Exit");
            ConsoleColor winBg = desktop.IsDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;

            var info = BatteryService.GetBatteryInfo();

            // Row 1: Power Source & Status
            System.Console.SetCursorPosition(left + 3, top + 2);
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.Write("Power Source      : ");
            if (info.IsAcConnected)
            {
                System.Console.ForegroundColor = ConsoleColor.Green;
                System.Console.Write("AC Mains Adapter (Wall Power - Charging)".PadRight(winWidth - 25));
            }
            else
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.Write("Battery (Discharging / Battery Mode)".PadRight(winWidth - 25));
            }

            // Row 2: Status & Health
            System.Console.SetCursorPosition(left + 3, top + 3);
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.Write("Operation State   : ");
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.Write(info.Status.PadRight(winWidth - 25));

            // Row 3: Battery Charge Level
            System.Console.SetCursorPosition(left + 3, top + 5);
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.Write("Battery Charge    : ");
            System.Console.ForegroundColor = info.Percentage > 20 ? ConsoleColor.Green : ConsoleColor.Red;
            System.Console.Write($"{info.Percentage}% ");
            // Progress Bar inline
            DrawMiniBar(info.Percentage, 20);

            // Row 4: Hardware details
            System.Console.SetCursorPosition(left + 3, top + 7);
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.Write("Chassis / System  : ");
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.Write(info.ChassisType.PadRight(winWidth - 25));

            System.Console.SetCursorPosition(left + 3, top + 8);
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.Write("Cell Chemistry    : ");
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.Write(info.Chemistry.PadRight(winWidth - 25));

            System.Console.SetCursorPosition(left + 3, top + 9);
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.Write("Estimated Voltage : ");
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.Write((string.IsNullOrEmpty(info.Voltage) ? "11.1V (Nominal)" : info.Voltage).PadRight(winWidth - 25));

            // Row 5: ACPI / EC / SMBus bus state
            System.Console.SetCursorPosition(left + 3, top + 11);
            System.Console.ForegroundColor = ConsoleColor.DarkCyan;
            System.Console.Write("Hardware Bus Link : ");
            System.Console.ForegroundColor = ConsoleColor.Gray;
            string busLink = BatteryService.GetHardwareBusSummary();
            if (busLink.Length > winWidth - 25) busLink = busLink.Substring(0, winWidth - 28) + "...";
            System.Console.Write(busLink.PadRight(winWidth - 25));

            // Status message
            System.Console.SetCursorPosition(left + 3, top + 13);
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            string act = _lastActionStatus;
            if (act.Length > winWidth - 6) act = act.Substring(0, winWidth - 9) + "...";
            System.Console.Write(act.PadRight(winWidth - 6));

            // Footer controls
            System.Console.SetCursorPosition(left + 3, top + winHeight - 2);
            System.Console.BackgroundColor = winBg;
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.Write("[T] Toggle AC/Battery  |  [F5] Re-Scan  |  [ESC] Exit");
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
