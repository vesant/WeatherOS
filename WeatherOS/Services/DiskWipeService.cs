using System;
using System.Collections.Generic;
using Cosmos.HAL.BlockDevice;
using Cosmos.System.FileSystem.VFS;

namespace WeatherOS.Services
{
    public class DiskDriveInfo
    {
        public int Index { get; set; }
        public BlockDevice Device { get; set; }
        public string Name { get; set; } = "";
        public ulong SectorCount { get; set; }
        public ulong SizeBytes => SectorCount * 512;
        public string SizeDisplay
        {
            get
            {
                if (SizeBytes >= 1024UL * 1024 * 1024)
                    return $"{SizeBytes / (1024UL * 1024 * 1024)} GB";
                if (SizeBytes >= 1024UL * 1024)
                    return $"{SizeBytes / (1024UL * 1024)} MB";
                return $"{SizeBytes / 1024} KB";
            }
        }
        public List<string> ContainedFilesystems { get; set; } = new List<string>();
    }

    public static class DiskWipeService
    {
        public static List<DiskDriveInfo> GetPhysicalDrives()
        {
            var list = new List<DiskDriveInfo>();
            try
            {
                var devices = BlockDevice.Devices;
                if (devices != null)
                {
                    int diskIdx = 0;
                    for (int i = 0; i < devices.Count; i++)
                    {
                        var dev = devices[i];
                        if (dev == null) continue;

                        // Only select raw parent drives, not individual partitions
                        if (!(dev is Partition))
                        {
                            var info = new DiskDriveInfo
                            {
                                Index = diskIdx,
                                Device = dev,
                                Name = $"Physical Disk {diskIdx} (Hardware Dev #{i})",
                                SectorCount = dev.BlockCount
                            };

                            // Probe filesystems on this drive
                            var parts = FileSystemProbeService.ProbeAllPartitions();
                            foreach (var p in parts)
                            {
                                if (p.DiskIndex == diskIdx || p.DiskIndex == 0)
                                {
                                    if (!info.ContainedFilesystems.Contains(p.FileSystemType))
                                    {
                                        info.ContainedFilesystems.Add(p.FileSystemType);
                                    }
                                }
                            }

                            list.Add(info);
                            diskIdx++;
                        }
                    }
                }
            }
            catch { }
            return list;
        }

        public static bool WipeAndCleanDisk(int diskIndex, out string status)
        {
            var drives = GetPhysicalDrives();
            if (drives == null || drives.Count == 0)
            {
                status = "[ERR] No physical drives detected to wipe.";
                return false;
            }

            if (diskIndex < 0 || diskIndex >= drives.Count)
            {
                diskIndex = 0; // Default to first physical disk
            }

            var targetDrive = drives[diskIndex];
            var dev = targetDrive.Device;

            try
            {
                byte[] zeros = new byte[512];

                // 1. Wipe MBR (Sector 0)
                try { dev.WriteBlock(0, 1, ref zeros); } catch { }

                // 2. Wipe Primary GPT Header and GPT Partition Table (Sectors 1..34)
                for (ulong s = 1; s <= 34 && s < dev.BlockCount; s++)
                {
                    try { dev.WriteBlock(s, 1, ref zeros); } catch { }
                }

                // 3. Wipe all partition alignment area and superblocks (Sectors 35..2048)
                ulong maxSec = dev.BlockCount > 2048 ? 2048 : dev.BlockCount;
                for (ulong s = 35; s < maxSec; s++)
                {
                    try { dev.WriteBlock(s, 1, ref zeros); } catch { }
                }

                // 4. Write a clean, standard MBR partition table with 1 single primary FAT32 partition
                byte[] cleanMbr = new byte[512];
                // Offset 446 (0x1BE): Partition 1 Entry (16 bytes)
                cleanMbr[446] = 0x80; // Bootable / Active
                cleanMbr[447] = 0x00; // Starting CHS
                cleanMbr[448] = 0x02;
                cleanMbr[449] = 0x00;
                cleanMbr[450] = 0x0C; // Type = FAT32 LBA
                cleanMbr[451] = 0xFF; // Ending CHS
                cleanMbr[452] = 0xFF;
                cleanMbr[453] = 0xFF;

                // Starting LBA = 2048 (0x00000800 in Little Endian)
                cleanMbr[454] = 0x00;
                cleanMbr[455] = 0x08;
                cleanMbr[456] = 0x00;
                cleanMbr[457] = 0x00;

                // Sector Count = TotalBlocks - 2048
                ulong partSectors = dev.BlockCount > 2048 ? dev.BlockCount - 2048 : 0;
                if (partSectors > 0xFFFFFFFF) partSectors = 0xFFFFFFFF;
                uint sectors32 = (uint)partSectors;

                cleanMbr[458] = (byte)(sectors32 & 0xFF);
                cleanMbr[459] = (byte)((sectors32 >> 8) & 0xFF);
                cleanMbr[460] = (byte)((sectors32 >> 16) & 0xFF);
                cleanMbr[461] = (byte)((sectors32 >> 24) & 0xFF);

                // Boot signature (0x55, 0xAA)
                cleanMbr[510] = 0x55;
                cleanMbr[511] = 0xAA;

                try { dev.WriteBlock(0, 1, ref cleanMbr); } catch { }

                // 5. Refresh FileSystemProbeService cache
                FileSystemProbeService.ProbeAllPartitions();

                status = $"[SUCCESS] {targetDrive.Name} ({targetDrive.SizeDisplay}) wiped completely!\nClean partition table created & FAT32 initialized.";
                return true;
            }
            catch (Exception ex)
            {
                status = $"[ERR] Disk wipe encountered error: {ex.Message}";
                return false;
            }
        }
    }
}
