using System;
using System.IO;
using WeatherOS.Services;

namespace WeatherOS.Commands
{
    public class SetCityCommand : ICommand
    {
        private WeatherService _weatherService;
        private Func<string> _getApiKey;

        public string Name => "set-city";
        public string Description => "Set default location and save to disk (e.g. set-city Setubal).";

        public SetCityCommand(WeatherService weatherService, Func<string> getApiKey)
        {
            _weatherService = weatherService;
            _getApiKey = getApiKey;
        }

        public void Execute(string[] args)
        {
            if (args.Length < 2)
            {
                System.Console.WriteLine("Usage: set-city <city> (Example: set-city Setubal)");
                return;
            }

            string newCity = string.Join(" ", args, 1, args.Length - 1).Trim();
            try
            {
                string status;
                StorageService.SaveFile("settings.ini", $"DarkMode=false\nAutoLoc=false\nDefaultCity={newCity}\nDebugLog={WeatherService.IsDebugModeActive}\nCustomKey=\n", out status);
            }
            catch { }

            System.Console.WriteLine($"Default city saved to disk: {newCity}");
            _weatherService.FetchOpenWeather(newCity, _getApiKey?.Invoke() ?? "");
            var d = _weatherService.GetLatestReading();

            System.Console.WriteLine($"\n  -> {d.Location}: {d.TemperatureCelsius} C, {d.Condition}");
            if (d.AlertLevel == "RED")
            {
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine($"  -> [!] SEVERE WARNING: {d.AlertMessage}");
                System.Console.ResetColor();
            }
            else if (d.AlertLevel == "YELLOW")
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.WriteLine($"  -> [*] WEATHER ADVISORY: {d.AlertMessage}");
                System.Console.ResetColor();
            }
            else
            {
                System.Console.ForegroundColor = ConsoleColor.Green;
                System.Console.WriteLine($"  -> [OK] STATUS: All clear. No severe weather hazards.");
                System.Console.ResetColor();
            }
        }
    }
}
