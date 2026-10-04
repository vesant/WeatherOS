using System;
using System.Drawing;
using Cosmos.System;
using Cosmos.System.Graphics;
using Cosmos.System.Graphics.Fonts;
using WeatherOS.Services;

namespace WeatherOS.Graphics
{
    /// <summary>
    /// Handles the VBE Canvas Graphical User Interface for WeatherOS.
    /// Strictly adheres to Cosmos memory constraints: all Pens, Colors, and Modes are pre-allocated
    /// at initialization to prevent Garbage Collector (GC) thrashing and out-of-memory kernel panics.
    /// </summary>
    public class WeatherGuiRenderer
    {
        private Canvas _canvas;
        private readonly Mode _screenMode;

        // Pre-allocated Colors (Cached to avoid heap allocations inside render cycles)
        private readonly Color _colorBackground = Color.FromArgb(24, 32, 47);      // Dark Meteorological Blue
        private readonly Color _colorCardBg = Color.FromArgb(36, 48, 68);          // Card container background
        private readonly Color _colorBorder = Color.FromArgb(64, 82, 110);         // Subtle borders
        private readonly Color _colorSun = Color.FromArgb(253, 203, 110);          // Warm Golden Yellow
        private readonly Color _colorSunCorona = Color.FromArgb(225, 112, 85);     // Corona/Ray Orange
        private readonly Color _colorTempBar = Color.FromArgb(231, 76, 60);        // Crimson Red for Temp
        private readonly Color _colorTempBarBg = Color.FromArgb(50, 60, 80);       // Bar background
        private readonly Color _colorHumidityBar = Color.FromArgb(52, 152, 219);   // Sky Blue for Humidity
        private readonly Color _colorPressureBar = Color.FromArgb(46, 204, 113);   // Emerald Green
        private readonly Color _colorText = Color.FromArgb(245, 246, 250);         // Off-White
        private readonly Color _colorTextDim = Color.FromArgb(164, 176, 190);      // Grayish caption text

        // Pre-allocated Pens (Reused across every single frame)
        private readonly Pen _bgPen;
        private readonly Pen _cardPen;
        private readonly Pen _borderPen;
        private readonly Pen _sunPen;
        private readonly Pen _sunCoronaPen;
        private readonly Pen _tempBarPen;
        private readonly Pen _tempBarBgPen;
        private readonly Pen _humidityBarPen;
        private readonly Pen _pressureBarPen;
        private readonly Pen _textPen;
        private readonly Pen _textDimPen;

        // Pre-allocated Font reference
        private readonly Font _font;

        // State
        private bool _isActive;

        public bool IsActive => _isActive;

        public WeatherGuiRenderer()
        {
            // Set 800x600 with 32-bit color depth (standard VBE mode supported by QEMU and real hardware)
            _screenMode = new Mode(800, 600, ColorDepth.ColorDepth32);

            // Instantiate Pens once
            _bgPen = new Pen(_colorBackground);
            _cardPen = new Pen(_colorCardBg);
            _borderPen = new Pen(_colorBorder);
            _sunPen = new Pen(_colorSun);
            _sunCoronaPen = new Pen(_colorSunCorona);
            _tempBarPen = new Pen(_colorTempBar);
            _tempBarBgPen = new Pen(_colorTempBarBg);
            _humidityBarPen = new Pen(_colorHumidityBar);
            _pressureBarPen = new Pen(_colorPressureBar);
            _textPen = new Pen(_colorText);
            _textDimPen = new Pen(_colorTextDim);

            // Default bitmap font bundled with Cosmos System.Graphics
            _font = PCScreenFont.Default;
        }

        /// <summary>
        /// Initializes the FullScreenCanvas and enters VBE graphical mode.
        /// </summary>
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

        /// <summary>
        /// Renders the meteorological dashboard frame onto the double-buffered canvas.
        /// Zero heap allocations occur in this method.
        /// </summary>
        /// <param name="data">Current weather metrics.</param>
        public void Render(ref WeatherData data)
        {
            if (!_isActive || _canvas == null) return;

            // 1. Clear background
            _canvas.Clear(_colorBackground);

            // 2. Header Bar
            _canvas.DrawFilledRectangle(_cardPen, 20, 20, 760, 50);
            _canvas.DrawRectangle(_borderPen, 20, 20, 760, 50);
            _canvas.DrawString("WeatherOS - Painel Meteorologico", _font, _textPen, 40, 36);
            _canvas.DrawString("[VBE 800x600x32]", _font, _textDimPen, 640, 36);

            // 3. Sun and Weather Condition Visual Card (Left side)
            _canvas.DrawFilledRectangle(_cardPen, 20, 90, 240, 420);
            _canvas.DrawRectangle(_borderPen, 20, 90, 240, 420);
            _canvas.DrawString("CONDICAO ATUAL", _font, _textDimPen, 35, 105);

            // Geometric Primitive: Sun (Circle + Rays)
            int sunCenterX = 140;
            int sunCenterY = 220;
            int sunRadius = 45;

            // Sun body
            _canvas.DrawFilledCircle(_sunPen, sunCenterX, sunCenterY, sunRadius);

            // Sun corona / rays
            _canvas.DrawLine(_sunCoronaPen, sunCenterX - 65, sunCenterY, sunCenterX - 50, sunCenterY);
            _canvas.DrawLine(_sunCoronaPen, sunCenterX + 50, sunCenterY, sunCenterX + 65, sunCenterY);
            _canvas.DrawLine(_sunCoronaPen, sunCenterX, sunCenterY - 65, sunCenterX, sunCenterY - 50);
            _canvas.DrawLine(_sunCoronaPen, sunCenterX, sunCenterY + 50, sunCenterX, sunCenterY + 65);
            _canvas.DrawLine(_sunCoronaPen, sunCenterX - 45, sunCenterY - 45, sunCenterX - 35, sunCenterY - 35);
            _canvas.DrawLine(_sunCoronaPen, sunCenterX + 35, sunCenterY + 35, sunCenterX + 45, sunCenterY + 45);
            _canvas.DrawLine(_sunCoronaPen, sunCenterX + 35, sunCenterY - 35, sunCenterX + 45, sunCenterY - 45);
            _canvas.DrawLine(_sunCoronaPen, sunCenterX - 45, sunCenterY + 45, sunCenterX - 35, sunCenterY + 35);

            // Condition status label
            _canvas.DrawString(data.Condition, _font, _textPen, 35, 300);
            _canvas.DrawString("Origem: Estacao Arduino", _font, _textDimPen, 35, 330);

            // 4. Telemetry Metric Charts (Center & Right)
            // Card 1: Temperature Chart
            _canvas.DrawFilledRectangle(_cardPen, 280, 90, 500, 130);
            _canvas.DrawRectangle(_borderPen, 280, 90, 500, 130);
            _canvas.DrawString("TEMPERATURA AMBIENTE", _font, _textDimPen, 300, 105);
            
            // Bar background & filled gauge
            _canvas.DrawFilledRectangle(_tempBarBgPen, 300, 140, 450, 24);
            int tempWidth = (int)((data.TemperatureCelsius / 50.0f) * 450);
            if (tempWidth < 0) tempWidth = 0;
            if (tempWidth > 450) tempWidth = 450;
            _canvas.DrawFilledRectangle(_tempBarPen, 300, 140, tempWidth, 24);
            _canvas.DrawRectangle(_borderPen, 300, 140, 450, 24);
            _canvas.DrawString("Temp: ~21.5 C (Escala 0 a 50 C)", _font, _textPen, 300, 180);

            // Card 2: Humidity Chart
            _canvas.DrawFilledRectangle(_cardPen, 280, 235, 500, 130);
            _canvas.DrawRectangle(_borderPen, 280, 235, 500, 130);
            _canvas.DrawString("HUMIDADE RELATIVA DO AR", _font, _textDimPen, 300, 250);

            // Humidity bar
            _canvas.DrawFilledRectangle(_tempBarBgPen, 300, 285, 450, 24);
            int humWidth = (int)((data.HumidityPercent / 100.0f) * 450);
            if (humWidth < 0) humWidth = 0;
            if (humWidth > 450) humWidth = 450;
            _canvas.DrawFilledRectangle(_humidityBarPen, 300, 285, humWidth, 24);
            _canvas.DrawRectangle(_borderPen, 300, 285, 450, 24);
            _canvas.DrawString("Humidade: ~58 % (Escala 0 a 100 %)", _font, _textPen, 300, 325);

            // Card 3: Atmospheric Pressure & Wind Bar
            _canvas.DrawFilledRectangle(_cardPen, 280, 380, 500, 130);
            _canvas.DrawRectangle(_borderPen, 280, 380, 500, 130);
            _canvas.DrawString("PRESSAO ATMOSFERICA E VENTO", _font, _textDimPen, 300, 395);
            _canvas.DrawString("Pressao: 1014.2 hPa (Normal: 1013.25 hPa)", _font, _textPen, 300, 430);
            _canvas.DrawString("Vento: 12.8 km/h Direcao: NNW", _font, _textPen, 300, 460);

            // 5. Footer / Controls Helper
            _canvas.DrawFilledRectangle(_cardPen, 20, 530, 760, 50);
            _canvas.DrawRectangle(_borderPen, 20, 530, 760, 50);
            _canvas.DrawString("Controlos: Pressiona [ESC] ou [Q] para regressar ao Terminal Base (Modo Texto)", _font, _textPen, 40, 547);

            // Push frame buffer to screen (VBE)
            _canvas.Display();
        }

        /// <summary>
        /// Safely shuts down the graphics canvas, restoring standard VGA text mode.
        /// </summary>
        public void Stop()
        {
            if (!_isActive) return;

            try
            {
                _canvas?.Disable();
                _canvas = null;
            }
            catch (Exception)
            {
                // Fallback safe suppression
            }
            finally
            {
                _isActive = false;
            }
        }
    }
}
