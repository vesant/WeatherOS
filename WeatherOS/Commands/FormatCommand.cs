using System;
using WeatherOS.Services;

namespace WeatherOS.Commands
{
    public class FormatCommand : ICommand
    {
        public string Name => "format";
        public string Description => "Format a drive or partition as FAT32 (format <drive> [fat32])";

        public void Execute(string[] args)
        {
            if (args.Length == 0)
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.WriteLine("Usage: format <drive_or_partition> [fat32]");
                System.Console.WriteLine("Example: format 0:\\ fat32   OR   format 0 fat32");
                System.Console.ResetColor();
                return;
            }

            string targetDrive = args[0].Trim();
            string fsTarget = args.Length > 1 ? args[1].ToUpper().Trim() : "FAT32";

            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.WriteLine($"[format] Preparing volume: {targetDrive}...");
            System.Console.WriteLine($"[format] Formatting as {fsTarget}. All data on this partition will be erased.");
            System.Console.ResetColor();

            string status;
            bool ok = StorageService.FormatDrive(targetDrive, fsTarget, out status);

            if (ok)
            {
                System.Console.ForegroundColor = ConsoleColor.Green;
                System.Console.WriteLine(status);
            }
            else
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.WriteLine(status);
            }
            System.Console.ResetColor();
        }
    }
}
