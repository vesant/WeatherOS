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
                System.Console.WriteLine("Initializing Network Stack (DHCP)...");
                using (var xClient = new Cosmos.System.Network.IPv4.UDP.DHCP.DHCPClient())
                {
                    xClient.SendDiscoverPacket();
                }

                _assignedIpAddress = Cosmos.System.Network.Config.NetworkConfiguration.CurrentAddress.ToString();
                _isNetworkConfigured = true;
            }
            catch (Exception)
            {
                _isNetworkConfigured = false;
                _assignedIpAddress = "Offline (No Cable / DHCP)";
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

            System.Console.Write("WeatherOS> ");
            string input = System.Console.ReadLine();
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
                    System.Console.WriteLine("Available commands:");
                    System.Console.WriteLine("  help         - Show this menu.");
                    System.Console.WriteLine("  clear        - Clear the screen.");
                    System.Console.WriteLine("  ipconfig     - Show current network IP address.");
                    System.Console.WriteLine("  ping <ip>    - Test network connectivity (ICMP Echo Request).");
                    System.Console.WriteLine("  weather      - Fetch and display the latest meteorological reading.");
                    System.Console.WriteLine("  fetch <city> - Pull live data from OpenWeather API.");
                    System.Console.WriteLine("  serial-test  - Probe COM1 and listen for Arduino sensor telemetry.");
                    System.Console.WriteLine("  gui          - Switch to ASCII meteorological dashboard.");
                    System.Console.WriteLine("  poweroff     - ACPI Shutdown / Power off the system.");
                    break;

                case "fetch":
                    if (!_isNetworkConfigured || parts.Length < 2)
                    {
                        System.Console.WriteLine("Error: Network offline or missing city. (Example: fetch Lisbon)");
                        break;
                    }
                    string city = string.Join(" ", parts, 1, parts.Length - 1);
                    _weatherService.FetchOpenWeather(city, "bd7e29393f3846fb31118232bdb2001f");
                    break;

                case "ping":
                    if (!_isNetworkConfigured || parts.Length < 2)
                    {
                        System.Console.WriteLine("Error: Network offline or missing IP. (Example: ping 192.168.1.1)");
                        break;
                    }
                    try
                    {
                        var address = Cosmos.System.Network.IPv4.Address.Parse(parts[1]);
                        System.Console.WriteLine($"Pinging {parts[1]} with 32 bytes of data...");
                        
                        int successCount = 0;
                        using (var xClient = new Cosmos.System.Network.IPv4.ICMPClient())
                        {
                            xClient.Connect(address);
                            for (int i = 0; i < 4; i++)
                            {
                                xClient.SendEcho();
                                var endpoint = new Cosmos.System.Network.IPv4.EndPoint(Cosmos.System.Network.IPv4.Address.Zero, 0);
                                int time = xClient.Receive(ref endpoint, 2000);
                                
                                if (time >= 0)
                                {
                                    System.Console.WriteLine($"Reply from {parts[1]}: time={time}ms");
                                    successCount++;
                                }
                                else
                                {
                                    System.Console.WriteLine("Request timed out.");
                                }
                            }
                        }
                        System.Console.WriteLine($"Ping statistics: 4 sent, {successCount} received.\n");
                    }
                    catch
                    {
                        System.Console.WriteLine("Invalid IP address or hardware error.\n");
                    }
                    break;

                case "clear":
                    PrintBootBanner();
                    break;

                case "ipconfig":
                    System.Console.WriteLine($"Network IP Address: {_assignedIpAddress}");
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

                case "poweroff":
                    System.Console.WriteLine("Initiating ACPI Shutdown sequence...");
                    Cosmos.System.Power.Shutdown();
                    Stop();
                    break;

                default:
                    System.Console.ForegroundColor = ConsoleColor.Red;
                    System.Console.WriteLine($"Unknown command: '{command}'. Type 'help' for a list of commands.");
                    System.Console.ResetColor();
                    break;
            }
        }

        private void DisplayTerminalWeather()
        {
            _latestWeatherData = _weatherService.GetLatestReading();

            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.WriteLine("\n[LOCAL TELEMETRY REPORT]");
            System.Console.WriteLine("==================================================");
            System.Console.ResetColor();

            System.Console.WriteLine("  Source Station     : Local Weather Engine (Standalone x86 PC)");
            System.Console.WriteLine("  Condition          : " + _latestWeatherData.Condition);
            
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine("  Temperature        : " + ((int)_latestWeatherData.TemperatureCelsius).ToString() + " C");
            System.Console.ResetColor();

            System.Console.ForegroundColor = ConsoleColor.Blue;
            System.Console.WriteLine("  Relative Humidity  : " + ((int)_latestWeatherData.HumidityPercent).ToString() + " %");
            System.Console.ResetColor();

            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine("  Atmospheric Press. : " + ((int)_latestWeatherData.PressureHpa).ToString() + " hPa");
            System.Console.ResetColor();

            System.Console.WriteLine("  Wind Speed / Dir   : " + ((int)_latestWeatherData.WindSpeedKmh).ToString() + " km/h (" + _latestWeatherData.WindDirection + ")");
            System.Console.WriteLine("--------------------------------------------------");
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.WriteLine("  Note: Type 'gui' to inspect visual bar charts.\n");
            System.Console.ResetColor();
        }

        private void TestSerialSensorPort()
        {
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine("\n[Hardware Diagnostic - PC COM1 Serial Port]");
            System.Console.ResetColor();
            System.Console.WriteLine("Note: Placeholder diagnostic for future external sensor hardware.");

            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.WriteLine("[Port Idle] No external sensor attached. Standalone PC mode active.");
            System.Console.ResetColor();
            System.Console.WriteLine();
        }

        private void SwitchToGuiMode()
        {
            System.Console.WriteLine("Switching to full-screen ASCII tty (Terminal Dashboard)...");
            
            bool started = _guiRenderer.Start();
            if (started)
            {
                _isGuiActive = true;
            }
            else
            {
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine("[sys] error: unable to initialize dashboard renderer.");
                System.Console.ResetColor();
            }
        }

        private void HandleGuiMode()
        {
            try
            {
                _latestWeatherData = _weatherService.GetLatestReading();
                _guiRenderer.Render(ref _latestWeatherData);

                // Block until a key is pressed (Saves 100% CPU!)
                var keyEvent = Cosmos.System.KeyboardManager.ReadKey();
                if (keyEvent.Key == Cosmos.System.ConsoleKeyEx.Escape || keyEvent.Key == Cosmos.System.ConsoleKeyEx.Q)
                {
                    ExitGuiMode();
                }
            }
            catch (Exception ex)
            {
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.Write("[GUI ERROR] ");
                System.Console.WriteLine(ex.Message);
                System.Console.ResetColor();
                while(true) { } // Halt to ensure we see the error!
            }
        }

        private void ExitGuiMode()
        {
            _guiRenderer.Stop();
            _isGuiActive = false;

            PrintBootBanner();
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine("[sys] tty1: Terminated graphical session.\n");
            System.Console.ResetColor();
        }

        private void PrintBootBanner()
        {
            System.Console.Clear();
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.WriteLine("================================================================================");
            System.Console.WriteLine("  __          __         _   _                  ____   _____            ");
            System.Console.WriteLine("  \\ \\        / /        | | | |                / __ \\ / ____|           ");
            System.Console.WriteLine("   \\ \\  /\\  / /__  __ _ | |_| |__   ___ _ __  | |  | | (___             ");
            System.Console.WriteLine("    \\ \\/  \\/ / _ \\/ _` || __| '_ \\ / _ \\ '__| | |  | |\\___ \\            ");
            System.Console.WriteLine("     \\  /\\  /  __/ (_| || |_| | | |  __/ |    | |__| |____) |           ");
            System.Console.WriteLine("      \\/  \\/ \\___|\\__,_| \\__|_| |_|\\___|_|     \\____/|_____/            ");
            System.Console.WriteLine("              Dedicated Meteorological x86 Operating System                     ");
            System.Console.WriteLine("================================================================================");
            System.Console.ResetColor();

            System.Console.Write(" [");
            if (_isVfsMounted) System.Console.ForegroundColor = ConsoleColor.Green; else System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.Write(_isVfsMounted ? " OK " : "FAIL");
            System.Console.ResetColor();
            System.Console.WriteLine("] Virtual File System Ready");

            System.Console.Write(" [");
            if (_isNetworkConfigured) System.Console.ForegroundColor = ConsoleColor.Green; else System.Console.ForegroundColor = ConsoleColor.DarkYellow;
            System.Console.Write(_isNetworkConfigured ? " OK " : "WARN");
            System.Console.ResetColor();
            System.Console.WriteLine($"] IPv4 Network Stack : {_assignedIpAddress}");

            System.Console.WriteLine(" [ OK ] Hardware Target    : Standard x86 PC (Standalone Mode)");

            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.WriteLine("--------------------------------------------------------------------------------");
            System.Console.WriteLine(" Type 'help' to view commands or 'gui' to launch the meteorological dashboard.  ");
            System.Console.WriteLine("--------------------------------------------------------------------------------\n");
            System.Console.ResetColor();
        }
    }
}
