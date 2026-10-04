using System;
using Cosmos.System;
using Cosmos.System.Graphics;
using WeatherOS.Graphics;
using WeatherOS.Services;

namespace WeatherOS
{
    public class Kernel : Cosmos.System.Kernel
    {
        private WeatherGuiRenderer _guiRenderer = null!;
        private WeatherService _weatherService = null!;
        private SerialSensorService _serialSensorService = null!;
        
        private WeatherData _latestWeatherData;

        private bool _isVfsMounted = false;
        private bool _isNetworkConfigured = false;
        private string _assignedIpAddress = "127.0.0.1 (Loopback)";
        private bool _isGuiActive = false;

        protected override void BeforeRun()
        {
            try
            {
                _isVfsMounted = true; 
            }
            catch (Exception)
            {
                _isVfsMounted = false;
            }

            try
            {
                _isNetworkConfigured = false;
                _assignedIpAddress = "Disconnected (Standalone PC)";
            }
            catch (Exception)
            {
                _isNetworkConfigured = false;
            }

            _guiRenderer = new WeatherGuiRenderer();
            _weatherService = new WeatherService();
            _serialSensorService = new SerialSensorService();

            PrintBootBanner();
        }

        protected override void Run()
        {
            if (_isGuiActive)
            {
                HandleGuiMode();
                return;
            }

            Console.Write("WeatherOS> ");
            string input = Console.ReadLine();
            ProcessCommand(input);
        }

        private void ProcessCommand(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return;

            string[] parts = input.Trim().Split(' ');
            string command = parts[0].ToLower();

            switch (command)
            {
                case "help":
                    Console.WriteLine("Available commands:");
                    Console.WriteLine("  help         - Show this menu.");
                    Console.WriteLine("  clear        - Clear the screen.");
                    Console.WriteLine("  ipconfig     - Show current network IP address.");
                    Console.WriteLine("  weather      - Fetch and display the latest meteorological reading.");
                    Console.WriteLine("  serial-test  - Probe COM1 and listen for Arduino sensor telemetry.");
                    Console.WriteLine("  gui          - Switch to graphical meteorological dashboard.");
                    Console.WriteLine("  halt         - Shutdown OS.");
                    break;

                case "clear":
                    PrintBootBanner();
                    break;

                case "ipconfig":
                    Console.WriteLine($"Network IP Address: {_assignedIpAddress}");
                    break;

                case "weather":
                    DisplayTerminalWeather();
                    break;

                case "serial-test":
                    TestSerialSensorPort();
                    break;

                case "gui":
                    SwitchToGuiMode();
                    break;

                case "halt":
                    Console.WriteLine("Halting system...");
                    Stop();
                    break;

                default:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Unknown command: '{command}'. Type 'help' for a list of commands.");
                    Console.ResetColor();
                    break;
            }
        }

        private void DisplayTerminalWeather()
        {
            _latestWeatherData = _weatherService.GetLatestReading();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n[LOCAL TELEMETRY REPORT]");
            Console.WriteLine("==================================================");
            Console.ResetColor();

            Console.WriteLine($"  Source Station     : Local Weather Engine (Standalone x86 PC)");
            Console.WriteLine($"  Condition          : {_latestWeatherData.Condition}");
            
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  Temperature        : {_latestWeatherData.TemperatureCelsius:F1} C");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine($"  Relative Humidity  : {_latestWeatherData.HumidityPercent:F1} %");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  Atmospheric Press. : {_latestWeatherData.PressureHpa:F1} hPa");
            Console.ResetColor();

            Console.WriteLine($"  Wind Speed / Dir   : {_latestWeatherData.WindSpeedKmh:F1} km/h ({_latestWeatherData.WindDirection})");
            Console.WriteLine("--------------------------------------------------");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  Note: Type 'gui' to inspect visual bar charts.\n");
            Console.ResetColor();
        }

        private void TestSerialSensorPort()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n[Hardware Diagnostic - PC COM1 Serial Port]");
            Console.ResetColor();
            Console.WriteLine("Note: Placeholder diagnostic for future external sensor hardware.");

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("[Port Idle] No external sensor attached. Standalone PC mode active.");
            Console.ResetColor();
            Console.WriteLine();
        }

        private void SwitchToGuiMode()
        {
            Console.WriteLine("Switching to Graphical Mode (VBE 800x600)...");
            
            bool started = _guiRenderer.Start();
            if (started)
            {
                _isGuiActive = true;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Error: Unable to initialize VBE canvas. Check QEMU VGA adapter.");
                Console.ResetColor();
            }
        }

        private void HandleGuiMode()
        {
            _latestWeatherData = _weatherService.GetLatestReading();
            _guiRenderer.Render(ref _latestWeatherData);

            if (KeyboardManager.TryReadKey(out var keyEvent))
            {
                if (keyEvent.Key == ConsoleKeyEx.Escape || keyEvent.Key == ConsoleKeyEx.Q)
                {
                    ExitGuiMode();
                }
            }
        }

        private void ExitGuiMode()
        {
            _guiRenderer.Stop();
            _isGuiActive = false;

            PrintBootBanner();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[GUI Exited] Switched back to Terminal Base Mode.\n");
            Console.ResetColor();
        }

        private void PrintBootBanner()
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("================================================================================");
            Console.WriteLine("    __      __               _   _                  ____   _____            ");
            Console.WriteLine("    \\ \\    / /              | | | |                / __ \\ / ____|           ");
            Console.WriteLine("     \\ \\  / /__  __ _ ______| |_| |__   ___ _ __  | |  | | (___             ");
            Console.WriteLine("      \\ \\/ / _ \\/ _` |______| __| '_ \\ / _ \\ '__| | |  | |\\___ \\            ");
            Console.WriteLine("       \\  /  __/ (_| |      | |_| | | |  __/ |    | |__| |____) |           ");
            Console.WriteLine("        \\/ \\___|\\__,_|       \\__|_| |_|\\___|_|     \\____/|_____/            ");
            Console.WriteLine("              Dedicated Meteorological x86 Operating System                     ");
            Console.WriteLine("================================================================================");
            Console.ResetColor();

            Console.Write(" [");
            if (_isVfsMounted) Console.ForegroundColor = ConsoleColor.Green; else Console.ForegroundColor = ConsoleColor.Red;
            Console.Write(_isVfsMounted ? " OK " : "FAIL");
            Console.ResetColor();
            Console.WriteLine("] Virtual File System Ready");

            Console.Write(" [");
            if (_isNetworkConfigured) Console.ForegroundColor = ConsoleColor.Green; else Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Write(_isNetworkConfigured ? " OK " : "WARN");
            Console.ResetColor();
            Console.WriteLine($"] IPv4 Network Stack : {_assignedIpAddress}");

            Console.WriteLine(" [ OK ] Hardware Target    : Standard x86 PC (Standalone Mode)");

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.WriteLine(" Type 'help' to view commands or 'gui' to launch the meteorological dashboard.  ");
            Console.WriteLine("--------------------------------------------------------------------------------\n");
            Console.ResetColor();
        }
    }
}
