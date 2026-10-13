using System;
using WeatherOS.Graphics;

namespace WeatherOS.Commands
{
    public class BootMenuCommand : ICommand
    {
        private Action _onReturn;

        public string Name => "bootmenu";
        public string Description => "Open the WeatherOS Boot Selection Manager.";

        public BootMenuCommand(Action onReturn)
        {
            _onReturn = onReturn;
        }

        public void Execute(string[] args)
        {
            BootMenu.Show();
            _onReturn?.Invoke();
        }
    }
}
