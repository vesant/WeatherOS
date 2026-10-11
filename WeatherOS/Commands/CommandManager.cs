using System;
using System.Collections.Generic;

namespace WeatherOS.Commands
{
    public class CommandManager
    {
        private List<ICommand> _commands = new List<ICommand>();

        public void Register(ICommand command)
        {
            if (command != null)
            {
                _commands.Add(command);
            }
        }

        public List<ICommand> GetCommands()
        {
            return _commands;
        }

        public bool Execute(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return true;

            string[] parts = input.Trim().Split(' ');
            string cmdName = parts[0].ToLower();

            foreach (var cmd in _commands)
            {
                if (cmd.Name.ToLower() == cmdName)
                {
                    cmd.Execute(parts);
                    return true;
                }
            }

            return false;
        }
    }
}
