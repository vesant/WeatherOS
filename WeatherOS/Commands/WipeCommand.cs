using System;
using WeatherOS.Services;

namespace WeatherOS.Commands
{
    public class WipeCommand : ICommand
    {
        public string Name => "wipe";
        public string Description => "Completely erase/wipe a physical disk to clean state (wipe <disk_idx>)";

        public void Execute(string[] args)
        {
            var drives = DiskWipeService.GetPhysicalDrives();

            if (drives == null || drives.Count == 0)
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.WriteLine("[wipe] No physical block drives detected.");
                System.Console.ResetColor();
                return;
            }

            if (args.Length == 0)
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.WriteLine("\n=== PHYSICAL DISK WIPE & CLEAN UTILITY ===");
                System.Console.ResetColor();
                System.Console.WriteLine("Available Physical Drives:");
                for (int i = 0; i < drives.Count; i++)
                {
                    string fsList = drives[i].ContainedFilesystems.Count > 0 ? string.Join(", ", drives[i].ContainedFilesystems) : "None/Raw";
                    System.Console.WriteLine($"  [{i}] {drives[i].Name} - Size: {drives[i].SizeDisplay} (Detected FS: {fsList})");
                }

                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine("\nWARNING: Wiping a disk permanently erases all filesystems (ext4, NTFS, FAT32)!");
                System.Console.WriteLine("To clean a disk, specify its index, for example:");
                System.Console.ForegroundColor = ConsoleColor.White;
                System.Console.WriteLine("  wipe 0      (wipes Physical Disk 0)\n");
                System.Console.ResetColor();
                return;
            }

            int targetIndex = 0;
            if (!int.TryParse(args[0], out targetIndex) || targetIndex < 0 || targetIndex >= drives.Count)
            {
                System.Console.ForegroundColor = ConsoleColor.Red;
                System.Console.WriteLine($"[ERR] Invalid disk index: '{args[0]}'. Run 'wipe' without arguments to view drives.");
                System.Console.ResetColor();
                return;
            }

            var chosen = drives[targetIndex];

            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine($"\n[WIPE] STARTING COMPLETE DISK WIPE ON {chosen.Name} ({chosen.SizeDisplay})...");
            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine("  • Zeroing Master Boot Record (MBR)...");
            System.Console.WriteLine("  • Zeroing GUID Partition Tables (Primary & Backup GPT)...");
            System.Console.WriteLine("  • Wiping filesystem superblocks (ext4, NTFS, FAT)...");
            System.Console.WriteLine("  • Initializing fresh partition table...");
            System.Console.WriteLine("  • Formatting clean FAT32 volume...");
            System.Console.ResetColor();

            string status;
            bool ok = DiskWipeService.WipeAndCleanDisk(targetIndex, out status);

            if (ok)
            {
                System.Console.ForegroundColor = ConsoleColor.Green;
                System.Console.WriteLine("\n" + status + "\n");
            }
            else
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.WriteLine("\n" + status + "\n");
            }
            System.Console.ResetColor();
        }
    }
}
