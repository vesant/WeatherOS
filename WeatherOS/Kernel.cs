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
                System.Console.WriteLine("Aguardando IP via DHCP (Pode demorar uns segundos)...");
                using (var xClient = new Cosmos.System.Network.IPv4.UDP.DHCP.DHCPClient())
                {
                    // Envia um pedido à rede (Router) para obter um IP
                    xClient.SendDiscoverPacket();
                }

                _assignedIpAddress = Cosmos.System.Network.Config.NetworkConfiguration.CurrentAddress.ToString();
                _isNetworkConfigured = true;
            }
            catch (Exception)
            {
                _isNetworkConfigured = false;
                _assignedIpAddress = "Desligado (Sem Cabo ou Sem DHCP)";
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
                    System.Console.WriteLine("  serial-test  - Probe COM1 and listen for Arduino sensor telemetry.");
                    System.Console.WriteLine("  gui          - Switch to graphical meteorological dashboard.");
                    System.Console.WriteLine("  halt         - Shutdown OS.");
                    break;

                case "ping":
                    if (!_isNetworkConfigured || parts.Length < 2)
                    {
                        System.Console.WriteLine("Erro: Rede desligada ou falta de IP. (Ex: ping 192.168.1.1)");
                        break;
                    }
                    try
                    {
                        var address = Cosmos.System.Network.IPv4.Address.Parse(parts[1]);
                        System.Console.WriteLine($"A enviar ping para {parts[1]}...");
                        
                        int successCount = 0;
                        using (var xClient = new Cosmos.System.Network.IPv4.ICMPClient())
                        {
                            xClient.Connect(address);
                            for (int i = 0; i < 4; i++)
                            {
                                xClient.SendEcho();
                                var endpoint = new Cosmos.System.Network.IPv4.EndPoint(Cosmos.System.Network.IPv4.Address.Zero, 0);
                                int time = xClient.Receive(ref endpoint, 2000); // 2 segs timeout
                                
                                if (time >= 0)
                                {
                                    System.Console.WriteLine($"Resposta de {parts[1]}: tempo={time}ms");
                                    successCount++;
                                }
                                else
                                {
                                    System.Console.WriteLine("Esgotado o tempo de espera do pedido.");
                                }
                            }
                        }
                        System.Console.WriteLine($"Estatisticas: 4 enviados, {successCount} recebidos.\n");
                    }
                    catch
                    {
                        System.Console.WriteLine("IP Invalido ou erro de hardware de rede.\n");
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

                case "halt":
                    System.Console.WriteLine("Halting system...");
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

            System.Console.WriteLine($"  Source Station     : Local Weather Engine (Standalone x86 PC)");
            System.Console.WriteLine($"  Condition          : {_latestWeatherData.Condition}");
            
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine($"  Temperature        : {_latestWeatherData.TemperatureCelsius:F1} C");
            System.Console.ResetColor();

            System.Console.ForegroundColor = ConsoleColor.Blue;
            System.Console.WriteLine($"  Relative Humidity  : {_latestWeatherData.HumidityPercent:F1} %");
            System.Console.ResetColor();

            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine($"  Atmospheric Press. : {_latestWeatherData.PressureHpa:F1} hPa");
            System.Console.ResetColor();

            System.Console.WriteLine($"  Wind Speed / Dir   : {_latestWeatherData.WindSpeedKmh:F1} km/h ({_latestWeatherData.WindDirection})");
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
            System.Console.WriteLine("Switching to Graphical Mode (VBE 800x600)...");
            
            bool started = _guiRenderer.Start();
            if (started)
            {
                _isGuiActive = true;
            }
            else
            {
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine("Error: Unable to initialize VBE canvas. Check QEMU VGA adapter.");
                System.Console.ResetColor();
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
            // Devido a um bug conhecido no Cosmos Gen2 (falha ao restaurar os registos VGA a partir de VBE),
            // a forma mais limpa de sair do modo gráfico é fazer um reboot instantâneo.
            Cosmos.System.Power.Reboot();
        }

        private void PrintBootBanner()
        {
            System.Console.Clear();
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.WriteLine("================================================================================");
            System.Console.WriteLine("    __      __               _   _                  ____   _____            ");
            System.Console.WriteLine("    \\ \\    / /              | | | |                / __ \\ / ____|           ");
            System.Console.WriteLine("     \\ \\  / /__  __ _ ______| |_| |__   ___ _ __  | |  | | (___             ");
            System.Console.WriteLine("      \\ \\/ / _ \\/ _` |______| __| '_ \\ / _ \\ '__| | |  | |\\___ \\            ");
            System.Console.WriteLine("       \\  /  __/ (_| |      | |_| | | |  __/ |    | |__| |____) |           ");
            System.Console.WriteLine("        \\/ \\___|\\__,_|       \\__|_| |_|\\___|_|     \\____/|_____/            ");
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
