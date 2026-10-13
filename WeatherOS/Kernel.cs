using System;
using Cosmos.System;
using Cosmos.System.Graphics;
using WeatherOS.Commands;
using WeatherOS.Graphics;
using WeatherOS.Services;

namespace WeatherOS
{
    public class Kernel : Cosmos.System.Kernel
    {
        private TuiDesktop _tuiDesktop = null!;
        private WeatherService _weatherService = null!;
        private SerialSensorService _serialSensorService = null!;
        private CommandManager _commandManager = null!;

        private bool _isVfsMounted = false;
        private bool _isNetworkConfigured = false;

        private Cosmos.System.FileSystem.CosmosVFS _vfs;
        public static Cosmos.HAL.Network.RTL8102E CustomNIC;

        protected override void BeforeRun()
        {
            try
            {
                CustomNIC = new Cosmos.HAL.Network.RTL8102E();
                if (CustomNIC.Initialize())
                {
                    Cosmos.HAL.NetworkDevice.Devices.Add(CustomNIC);
                    System.Console.WriteLine("Custom RTL8102E Driver Loaded!");
                }
            }
            catch { }

            try
            {
                if (Cosmos.HAL.NetworkDevice.Devices.Count > 0)
                {
                    System.Console.WriteLine("Configuring Static IP (Bypassing DHCP Freeze)...");
                    
                    var ip = new Cosmos.System.Network.IPv4.Address(192, 168, 31, 100);
                    var subnet = new Cosmos.System.Network.IPv4.Address(255, 255, 255, 0);
                    var gateway = new Cosmos.System.Network.IPv4.Address(192, 168, 31, 1);
                    
                    var config = new Cosmos.System.Network.Config.IPConfig(ip, subnet, gateway);
                    Cosmos.System.Network.NetworkStack.ConfigIP(Cosmos.HAL.NetworkDevice.Devices[0], config);
                    _isNetworkConfigured = true;
                    System.Console.WriteLine("[net] Starting Bare-Metal AutoConfig...");
                    WeatherOS.Services.RawTcpHttp.DoAutoConfig(Cosmos.HAL.NetworkDevice.Devices[0]);
                    if (WeatherOS.Services.RawTcpHttp.GetAssignedIp() == "0.0.0.0")
                    {
                        _isNetworkConfigured = false;
                    }
                }
                else
                {
                    System.Console.WriteLine("No supported network card found.");
                    throw new Exception("No NIC");
                }
            }
            catch (Exception)
            {
                _isNetworkConfigured = false;
            }

            try
            {
                AhciDriverService.Initialize();
                CustomPciAtaDriverService.Initialize();
                _vfs = new Cosmos.System.FileSystem.CosmosVFS();
                Cosmos.System.FileSystem.VFS.VFSManager.RegisterVFS(_vfs);
                _isVfsMounted = true; 
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("VFS Init Error: " + ex.Message);
                _isVfsMounted = false;
            }

            _weatherService = new WeatherService();
            _tuiDesktop = new TuiDesktop(_weatherService);
            _serialSensorService = new SerialSensorService();

            InitializeCommandManager();

            // Pre-seed default cumulative update package in memory store for simulation/testing
            UpdateManagerService.InitDefaultUpdatePackage();

            // Show Boot Menu selection only if OS is NOT installed on disk.
            // If already installed, the boot menu is bypassed and boots directly from hard disk.
            if (UpdateManagerService.IsInstalledOnDisk())
            {
                UpdateManagerService.SetBootSource(BootSource.HardDisk);
            }
            else
            {
                var bootChoice = Graphics.BootMenu.Show();
                if (bootChoice == Graphics.BootMode.InstallToDisk)
                {
                    _tuiDesktop.StartApp("installer");
                }
            }

            if (_isNetworkConfigured)
            {
                string targetCity = "Setubal";
                bool autoLocEnabled = false;

                try
                {
                    string iniContent;
                    string stat;
                    if (StorageService.LoadFile("settings.ini", out iniContent, out stat) && !string.IsNullOrEmpty(iniContent))
                    {
                        string[] lines = iniContent.Split('\n');
                        foreach (var line in lines)
                        {
                            string t = line.Trim();
                            if (t.StartsWith("DefaultCity="))
                            {
                                string c = t.Substring(12).Trim();
                                if (!string.IsNullOrWhiteSpace(c)) targetCity = c;
                            }
                            if (t.StartsWith("AutoLoc="))
                            {
                                autoLocEnabled = t.Substring(8).Trim() == "true";
                            }
                            if (t.StartsWith("DebugLog="))
                            {
                                bool dbg = t.Substring(9).Trim() == "true";
                                _weatherService.SetDebugMode(dbg);
                            }
                        }
                    }
                }
                catch { }

                if (autoLocEnabled)
                {
                    System.Console.WriteLine("[net] Auto-locating via IP and fetching weather telemetry (wttr.in)...");
                    _weatherService.FetchOpenWeather("", DecodeApiKey());
                }
                else
                {
                    System.Console.WriteLine($"[net] Auto-locate disabled. Fetching weather for default: {targetCity}...");
                    _weatherService.FetchOpenWeather(targetCity, DecodeApiKey());
                }
            }

            var splash = new Graphics.SplashScreen();
            splash.Run();

            PrintBootBanner();
        }

        private void InitializeCommandManager()
        {
            _commandManager = new CommandManager();
            _commandManager.Register(new HelpCommand(_commandManager, _tuiDesktop));
            _commandManager.Register(new ClearCommand(() => PrintBootBanner()));
            _commandManager.Register(new BootMenuCommand(() => PrintBootBanner()));
            _commandManager.Register(new InstallCommand(() => _tuiDesktop.StartApp("installer")));
            _commandManager.Register(new PingCommand());
            _commandManager.Register(new FetchCommand(_weatherService, () => DecodeApiKey()));
            _commandManager.Register(new SetCityCommand(_weatherService, () => DecodeApiKey()));
            _commandManager.Register(new WeatherCommand(_weatherService));
            _commandManager.Register(new SerialTestCommand());
            _commandManager.Register(new DebugCommand(_weatherService));
            _commandManager.Register(new GuiCommand(() => SwitchToGuiMode()));
            _commandManager.Register(new TransferCommand(() => LaunchTransferMode()));
            _commandManager.Register(new DisksCommand());
            _commandManager.Register(new DiskDiagCommand());
            _commandManager.Register(new FormatCommand());
            _commandManager.Register(new PoweroffCommand());
            _commandManager.Register(new RebootCommand());
        }

        private string DecodeApiKey()
        {
            return "bc0cfac1c3144fc3bf0145134260810";
        }

        protected override void Run()
        {
            string input = TerminalLineEditor.ReadLine("WeatherOS> ");
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

            // Poweroff alias support (halt -> poweroff)
            if (command == "halt")
            {
                input = "poweroff";
            }
            if (command == "lsblk" || command == "diskpart")
            {
                input = "disks";
            }
            if (command == "storagediag" || command == "diag")
            {
                input = "diskdiag";
            }
            if (command == "boot" || command == "bootmgr")
            {
                input = "bootmenu";
            }
            if (command == "setup")
            {
                input = "install";
            }
            if (command == "clean" || command == "cls" || command == "clear")
            {
                System.Console.Clear();
                PrintBootBanner();
                return;
            }

            bool executed = _commandManager.Execute(input);
            if (!executed)
            {
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine($"Unknown command: '{command}'. Type 'help' for a list of commands.");
                System.Console.ResetColor();
            }
        }

        private void SwitchToGuiMode()
        {
            System.Console.Clear();
            _tuiDesktop.Start();
            PrintBootBanner();
        }

        private void LaunchTransferMode()
        {
            System.Console.Clear();
            _tuiDesktop.StartApp("transfer");
            PrintBootBanner();
        }

        private void PrintBootBanner()
        {
            System.Console.Clear();
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.WriteLine("==============================================================================");
            System.Console.WriteLine("  __          __         _   _                  ____   _____            ");
            System.Console.WriteLine("  \\ \\        / /        | | | |                / __ \\ / ____|           ");
            System.Console.WriteLine("   \\ \\  /\\  / /__  __ _ | |_| |__   ___ _ __  | |  | | (___             ");
            System.Console.WriteLine("    \\ \\/  \\/ / _ \\/ _` || __| '_ \\ / _ \\ '__| | |  | |\\___ \\            ");
            System.Console.WriteLine("     \\  /\\  /  __/ (_| || |_| | | |  __/ |    | |__| |____) |           ");
            System.Console.WriteLine("      \\/  \\/ \\___|\\__,_| \\__|_| |_|\\___|_|     \\____/|_____/            ");
            System.Console.WriteLine("              Dedicated Meteorological x86 Operating System                     ");
            System.Console.WriteLine("==============================================================================");
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
            string ipDisplay = WeatherOS.Services.WeatherService.IsDebugModeActive ? WeatherOS.Services.RawTcpHttp.GetAssignedIp() : WeatherOS.Services.RawTcpHttp.GetMaskedIp();
            System.Console.WriteLine($"] IPv4 Network Stack : {ipDisplay}");

            System.Console.WriteLine(" [ OK ] Hardware Target    : Standard x86 PC (Standalone Mode)");

            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.WriteLine("------------------------------------------------------------------------------");
            System.Console.WriteLine(" Type 'help' to view commands or 'gui' to launch the meteorological dashboard.");
            System.Console.WriteLine("------------------------------------------------------------------------------");
            System.Console.ResetColor();
        }
    }
}
