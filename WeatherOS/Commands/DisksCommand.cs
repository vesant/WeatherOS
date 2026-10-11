using System;
using System.Collections.Generic;
using WeatherOS.Services;

namespace WeatherOS.Commands
{
    public class DisksCommand : ICommand
    {
        public string Name => "disks";
        public string Description => "List all physical disks, partitions and file systems (ext4, FAT32, NTFS, exFAT)";

        public void Execute(string[] args)
        {
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.WriteLine("\n=== PHYSICAL STORAGE & FILESYSTEM DETECTOR ===");
            System.Console.ResetColor();

            var partitions = FileSystemProbeService.ProbeAllPartitions();

            if (partitions == null || partitions.Count == 0)
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.WriteLine("No block storage partitions detected via Cosmos HAL.");
                System.Console.ForegroundColor = ConsoleColor.Cyan;
                System.Console.WriteLine("\n[!] To diagnose why internal disks are not showing up (AHCI / SATA / IDE), run:");
                System.Console.ForegroundColor = ConsoleColor.White;
                System.Console.WriteLine("    diskdiag   - Hardware storage controller & port diagnostic report");
                System.Console.ResetColor();
                return;
            }

            System.Console.WriteLine("--------------------------------------------------------------------------------");
            System.Console.WriteLine(string.Format("{0,-12} | {1,-18} | {2,-8} | {3,-12} | {4}", "DEVICE", "FILESYSTEM", "SIZE", "MOUNT", "STATUS"));
            System.Console.WriteLine("--------------------------------------------------------------------------------");

            foreach (var p in partitions)
            {
                string dev = p.DeviceName;
                if (dev.Length > 12) dev = dev.Substring(0, 12);

                string fs = p.FileSystemType;
                if (fs.Length > 18) fs = fs.Substring(0, 18);

                string size = p.SizeDisplay;
                string mount = !string.IsNullOrEmpty(p.MountedDriveLetter) ? p.MountedDriveLetter : "--";
                
                string status = p.IsLinuxSystem ? "Linux Target" : (p.IsWritable ? "Writable" : "Read-Only");

                if (p.IsLinuxSystem)
                {
                    System.Console.ForegroundColor = ConsoleColor.Green;
                }
                else if (p.IsWritable)
                {
                    System.Console.ForegroundColor = ConsoleColor.Cyan;
                }
                else
                {
                    System.Console.ForegroundColor = ConsoleColor.DarkGray;
                }

                System.Console.WriteLine(string.Format("{0,-12} | {1,-18} | {2,-8} | {3,-12} | {4}", dev, fs, size, mount, status));

                if (!string.IsNullOrEmpty(p.VolumeLabel) || !string.IsNullOrEmpty(p.Uuid))
                {
                    System.Console.ForegroundColor = ConsoleColor.DarkYellow;
                    string extra = "  -> ";
                    if (!string.IsNullOrEmpty(p.VolumeLabel)) extra += $"Label: [{p.VolumeLabel}] ";
                    if (!string.IsNullOrEmpty(p.Uuid)) extra += $"UUID: {p.Uuid}";
                    System.Console.WriteLine(extra);
                }
            }

            System.Console.ResetColor();
            System.Console.WriteLine("--------------------------------------------------------------------------------");
            System.Console.ForegroundColor = ConsoleColor.White;
            System.Console.WriteLine("Tip: All detected partitions can be formatted to FAT32 using 'format <drive> fat32'.");
            System.Console.WriteLine("     Use 'diskmgr' to view and format partitions interactively in GUI.\n");
            System.Console.ResetColor();
        }
    }
}
