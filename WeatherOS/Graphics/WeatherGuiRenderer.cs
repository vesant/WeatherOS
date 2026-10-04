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

            _canvas.Clear(_colorBackground);

            // Header Bar
            _canvas.DrawFilledRectangle(_penCardBg, 20, 20, 760, 50);
            _canvas.DrawRectangle(_penBorder, 20, 20, 760, 50);
            _canvas.DrawString("WeatherOS - Painel Meteorologico", _font, _penText, 40, 36);
            _canvas.DrawString("[VBE 800x600x32]", _font, _penTextDim, 640, 36);

            // Sun and Weather Condition Visual Card
            _canvas.DrawFilledRectangle(_penCardBg, 20, 90, 240, 420);
            _canvas.DrawRectangle(_penBorder, 20, 90, 240, 420);
            _canvas.DrawString("CONDICAO ATUAL", _font, _penTextDim, 35, 105);

            int sunCenterX = 140;
            int sunCenterY = 220;
            int sunRadius = 45;

            _canvas.DrawFilledCircle(_penSun, sunCenterX, sunCenterY, sunRadius);
            
            _canvas.DrawLine(_penSunCorona, sunCenterX - 65, sunCenterY, sunCenterX - 50, sunCenterY);
            _canvas.DrawLine(_penSunCorona, sunCenterX + 50, sunCenterY, sunCenterX + 65, sunCenterY);
            _canvas.DrawLine(_penSunCorona, sunCenterX, sunCenterY - 65, sunCenterX, sunCenterY - 50);
            _canvas.DrawLine(_penSunCorona, sunCenterX, sunCenterY + 50, sunCenterX, sunCenterY + 65);
            _canvas.DrawLine(_penSunCorona, sunCenterX - 45, sunCenterY - 45, sunCenterX - 35, sunCenterY - 35);
            _canvas.DrawLine(_penSunCorona, sunCenterX + 35, sunCenterY + 35, sunCenterX + 45, sunCenterY + 45);
            _canvas.DrawLine(_penSunCorona, sunCenterX + 35, sunCenterY - 35, sunCenterX + 45, sunCenterY - 45);
            _canvas.DrawLine(_penSunCorona, sunCenterX - 45, sunCenterY + 45, sunCenterX - 35, sunCenterY + 35);

            _canvas.DrawString(data.Condition, _font, _penText, 35, 300);
            _canvas.DrawString("Estacao: PC Local (x86)", _font, _penTextDim, 35, 330);

            // Temperature Chart
            _canvas.DrawFilledRectangle(_penCardBg, 280, 90, 500, 130);
            _canvas.DrawRectangle(_penBorder, 280, 90, 500, 130);
            _canvas.DrawString("TEMPERATURA AMBIENTE", _font, _penTextDim, 300, 105);
            
            _canvas.DrawFilledRectangle(_penTempBarBg, 300, 140, 450, 24);
            int tempWidth = (int)((data.TemperatureCelsius / 50.0f) * 450);
            if (tempWidth < 0) tempWidth = 0;
            if (tempWidth > 450) tempWidth = 450;
            _canvas.DrawFilledRectangle(_penTempBar, 300, 140, tempWidth, 24);
            _canvas.DrawRectangle(_penBorder, 300, 140, 450, 24);
            _canvas.DrawString("Temp: ~21.5 C (Escala 0 a 50 C)", _font, _penText, 300, 180);

            // Humidity Chart
            _canvas.DrawFilledRectangle(_penCardBg, 280, 235, 500, 130);
            _canvas.DrawRectangle(_penBorder, 280, 235, 500, 130);
            _canvas.DrawString("HUMIDADE RELATIVA DO AR", _font, _penTextDim, 300, 250);

            _canvas.DrawFilledRectangle(_penTempBarBg, 300, 285, 450, 24);
            int humWidth = (int)((data.HumidityPercent / 100.0f) * 450);
            if (humWidth < 0) humWidth = 0;
            if (humWidth > 450) humWidth = 450;
            _canvas.DrawFilledRectangle(_penHumidityBar, 300, 285, humWidth, 24);
            _canvas.DrawRectangle(_penBorder, 300, 285, 450, 24);
            _canvas.DrawString("Humidade: ~58 % (Escala 0 a 100 %)", _font, _penText, 300, 325);

            // Atmospheric Pressure
            _canvas.DrawFilledRectangle(_penCardBg, 280, 380, 500, 130);
            _canvas.DrawRectangle(_penBorder, 280, 380, 500, 130);
            _canvas.DrawString("PRESSAO ATMOSFERICA E VENTO", _font, _penTextDim, 300, 395);
            _canvas.DrawString("Pressao: 1014.2 hPa (Normal: 1013.25 hPa)", _font, _penText, 300, 430);
            _canvas.DrawString("Vento: 12.8 km/h Direcao: NNW", _font, _penText, 300, 460);

            // Footer
            _canvas.DrawFilledRectangle(_penCardBg, 20, 530, 760, 50);
            _canvas.DrawRectangle(_penBorder, 20, 530, 760, 50);
            _canvas.DrawString("Controlos: Pressiona [ESC] ou [Q] para reiniciar o sistema (Voltar ao Terminal)", _font, _penText, 40, 547);

            _canvas.Display();
        }

        public void Stop()
        {
            if (!_isActive) return;

            try
            {
                _canvas.Disable();
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
