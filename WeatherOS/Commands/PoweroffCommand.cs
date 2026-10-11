using System;
using Cosmos.System;

namespace WeatherOS.Commands
{
    public class PoweroffCommand : ICommand
    {
        public string Name => "poweroff";
        public string Description => "Safely shut down the computer (ACPI Poweroff).";

        public void Execute(string[] args)
        {
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine("Shutting down WeatherOS system...");
            System.Console.ResetColor();
            Power.Shutdown();
        }
    }
}
