using System;
using Cosmos.System;
using Cosmos.System.Graphics;
using WeatherOS.Graphics;
using WeatherOS.Services;

namespace WeatherOS
{
    public class Kernel : Cosmos.System.Kernel
    {
        private TuiDesktop _tuiDesktop = null!;
        private WeatherService _weatherService = null!;
        private SerialSensorService _serialSensorService = null!;
        
        private WeatherData _latestWeatherData;

        private bool _isVfsMounted = false;
        private bool _isNetworkConfigured = false;
        private string _assignedIpAddress = "127.0.0.1 (Loopback)";

        private Cosmos.System.FileSystem.CosmosVFS _vfs;

        protected override void BeforeRun()
        {
            try
            {
                _vfs = new Cosmos.System.FileSystem.CosmosVFS();
                Cosmos.System.FileSystem.VFS.VFSManager.RegisterVFS(_vfs);
                _isVfsMounted = true; 
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("VFS Init Error: " + ex.Message);
                _isVfsMounted = false;
            }

            try
            {
                if (Cosmos.HAL.NetworkDevice.Devices.Count > 0)
                {
                    System.Console.WriteLine("Initializing Network Stack (DHCP)...");
                    using (var xClient = new Cosmos.System.Network.IPv4.UDP.DHCP.DHCPClient())
                    {
                        xClient.SendDiscoverPacket();
                    }
                }
                else
                {
                    System.Console.WriteLine("No supported network card found. Skipping DHCP.");
                    throw new Exception("No NIC");
                }

                _assignedIpAddress = Cosmos.System.Network.Config.NetworkConfiguration.CurrentAddress.ToString();
                _isNetworkConfigured = true;
            }
            catch (Exception)
            {
                _isNetworkConfigured = false;
                _assignedIpAddress = "Offline (No Cable / DHCP)";
            }

            _weatherService = new WeatherService();
            _tuiDesktop = new TuiDesktop(_weatherService);
            _serialSensorService = new SerialSensorService();

            if (_isNetworkConfigured)
            {
                System.Console.WriteLine("Auto-locating via IP Geolocation API...");
                string autoCity = _weatherService.FetchAutoLocation();
                if (!string.IsNullOrWhiteSpace(autoCity))
                {
                    System.Console.WriteLine($"Detected City: {autoCity}. Fetching weather telemetry...");
                    _weatherService.FetchOpenWeather(autoCity, DecodeApiKey());
                }
            }

            var splash = new Graphics.SplashScreen();
            splash.Run();

            PrintBootBanner();
        }

        private string DecodeApiKey()
        {
            // Nova chave do WeatherAPI.com
            return "bc0cfac1c3144fc3bf0145134260810";
        }

        protected override void Run()
        {
            System.Console.Write("WeatherOS> ");
            string input = System.Console.ReadLine();
            ProcessCommand(input);
        }

        private void ProcessCommand(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return;

            string[] parts = input.Trim().Split(' ');
            string command = parts[0].ToLower();

            // Intercept dynamic TUI app commands
            string[] appIds = _tuiDesktop.GetAppIds();
            for (int i = 0; i < appIds.Length; i++)
            {
                if (command == appIds[i])
                {
                    System.Console.WriteLine($"Launching TUI App: {appIds[i]}...");
                    _tuiDesktop.StartApp(command);
                    PrintBootBanner();
                    return;
                }
            }

            switch (command)
            {
                case "help":
                    System.Console.WriteLine("Available Core Commands:");
                    System.Console.WriteLine("  help         - Show this menu.");
                    System.Console.WriteLine("  clear        - Clear the screen.");
                    System.Console.WriteLine("  ping <ip>    - Test network connectivity.");
                    System.Console.WriteLine("  fetch <city> - Pull live data from OpenWeather API.");
                    System.Console.WriteLine("  serial-test  - Probe COM1 and listen for Arduino telemetry.");
                    System.Console.WriteLine("  gui          - Switch to ASCII Dashboard Menu.");
                    
                    System.Console.WriteLine("\nAvailable Graphical Apps (Type to Launch Directly):");
                    string[] appNames = _tuiDesktop.GetAppNames();
                    for (int i = 0; i < appIds.Length; i++)
                    {
                        System.Console.WriteLine($"  {appIds[i].PadRight(12)} - {appNames[i]}");
                    }
                    break;

                case "fetch":
                    if (!_isNetworkConfigured || parts.Length < 2)
                    {
                        System.Console.WriteLine("Error: Network offline or missing city. (Example: fetch Lisbon)");
                        break;
                    }
                    string city = string.Join(" ", parts, 1, parts.Length - 1);
                    _weatherService.FetchOpenWeather(city, DecodeApiKey());
                    var data = _weatherService.GetLatestReading();
                    System.Console.WriteLine($"\n  -> {data.Location}: {data.TemperatureCelsius}C, {data.Condition}");
                    if (data.AlertLevel != "NONE") System.Console.WriteLine($"  -> [ALERT {data.AlertLevel}]: {data.AlertMessage}");
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

                case "serial-test":
                    TestSerialSensorPort();
                    break;

                case "gui":
                    SwitchToGuiMode();
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
            System.Console.WriteLine("Loading TUI Desktop Environment...");
            _tuiDesktop.Start();
            PrintBootBanner();
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
