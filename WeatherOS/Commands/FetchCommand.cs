using System;
using WeatherOS.Services;

namespace WeatherOS.Commands
{
    public class FetchCommand : ICommand
    {
        private WeatherService _weatherService;
        private Func<string> _getApiKey;

        public string Name => "fetch";
        public string Description => "Pull live weather data for any city (e.g. fetch Porto).";

        public FetchCommand(WeatherService weatherService, Func<string> getApiKey)
        {
            _weatherService = weatherService;
            _getApiKey = getApiKey;
        }

        public void Execute(string[] args)
        {
            if (args.Length < 2)
            {
                System.Console.WriteLine("Usage: fetch <city> (Example: fetch Lisbon)");
                return;
            }

            string city = string.Join(" ", args, 1, args.Length - 1).Trim();
            System.Console.WriteLine($"Fetching live weather for '{city}'...");
            _weatherService.FetchOpenWeather(city, _getApiKey?.Invoke() ?? "");
            var data = _weatherService.GetLatestReading();

            System.Console.WriteLine($"\n  -> {data.Location}: {data.TemperatureCelsius} C, {data.Condition}");
            System.Console.WriteLine($"  -> Humidity: {data.HumidityPercent}%, Pressure: {data.PressureHpa} hPa, Wind: {data.WindSpeedKmh} km/h, Precip: {data.PrecipitationMm} mm");
            
            if (data.AlertLevel == "RED")
            {
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine($"  -> [!] SEVERE WARNING: {data.AlertMessage}");
                System.Console.ResetColor();
            }
            else if (data.AlertLevel == "YELLOW")
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.WriteLine($"  -> [*] WEATHER ADVISORY: {data.AlertMessage}");
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
