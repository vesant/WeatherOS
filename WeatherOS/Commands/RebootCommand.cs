using System;
using Cosmos.System;

namespace WeatherOS.Commands
{
    public class RebootCommand : ICommand
    {
        public string Name => "reboot";
        public string Description => "Restart the operating system.";

        public void Execute(string[] args)
        {
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine("Rebooting system...");
            System.Console.ResetColor();
            Power.Reboot();
        }
    }
}
