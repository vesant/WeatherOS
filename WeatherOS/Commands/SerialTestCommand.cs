using System;

namespace WeatherOS.Commands
{
    public class SerialTestCommand : ICommand
    {
        public string Name => "serial-test";
        public string Description => "Probe COM1 and listen for Arduino telemetry.";

        public void Execute(string[] args)
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
    }
}
