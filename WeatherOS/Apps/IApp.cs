using Cosmos.System;
using WeatherOS.Graphics;

namespace WeatherOS.Apps
{
    public interface IApp
    {
        string Id { get; }
        string Name { get; }
        bool IsRealTime { get; }
        void Draw(TuiDesktop desktop);
        void HandleInput(TuiDesktop desktop, KeyEvent keyEvent);
    }
}
