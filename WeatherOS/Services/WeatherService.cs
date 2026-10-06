using System;

namespace WeatherOS.Services
{
    /// <summary>
    /// Represents structured meteorological observation data.
    /// Uses value-type semantics or reusable structures to prevent Garbage Collector overhead in Cosmos.
    /// </summary>
    public struct WeatherData
    {
        public float TemperatureCelsius;
        public float HumidityPercent;
        public float PressureHpa;
        public float WindSpeedKmh;
        public string WindDirection;
        public string Condition;
        public DateTime Timestamp;

        public WeatherData(float temp, float humidity, float pressure, float windSpeed, string windDir, string condition)
        {
            TemperatureCelsius = temp;
            HumidityPercent = humidity;
            PressureHpa = pressure;
            WindSpeedKmh = windSpeed;
            WindDirection = windDir;
            Condition = condition;
            Timestamp = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Service responsible for managing meteorological telemetry.
    /// Acts as a mock provider currently, with architecture ready for TCP socket ingestion from Arduino or remote stations.
    /// </summary>
    public class WeatherService
    {
        private WeatherData _cachedReading;
        private int _simulationCycle = 0;

        public WeatherService()
        {
            // Initial baseline weather conditions
            _cachedReading = new WeatherData(
                temp: 21.5f,
                humidity: 58.0f,
                pressure: 1014.2f,
                windSpeed: 12.8f,
                windDir: "NNW",
                condition: "Clear Sky / Sunny"
            );
        }

        public WeatherData GetLatestReading()
        {
            _simulationCycle = (_simulationCycle + 1) % 10;

            // Micro-variations around baseline to simulate active telemetry
            _cachedReading.TemperatureCelsius = 21.0f + (_simulationCycle * 0.2f);
            _cachedReading.HumidityPercent = 58.0f + ((_simulationCycle % 5) * 0.5f);
            _cachedReading.PressureHpa = 1013.8f + ((_simulationCycle % 3) * 0.3f);
            _cachedReading.WindSpeedKmh = 12.0f + (_simulationCycle * 0.4f);

            return _cachedReading;
        }

        private void ParseTelemetryPacket(string packet)
        {
            // Exemplo de pacote: T:22.5;H:60;P:1012;W:15;D:N;C:Chuva
            string[] parts = packet.Split(';');
            foreach (var part in parts)
            {
                if (part.Length < 3) continue;
                
                string key = part.Substring(0, 2); // "T:"
                string val = part.Substring(2);    // "22.5"

                try
                {
                    if (key == "T:") _cachedReading.TemperatureCelsius = float.Parse(val);
                    else if (key == "H:") _cachedReading.HumidityPercent = float.Parse(val);
                    else if (key == "P:") _cachedReading.PressureHpa = float.Parse(val);
                    else if (key == "W:") _cachedReading.WindSpeedKmh = float.Parse(val);
                    else if (key == "D:") _cachedReading.WindDirection = val;
                    else if (key == "C:") _cachedReading.Condition = val;
                }
                catch { }
            }
            _cachedReading.Timestamp = DateTime.UtcNow;
        }

        /// <summary>
        /// Calculates the approximate dew point natively on the PC CPU (Magnus-Tetens simplification).
        /// Zero allocations, pure value-type arithmetic.
        /// </summary>
        public float GetDewPointCelsius()
        {
            return _cachedReading.TemperatureCelsius - ((100.0f - _cachedReading.HumidityPercent) / 5.0f);
        }
    }
}
