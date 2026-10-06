using System;
using System.Drawing;
using Cosmos.System.Graphics;
using Cosmos.System.Graphics.Fonts;
using WeatherOS.Services;

namespace WeatherOS.Graphics
{
    public class WeatherGuiRenderer
    {
        private Canvas _canvas;
        private Mode _screenMode;

        // Custom palette
        private readonly Color _colorBackground = Color.FromArgb(47, 54, 64);
        private readonly Pen _penCardBg = new Pen(Color.FromArgb(53, 59, 72));
        private readonly Pen _penBorder = new Pen(Color.FromArgb(113, 128, 147));
        private readonly Pen _penSun = new Pen(Color.FromArgb(251, 197, 49));
        private readonly Pen _penSunCorona = new Pen(Color.FromArgb(225, 177, 44));
        private readonly Pen _penTempBar = new Pen(Color.FromArgb(232, 65, 24));
        private readonly Pen _penTempBarBg = new Pen(Color.FromArgb(127, 143, 166));
        private readonly Pen _penHumidityBar = new Pen(Color.FromArgb(0, 168, 255));
        private readonly Pen _penPressureBar = new Pen(Color.FromArgb(46, 204, 113));
        private readonly Pen _penText = new Pen(Color.FromArgb(245, 246, 250));
        private readonly Pen _penTextDim = new Pen(Color.FromArgb(164, 176, 190));

        // Pre-allocated Font reference
        private readonly Cosmos.System.Graphics.Fonts.Font _font;

        private bool _isActive;
        public bool IsActive => _isActive;

        public WeatherGuiRenderer()
        {
            _screenMode = new Mode(800, 600, ColorDepth.ColorDepth32);
            _font = PCScreenFont.Default;
        }

        public bool Start()
        {
            try
            {
                _canvas = FullScreenCanvas.GetFullScreenCanvas(_screenMode);
                _canvas.Clear(_colorBackground);
                _isActive = true;
                return true;
            }
            catch (Exception)
            {
                _isActive = false;
                return false;
            }
        }

        public void Render(ref WeatherData data)
        {
            if (!_isActive || _canvas == null) return;

            // Clear background
            _canvas.Clear(_colorBackground);

            // Draw Top Banner
            _canvas.DrawString("WEATHER OS - SATELLITE TELEMETRY", _font, _penText, 30, 20);
            _canvas.DrawString("Real-time meteorological monitoring dashboard", _font, _penTextDim, 30, 40);

            // Left Card: Core Metrics
            DrawDataCard(30, 80, 350, 480, "PRIMARY SENSORS");
            
            _canvas.DrawString("LOCAL TEMPERATURE", _font, _penTextDim, 50, 130);
            _canvas.DrawString($"{data.TemperatureCelsius:F1} C", _font, _penSun, 50, 150);
            DrawProgressBar(50, 170, 250, 15, data.TemperatureCelsius, -10f, 50f, _penTempBar, _penTempBarBg);

            _canvas.DrawString("RELATIVE HUMIDITY", _font, _penTextDim, 50, 220);
            _canvas.DrawString($"{data.HumidityPercent:F1} %", _font, _penText, 50, 240);
            DrawProgressBar(50, 260, 250, 15, data.HumidityPercent, 0f, 100f, _penHumidityBar, _penTempBarBg);

            _canvas.DrawString("ATMOSPHERIC PRESSURE", _font, _penTextDim, 50, 310);
            _canvas.DrawString($"{data.PressureHpa:F1} hPa", _font, _penText, 50, 330);
            DrawProgressBar(50, 350, 250, 15, data.PressureHpa, 950f, 1050f, _penPressureBar, _penTempBarBg);

            // Right Card: Environment & Wind
            DrawDataCard(410, 80, 350, 480, "ENVIRONMENTAL DATA");
            
            _canvas.DrawString("SKY CONDITION", _font, _penTextDim, 430, 130);
            _canvas.DrawString(data.Condition, _font, _penText, 430, 150);

            // Draw simple sun/weather icon
            _canvas.DrawFilledCircle(_penSunCorona, 650, 170, 40);
            _canvas.DrawFilledCircle(_penSun, 650, 170, 35);

            _canvas.DrawString("WIND DYNAMICS", _font, _penTextDim, 430, 260);
            _canvas.DrawString($"Speed: {data.WindSpeedKmh:F1} km/h", _font, _penText, 430, 280);
            _canvas.DrawString($"Heading: {data.WindDirection}", _font, _penText, 430, 300);

            // Draw simple compass representation
            _canvas.DrawRectangle(_penTextDim, 600, 250, 100, 100);
            _canvas.DrawString("N", _font, _penText, 645, 255);
            _canvas.DrawString("S", _font, _penText, 645, 335);
            _canvas.DrawString("W", _font, _penText, 605, 295);
            _canvas.DrawString("E", _font, _penText, 685, 295);

            _canvas.DrawString("SYSTEM STATUS: ONLINE (LOCAL MODE)", _font, _penTextDim, 30, 580);
            _canvas.DrawString("PRESS [ESC] TO REBOOT AND RETURN TO TERMINAL", _font, _penSunCorona, 450, 580);

            _canvas.Display();
        }

        private void DrawDataCard(int x, int y, int width, int height, string title)
        {
            // Background
            _canvas.DrawFilledRectangle(_penCardBg, x, y, width, height);
            
            // Border
            _canvas.DrawRectangle(_penBorder, x, y, width, height);
            
            // Card Title Header
            _canvas.DrawFilledRectangle(_penBorder, x, y, width, 30);
            _canvas.DrawString(title, _font, _penText, x + 15, y + 8);
        }

        private void DrawProgressBar(int x, int y, int width, int height, float value, float min, float max, Pen fillPen, Pen bgPen)
        {
            _canvas.DrawFilledRectangle(bgPen, x, y, width, height);
            
            float clampedValue = Math.Max(min, Math.Min(value, max));
            float percentage = (clampedValue - min) / (max - min);
            int fillWidth = (int)(width * percentage);

            if (fillWidth > 0)
            {
                _canvas.DrawFilledRectangle(fillPen, x, y, fillWidth, height);
            }
        }

        public void Stop()
        {
            _isActive = false;
        }
    }
}
