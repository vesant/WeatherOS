using System;

namespace WeatherOS.Commands
{
    public class InstallCommand : ICommand
    {
        public string Name => "install";
        public string Description => "Launch the hard disk installation wizard.";

        public void Execute(string[] args)
        {
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine("\n[!] WeatherOS Hard Disk Installation Wizard");
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.WriteLine("--------------------------------------------------------------------------------");
            System.Console.WriteLine("The hard disk installation wizard and partitioner are currently under");
            System.Console.WriteLine("active development for bare-metal ATA/SATA controller targets.");
            System.Console.WriteLine();
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.WriteLine("You are currently running WeatherOS in Live OS (RAM) mode.");
            System.Console.WriteLine("All features, weather telemetry, network stacks, and diagnostic tools");
            System.Console.WriteLine("are fully available in this live session without modifying local drives.");
            System.Console.WriteLine("--------------------------------------------------------------------------------\n");
            System.Console.ResetColor();
        }
    }
}
