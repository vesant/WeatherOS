using System;

namespace WeatherOS.Commands
{
    public class TransferCommand : ICommand
    {
        private Action _onLaunchTransfer;

        public string Name => "transfer";
        public string Description => "Network File Transfer & OTA Streaming Utility (UDP 7331).";

        public TransferCommand(Action onLaunchTransfer)
        {
            _onLaunchTransfer = onLaunchTransfer;
        }

        public void Execute(string[] args)
        {
            _onLaunchTransfer?.Invoke();
        }
    }
}
