using System;
using WeatherOS.Services;

namespace WeatherOS.Commands
{
    public class WeatherCommand : ICommand
    {
        private WeatherService _weatherService;

        public string Name => "weather";
        public string Description => "Display local meteorological telemetry report.";

        public WeatherCommand(WeatherService weatherService)
        {
            _weatherService = weatherService;
        }

        public void Execute(string[] args)
        {
            var data = _weatherService.GetLatestReading();

            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.WriteLine("\n[LOCAL TELEMETRY REPORT]");
            System.Console.WriteLine("==================================================");
            System.Console.ResetColor();

            System.Console.WriteLine("  Source Station     : Local Weather Engine (Standalone x86 PC)");
            System.Console.WriteLine("  Location           : " + data.Location);
            System.Console.WriteLine("  Condition          : " + data.Condition);
            
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine("  Temperature        : " + ((int)data.TemperatureCelsius).ToString() + " C");
            System.Console.ResetColor();

            System.Console.ForegroundColor = ConsoleColor.Blue;
            System.Console.WriteLine("  Relative Humidity  : " + ((int)data.HumidityPercent).ToString() + " %");
            System.Console.ResetColor();

            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine("  Atmospheric Press. : " + ((int)data.PressureHpa).ToString() + " hPa");
            System.Console.ResetColor();

            System.Console.WriteLine("  Wind Speed / Dir   : " + ((int)data.WindSpeedKmh).ToString() + " km/h (" + data.WindDirection + ")");
            System.Console.WriteLine("  Precipitation      : " + data.PrecipitationMm.ToString() + " mm");
            System.Console.WriteLine("--------------------------------------------------");
            if (data.AlertLevel == "RED")
            {
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine("  [!] SEVERE WARNING : " + data.AlertMessage);
                System.Console.ResetColor();
            }
            else if (data.AlertLevel == "YELLOW")
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.WriteLine("  [*] ADVISORY       : " + data.AlertMessage);
                System.Console.ResetColor();
            }
            else
            {
                System.Console.ForegroundColor = ConsoleColor.Green;
                System.Console.WriteLine("  [OK] STATUS        : All clear. No severe weather hazards reported.");
                System.Console.ResetColor();
            }
            System.Console.WriteLine("--------------------------------------------------");
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.WriteLine("  Note: Type 'gui' to inspect visual bar charts.\n");
            System.Console.ResetColor();
        }
    }
}
