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
            desktop.DrawWindow(5, 3, 70, 18, "WEATHER APPLICATION");
            ConsoleColor winBg = desktop.IsDarkMode ? ConsoleColor.Black : ConsoleColor.Blue;
            System.Console.BackgroundColor = winBg;

            var data = desktop.WeatherService.GetLatestReading();

            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.SetCursorPosition(30, 5);
            System.Console.Write("LOCATION: " + data.Location);

            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.SetCursorPosition(30, 7);
            System.Console.Write("Condition : " + data.Condition);

            System.Console.SetCursorPosition(30, 9);
            System.Console.Write("Temp      : " + ((int)data.TemperatureCelsius).ToString() + " C");

            System.Console.SetCursorPosition(30, 11);
            System.Console.Write("Humidity  : " + ((int)data.HumidityPercent).ToString() + " %");

            System.Console.SetCursorPosition(30, 13);
            System.Console.Write("Precip    : " + data.PrecipitationMm.ToString() + " mm");

            System.Console.SetCursorPosition(50, 9);
            System.Console.Write("Pressure : " + ((int)data.PressureHpa).ToString() + " hPa");

            System.Console.SetCursorPosition(50, 11);
            System.Console.Write("Wind Spd : " + ((int)data.WindSpeedKmh).ToString() + " km/h");

            System.Console.SetCursorPosition(50, 13);
            System.Console.Write("Wind Dir : " + data.WindDirection);

            desktop.DrawWeatherIcon(data.Condition, data.IsNight, data.MoonPhase, 8, 6);

            // Astronomy and Moon phase details under icon
            System.Console.SetCursorPosition(8, 11);
            System.Console.ForegroundColor = data.IsNight ? ConsoleColor.Cyan : ConsoleColor.Yellow;
            System.Console.Write(data.IsNight ? "  [NIGHT TIME]  " : "   [DAYLIGHT]   ");

            System.Console.SetCursorPosition(8, 12);
            System.Console.ForegroundColor = ConsoleColor.White;
            string moonTxt = !string.IsNullOrEmpty(data.MoonPhase) ? $"{data.MoonPhase}" : "Sky: Clear";
            if (moonTxt.Length > 18) moonTxt = moonTxt.Substring(0, 18);
            System.Console.Write(moonTxt.PadRight(18));

            System.Console.SetCursorPosition(8, 13);
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            string illumTxt = !string.IsNullOrEmpty(data.MoonIllumination) ? $"Illum: {data.MoonIllumination}" : "";
            System.Console.Write(illumTxt.PadRight(18));

            // Alert Banner
            if (data.AlertLevel == "RED")
            {
                System.Console.SetCursorPosition(7, 16);
                System.Console.BackgroundColor = ConsoleColor.Red;
                System.Console.ForegroundColor = ConsoleColor.White;
                
                string alertText = " [!] SEVERE WARNING: " + data.AlertMessage + " ";
                if (alertText.Length > 66) alertText = alertText.Substring(0, 66);
                while (alertText.Length < 66) alertText += " ";
                System.Console.Write(alertText);

                System.Console.SetCursorPosition(7, 17);
                string footer = " Take immediate safety precautions. Monitor local authorities. ";
                if (footer.Length > 66) footer = footer.Substring(0, 66);
                while (footer.Length < 66) footer += " ";
                System.Console.Write(footer);
            }
            else if (data.AlertLevel == "YELLOW")
            {
                System.Console.SetCursorPosition(7, 16);
                System.Console.BackgroundColor = ConsoleColor.Yellow;
                System.Console.ForegroundColor = ConsoleColor.Black;
                
                string alertText = " [*] WEATHER ADVISORY: " + data.AlertMessage + " ";
                if (alertText.Length > 66) alertText = alertText.Substring(0, 66);
                while (alertText.Length < 66) alertText += " ";
                System.Console.Write(alertText);

                System.Console.SetCursorPosition(7, 17);
                string footer = " Active regional hazards detected. Stay alert and plan ahead. ";
                if (footer.Length > 66) footer = footer.Substring(0, 66);
                while (footer.Length < 66) footer += " ";
                System.Console.Write(footer);
            }
            else
            {
                System.Console.SetCursorPosition(7, 16);
                System.Console.BackgroundColor = ConsoleColor.DarkGreen;
                System.Console.ForegroundColor = ConsoleColor.White;
                
                string alertText = " [OK] STATUS: All clear. No severe weather hazards reported. ";
                if (alertText.Length > 66) alertText = alertText.Substring(0, 66);
                while (alertText.Length < 66) alertText += " ";
                System.Console.Write(alertText);

                System.Console.SetCursorPosition(7, 17);
                string footer = " Atmospheric conditions within normal regional safety limits. ";
                if (footer.Length > 66) footer = footer.Substring(0, 66);
                while (footer.Length < 66) footer += " ";
                System.Console.Write(footer);
            }
        }

        public void HandleInput(TuiDesktop desktop, KeyEvent keyEvent)
        {
            if (keyEvent.Key == ConsoleKeyEx.F || keyEvent.Key == ConsoleKeyEx.D7 || keyEvent.Key == ConsoleKeyEx.Num7)
            {
                desktop.SetCurrentApp("forecast_7d");
            }
        }
    }
}
