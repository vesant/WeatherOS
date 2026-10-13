using System;

namespace WeatherOS.Commands
{
    public class InstallCommand : ICommand
    {
        private Action _launchInstaller;

        public string Name => "install";
        public string Description => "Launch the hard disk installation wizard.";

        public InstallCommand(Action launchInstaller = null)
        {
            _launchInstaller = launchInstaller;
        }

        public void Execute(string[] args)
        {
            if (_launchInstaller != null)
            {
                System.Console.WriteLine("Launching Hard Disk Installation Wizard...");
                _launchInstaller();
            }
            else
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.WriteLine("\n[!] Launching WeatherOS Setup Wizard...");
                System.Console.ResetColor();
            }
        }
    }
}
