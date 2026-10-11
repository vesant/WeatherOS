using System;
using WeatherOS.Graphics;

namespace WeatherOS.Commands
{
    public class GuiCommand : ICommand
    {
        private Action _onSwitchToGui;

        public string Name => "gui";
        public string Description => "Switch to ASCII Graphical Dashboard Menu.";

        public GuiCommand(Action onSwitchToGui)
        {
            _onSwitchToGui = onSwitchToGui;
        }

        public void Execute(string[] args)
        {
            _onSwitchToGui?.Invoke();
        }
    }
}
