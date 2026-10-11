using System;
using WeatherOS.Graphics;

namespace WeatherOS.Commands
{
    public class HelpCommand : ICommand
    {
        private CommandManager _manager;
        private TuiDesktop _desktop;

        public string Name => "help";
        public string Description => "Show available CLI commands and GUI apps.";

        public HelpCommand(CommandManager manager, TuiDesktop desktop)
        {
            _manager = manager;
            _desktop = desktop;
        }

        public void Execute(string[] args)
        {
            System.Console.WriteLine("Available Core Commands:");
            foreach (var cmd in _manager.GetCommands())
            {
                System.Console.WriteLine($"  {cmd.Name.PadRight(16)} - {cmd.Description}");
            }

            System.Console.WriteLine("\nAvailable Graphical Apps (Type to Launch Directly):");
            string[] appNames = _desktop.GetAppNames();
            string[] appIds = _desktop.GetAppIds();
            for (int i = 0; i < appIds.Length; i++)
            {
                System.Console.WriteLine($"  {appIds[i].PadRight(16)} - {appNames[i]}");
            }
        }
    }
}
