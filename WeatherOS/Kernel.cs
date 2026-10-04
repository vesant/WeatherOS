using System;
using Cosmos.System;
using Cosmos.System.FileSystem;
using Cosmos.System.FileSystem.VFS;
using Cosmos.System.Network.Config;
using Cosmos.System.Network.IPv4;
using WeatherOS.Graphics;
using WeatherOS.Services;

namespace WeatherOS
{
    /// <summary>
    /// Main entry point for the WeatherOS Kernel.
    /// Manages boot initialization, hybrid UI dispatching (CLI / VBE Canvas),
    /// and ensures low GC footprint to guarantee stability on x86 hardware and QEMU.
    /// </summary>
    public class Kernel : Cosmos.System.Kernel
    {
        // ---------------------------------------------------------------------
        // Persistent Kernel Subsystems (Instantiated once to avoid GC thrashing)
        // ---------------------------------------------------------------------
        private CosmosVFS _vfs;
        private WeatherService _weatherService;
        private SerialSensorService _serialSensorService;
        private WeatherGuiRenderer _guiRenderer;

        // Boot and Hardware State Flags
        private bool _isVfsMounted = false;
        private bool _isNetworkConfigured = false;
        private string _assignedIpAddress = "0.0.0.0 (Offline / DHCP Failed)";
        private bool _isGuiActive = false;

        // Reusable cached telemetry reading
        private WeatherData _latestWeatherData;

        /// <summary>
        /// 1. Boot Initialization Phase (BeforeRun)
        /// Executes strictly once before the main Run() loop starts.
        /// Configures filesystem, hardware drivers, and network stack defensively.
        /// </summary>
        protected override void BeforeRun()
        {
            // Initialize auxiliary domain services
            _weatherService = new WeatherService();
            _serialSensorService = new SerialSensorService();
            _guiRenderer = new WeatherGuiRenderer();

            // A. Initialize Virtual File System (VFS)
            try
            {
                _vfs = new CosmosVFS();
                VFSManager.RegisterVFS(_vfs);
                _isVfsMounted = true;
            }
            catch (Exception)
            {
                _isVfsMounted = false;
            }

            // B. Initialize Network Stack with DHCP
            try
            {
                // Request DHCP configuration
                // Protected with try-catch to prevent kernel panics if NIC is missing or unlinked
                Cosmos.System.Network.IPv4.Config.Enable(true);

                // Retrieve assigned IP address safely
                var currentAddress = NetworkConfiguration.CurrentAddress;
                if (currentAddress != null && currentAddress.ToString() != "0.0.0.0")
                {
                    _assignedIpAddress = currentAddress.ToString();
                    _isNetworkConfigured = true;
                }
                else
                {
                    _assignedIpAddress = "DHCP Discovery Timeout / Static required";
                    _isNetworkConfigured = false;
                }
            }
            catch (Exception)
            {
                _isNetworkConfigured = false;
                _assignedIpAddress = "NIC not detected or driver error";
            }

            // C. Terminal Base Presentation
            PrintBootBanner();
        }

        /// <summary>
        /// 2. Hybrid Run Loop
        /// Cosmos continuously calls Run() in an infinite supervisor loop.
        /// Operates as a state machine: Terminal Mode (CLI) vs Graphical Mode (VBE Canvas).
        /// </summary>
        protected override void Run()
        {
            if (_isGuiActive)
            {
                HandleGuiMode();
            }
            else
            {
                HandleTerminalMode();
            }
        }

        // =====================================================================
        // TERMINAL SUBSYSTEM (CLI)
        // =====================================================================

        private void HandleTerminalMode()
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("WeatherOS");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("@");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("core");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(":~# ");

            string rawInput = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(rawInput))
            {
                return;
            }

            string command = rawInput.Trim().ToLower();
            ExecuteCommand(command);
        }

        private void ExecuteCommand(string command)
        {
            switch (command)
            {
                case "help":
                    ShowHelp();
                    break;

                case "clear":
                    PrintBootBanner();
                    break;

                case "ipconfig":
                    ShowIpConfig();
                    break;

                case "weather":
                    ShowWeatherReport();
                    break;

                case "serial-test":
                    TestSerialSensorPort();
                    break;

                case "gui":
                    SwitchToGuiMode();
                    break;

                case "reboot":
                    Console.WriteLine("Rebooting WeatherOS...");
                    Cosmos.System.Power.Reboot();
                    break;

                case "shutdown":
                    Console.WriteLine("Shutting down WeatherOS...");
                    Cosmos.System.Power.Shutdown();
                    break;

                default:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Unknown command: '{command}'. Type 'help' for available commands.");
                    Console.ResetColor();
                    break;
            }
        }

        private void ShowHelp()
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n--- WeatherOS Shell Commands ---");
            Console.ResetColor();
            Console.WriteLine("  help         - Display this list of available commands");
            Console.WriteLine("  clear        - Clear the terminal screen and redisplay system banner");
            Console.WriteLine("  ipconfig     - Display current IPv4 address and network stack status");
            Console.WriteLine("  weather      - View real-time meteorological observations (Console View)");
            Console.WriteLine("  serial-test  - Diagnostic test for PC COM1 port (Future sensor placeholder)");
            Console.WriteLine("  gui          - Switch to the VBE High-Resolution Graphical Dashboard");
            Console.WriteLine("  reboot       - Reboot the computer");
            Console.WriteLine("  shutdown     - Gracefully power off the system\n");
        }

        private void ShowIpConfig()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n[Network Configuration - IPv4 Stack]");
            Console.ResetColor();
            Console.WriteLine($"  Status      : {(_isNetworkConfigured ? "Online (Connected)" : "Offline / Unassigned")}");
            Console.WriteLine($"  IPv4 Address: {_assignedIpAddress}");
            
            try
            {
                var currentAddress = NetworkConfiguration.CurrentAddress;
                if (currentAddress != null)
                {
                    Console.WriteLine($"  Current NIC : {currentAddress}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Diagnostics : {ex.Message}");
            }
            Console.WriteLine();
        }

        private void ShowWeatherReport()
        {
            _latestWeatherData = _weatherService.GetLatestReading();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n==================================================");
            Console.WriteLine("       WEATHEROS METEOROLOGICAL TELEMETRY         ");
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

            if (!_serialSensorService.IsInitialized)
            {
                Console.WriteLine("Probing PC COM1 UART (9600 baud)...");
                bool ok = _serialSensorService.Initialize(Cosmos.HAL.SerialPort.Port.COM1, Cosmos.HAL.SerialPort.BaudRate.BaudRate9600);
                if (ok)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("[ OK ] Local COM1 port initialized.");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine("[WARN] COM1 port not detected on this PC motherboard.");
                    Console.ResetColor();
                    return;
                }
            }

            Console.WriteLine("Listening for incoming serial packets...");
            if (_serialSensorService.TryReadTelemetryPacket(out string packet))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[Serial Data In]: {packet}");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("[Port Idle] No external sensor attached. Standalone PC mode active.");
                Console.ResetColor();
            }
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

        // =====================================================================
        // GRAPHICAL SUBSYSTEM (VBE / CANVAS)
        // =====================================================================

        private void HandleGuiMode()
        {
            // 1. Fetch fresh telemetry (reuses existing struct)
            _latestWeatherData = _weatherService.GetLatestReading();

            // 2. Render frame to canvas (zero allocations)
            _guiRenderer.Render(ref _latestWeatherData);

            // 3. Non-blocking key polling to exit GUI gracefully
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

            // Restore terminal display cleanly
            PrintBootBanner();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[GUI Exited] Switched back to Terminal Base Mode.\n");
            Console.ResetColor();
        }

        // =====================================================================
        // UTILITIES & BANNER
        // =====================================================================

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

            // Subsystem Status
            Console.Write(" [");
            if (_isVfsMounted)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write(" OK ");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write("FAIL");
            }
            Console.ResetColor();
            Console.WriteLine("] Virtual File System (CosmosVFS) Registered");

            Console.Write(" [");
            if (_isNetworkConfigured)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write(" OK ");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.Write("WARN");
            }
            Console.ResetColor();
            Console.WriteLine($"] IPv4 Network Stack : {_assignedIpAddress}");

            Console.Write(" [");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write(" OK ");
            Console.ResetColor();
            Console.WriteLine("] Hardware Target    : Standard x86 PC (Standalone Mode)");

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.WriteLine(" Type 'help' to view commands or 'gui' to launch the meteorological dashboard.  ");
            Console.WriteLine("--------------------------------------------------------------------------------\n");
            Console.ResetColor();
        }
    }
}
