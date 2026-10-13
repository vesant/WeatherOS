using System;
using Cosmos.System;
using WeatherOS.Graphics;

namespace WeatherOS.Apps
{
    public class WeatherDashboardApp : IApp
    {
        public string Id => "weather";
        public string Name => "Weather Dashboard";
        public bool IsRealTime => false;

        public void Draw(TuiDesktop desktop)
        {
            var win = desktop.CreateWindow("WEATHER APPLICATION", WindowSize.Large);

            var data = desktop.WeatherService.GetLatestReading();

            desktop.DrawWeatherIcon(data.Condition, data.IsNight, "", win.InnerLeft + 2, win.InnerTop + 1);

            // Day/Night indicator and station status under icon
            win.WriteAt(2, 6, data.IsNight ? "  [NIGHT TIME]  " : "   [DAYLIGHT]   ", data.IsNight ? ConsoleColor.Cyan : ConsoleColor.Yellow);
            win.WriteAt(2, 7, "  Station: Live   ", ConsoleColor.White);
            win.WriteAt(2, 8, "  Telemetry: OK   ", ConsoleColor.Cyan);

            // Weather telemetry metrics (Right side)
            win.WriteAt(24, 1, "LOCATION: " + data.Location, ConsoleColor.Yellow);
            win.WriteAt(24, 3, "Condition : " + data.Condition, ConsoleColor.White);
            win.WriteAt(24, 5, "Temp      : " + ((int)data.TemperatureCelsius).ToString() + " C", ConsoleColor.White);
            win.WriteAt(24, 7, "Humidity  : " + ((int)data.HumidityPercent).ToString() + " %", ConsoleColor.White);
            win.WriteAt(24, 9, "Precip    : " + data.PrecipitationMm.ToString() + " mm", ConsoleColor.White);

            win.WriteAt(44, 5, "Pressure : " + ((int)data.PressureHpa).ToString() + " hPa", ConsoleColor.White);
            win.WriteAt(44, 7, "Wind Spd : " + ((int)data.WindSpeedKmh).ToString() + " km/h", ConsoleColor.White);
            win.WriteAt(44, 9, "Wind Dir : " + data.WindDirection, ConsoleColor.White);

            // Alert Banner
            if (data.AlertLevel == "RED")
            {
                string alertText = " [!] SEVERE WARNING: " + data.AlertMessage;
                string footer = " Take immediate safety precautions. Monitor local authorities.";
                win.WriteRow(11, alertText, ConsoleColor.White, 1, ConsoleColor.Red, true);
                win.WriteRow(12, footer, ConsoleColor.White, 1, ConsoleColor.Red, true);
            }
            else if (data.AlertLevel == "YELLOW")
            {
                string alertText = " [*] WEATHER ADVISORY: " + data.AlertMessage;
                string footer = " Active regional hazards detected. Stay alert and plan ahead.";
                win.WriteRow(11, alertText, ConsoleColor.Black, 1, ConsoleColor.Yellow, true);
                win.WriteRow(12, footer, ConsoleColor.Black, 1, ConsoleColor.Yellow, true);
            }
            else
            {
                string alertText = " [OK] STATUS: All clear. No severe weather hazards reported.";
                string footer = " Atmospheric conditions within normal regional safety limits.";
                win.WriteRow(11, alertText, ConsoleColor.White, 1, ConsoleColor.DarkGreen, true);
                win.WriteRow(12, footer, ConsoleColor.White, 1, ConsoleColor.DarkGreen, true);
            }

            // Essential shortcut bar
            win.WriteFooter("[R] Refresh  |  [C] Change City  |  [F] 7-Day Forecast  |  [ESC] Exit", ConsoleColor.Yellow);
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.Escape)
            {
                desktop.SetCurrentApp("launcher");
            }
            else if (keyEvent.Key == ConsoleKeyEx.F || keyEvent.Key == ConsoleKeyEx.D7 || keyEvent.Key == ConsoleKeyEx.Num7)
            {
                desktop.SetCurrentApp("forecast_7d");
            }
            else if (keyEvent.KeyChar == 'c' || keyEvent.KeyChar == 'C')
            {
                desktop.SetCurrentApp("change_city");
            }
            else if (keyEvent.KeyChar == 'r' || keyEvent.KeyChar == 'R')
            {
                var cur = desktop.WeatherService.GetLatestReading();
                string city = cur.Location.Split(',')[0].Trim();
                desktop.WeatherService.FetchOpenWeather(city, desktop.ApiKey);
            }
        }
    }
}
