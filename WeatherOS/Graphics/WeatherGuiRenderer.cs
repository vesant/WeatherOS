using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using WeatherOS.Services;

namespace WeatherOS.Graphics
{
    public class WeatherGuiRenderer
    {
        private Canvas _canvas;
        private Mode _screenMode;

        // Custom palette
        private readonly Color _colorBackground = Color.FromArgb(47, 54, 64);      // Dark slate
        private readonly Color _colorCardBg = Color.FromArgb(53, 59, 72);          // Lighter slate
        private readonly Color _colorBorder = Color.FromArgb(113, 128, 147);       // Grayish border
        private readonly Color _colorSun = Color.FromArgb(251, 197, 49);           // Sunflower yellow
        private readonly Color _colorSunCorona = Color.FromArgb(225, 177, 44);     // Darker yellow
        private readonly Color _colorTempBar = Color.FromArgb(232, 65, 24);        // Fire red
        private readonly Color _colorTempBarBg = Color.FromArgb(127, 143, 166);    // Dimmed bar bg
        private readonly Color _colorHumidityBar = Color.FromArgb(0, 168, 255);    // Azure blue
        private readonly Color _colorPressureBar = Color.FromArgb(46, 204, 113);   // Emerald Green
        private readonly Color _colorText = Color.FromArgb(245, 246, 250);         // Off-White
        private readonly Color _colorTextDim = Color.FromArgb(164, 176, 190);      // Grayish caption text

        // Pre-allocated Font reference
        private readonly Cosmos.Kernel.System.Graphics.Fonts.Font _font;

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

            _canvas.Clear(_colorBackground);

            // Header Bar
            _canvas.DrawFilledRectangle(_colorCardBg, 20, 20, 760, 50);
            _canvas.DrawRectangle(_colorBorder, 20, 20, 760, 50);
            _canvas.DrawString("WeatherOS - Painel Meteorologico", _font, _colorText, 40, 36);
            _canvas.DrawString("[VBE 800x600x32]", _font, _colorTextDim, 640, 36);

            // Sun and Weather Condition Visual Card
            _canvas.DrawFilledRectangle(_colorCardBg, 20, 90, 240, 420);
            _canvas.DrawRectangle(_colorBorder, 20, 90, 240, 420);
            _canvas.DrawString("CONDICAO ATUAL", _font, _colorTextDim, 35, 105);

            int sunCenterX = 140;
            int sunCenterY = 220;
            int sunRadius = 45;

            _canvas.DrawFilledCircle(_colorSun, sunCenterX, sunCenterY, sunRadius);
            
            _canvas.DrawLine(_colorSunCorona, sunCenterX - 65, sunCenterY, sunCenterX - 50, sunCenterY);
            _canvas.DrawLine(_colorSunCorona, sunCenterX + 50, sunCenterY, sunCenterX + 65, sunCenterY);
            _canvas.DrawLine(_colorSunCorona, sunCenterX, sunCenterY - 65, sunCenterX, sunCenterY - 50);
            _canvas.DrawLine(_colorSunCorona, sunCenterX, sunCenterY + 50, sunCenterX, sunCenterY + 65);
            _canvas.DrawLine(_colorSunCorona, sunCenterX - 45, sunCenterY - 45, sunCenterX - 35, sunCenterY - 35);
            _canvas.DrawLine(_colorSunCorona, sunCenterX + 35, sunCenterY + 35, sunCenterX + 45, sunCenterY + 45);
            _canvas.DrawLine(_colorSunCorona, sunCenterX + 35, sunCenterY - 35, sunCenterX + 45, sunCenterY - 45);
            _canvas.DrawLine(_colorSunCorona, sunCenterX - 45, sunCenterY + 45, sunCenterX - 35, sunCenterY + 35);

            _canvas.DrawString(data.Condition, _font, _colorText, 35, 300);
            _canvas.DrawString("Estacao: PC Local (x86)", _font, _colorTextDim, 35, 330);

            // Temperature Chart
            _canvas.DrawFilledRectangle(_colorCardBg, 280, 90, 500, 130);
            _canvas.DrawRectangle(_colorBorder, 280, 90, 500, 130);
            _canvas.DrawString("TEMPERATURA AMBIENTE", _font, _colorTextDim, 300, 105);
            
            _canvas.DrawFilledRectangle(_colorTempBarBg, 300, 140, 450, 24);
            int tempWidth = (int)((data.TemperatureCelsius / 50.0f) * 450);
            if (tempWidth < 0) tempWidth = 0;
            if (tempWidth > 450) tempWidth = 450;
            _canvas.DrawFilledRectangle(_colorTempBar, 300, 140, tempWidth, 24);
            _canvas.DrawRectangle(_colorBorder, 300, 140, 450, 24);
            _canvas.DrawString("Temp: ~21.5 C (Escala 0 a 50 C)", _font, _colorText, 300, 180);

            // Humidity Chart
            _canvas.DrawFilledRectangle(_colorCardBg, 280, 235, 500, 130);
            _canvas.DrawRectangle(_colorBorder, 280, 235, 500, 130);
            _canvas.DrawString("HUMIDADE RELATIVA DO AR", _font, _colorTextDim, 300, 250);

            _canvas.DrawFilledRectangle(_colorTempBarBg, 300, 285, 450, 24);
            int humWidth = (int)((data.HumidityPercent / 100.0f) * 450);
            if (humWidth < 0) humWidth = 0;
            if (humWidth > 450) humWidth = 450;
            _canvas.DrawFilledRectangle(_colorHumidityBar, 300, 285, humWidth, 24);
            _canvas.DrawRectangle(_colorBorder, 300, 285, 450, 24);
            _canvas.DrawString("Humidade: ~58 % (Escala 0 a 100 %)", _font, _colorText, 300, 325);

            // Atmospheric Pressure
            _canvas.DrawFilledRectangle(_colorCardBg, 280, 380, 500, 130);
            _canvas.DrawRectangle(_colorBorder, 280, 380, 500, 130);
            _canvas.DrawString("PRESSAO ATMOSFERICA E VENTO", _font, _colorTextDim, 300, 395);
            _canvas.DrawString("Pressao: 1014.2 hPa (Normal: 1013.25 hPa)", _font, _colorText, 300, 430);
            _canvas.DrawString("Vento: 12.8 km/h Direcao: NNW", _font, _colorText, 300, 460);

            // Footer
            _canvas.DrawFilledRectangle(_colorCardBg, 20, 530, 760, 50);
            _canvas.DrawRectangle(_colorBorder, 20, 530, 760, 50);
            _canvas.DrawString("Controlos: Pressiona [ESC] ou [Q] para regressar ao Terminal Base", _font, _colorText, 40, 547);

            _canvas.Display();
        }

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
            }
            finally
            {
                _isActive = false;
            }
        }
    }
}
