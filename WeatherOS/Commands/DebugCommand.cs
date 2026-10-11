using System;
using WeatherOS.Services;

namespace WeatherOS.Commands
{
    public class DebugCommand : ICommand
    {
        private WeatherService _weatherService;

        public string Name => "debug";
        public string Description => "Toggle detailed debug logs and IP privacy masking.";

        public DebugCommand(WeatherService weatherService)
        {
            _weatherService = weatherService;
        }

        public void Execute(string[] args)
        {
            bool newDbg = !WeatherService.IsDebugModeActive;
            _weatherService.SetDebugMode(newDbg);
            System.Console.WriteLine("Detailed Debug Mode: " + (newDbg ? "ON (Showing Raw IP/Logs)" : "OFF (IP Secured/Masked)"));
        }
    }
}
