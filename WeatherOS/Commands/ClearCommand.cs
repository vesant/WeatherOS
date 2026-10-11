using System;

namespace WeatherOS.Commands
{
    public class ClearCommand : ICommand
    {
        private Action _onClear;

        public string Name => "clear";
        public string Description => "Clear the screen.";

        public ClearCommand(Action onClear)
        {
            _onClear = onClear;
        }

        public void Execute(string[] args)
        {
            _onClear?.Invoke();
        }
    }
}
