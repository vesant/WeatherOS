using System;
using System.Collections.Generic;
using System.Text;
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
                if (SectorCount == 0 || SectorCount > 0x000FFFFFFFFFFFFFUL)
                    return "Raw / Unmeasured";
                ulong gb = SizeBytes / (1024UL * 1024 * 1024);
                if (gb > 20000)
                    return "Raw / Unmeasured";
                if (gb >= 1)
                    return $"{gb} GB";
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

        public static bool FormatDriveAsFat32(BlockDevice dev, out string status)
        {
            if (dev == null)
            {
                status = "[ERR] Invalid BlockDevice specified.";
                return false;
            }

            try
            {
                ulong totalBlocks = dev.BlockCount;
                if (totalBlocks == 0 || totalBlocks > 0x000FFFFFFFFFFFFFUL)
                {
                    totalBlocks = 204800; // Safe 100MB minimum
                }

                ulong startLba = 2048; // Standard 1MB alignment
                ulong partSectors = totalBlocks > startLba ? totalBlocks - startLba : 0;
                if (partSectors > 0xFFFFFFFF) partSectors = 0xFFFFFFFF;
                uint sectors32 = (uint)partSectors;

                // 1. Write MBR at Sector 0 with 1 clean active FAT32 partition
                byte[] mbr = new byte[512];
                // Partition 1 Entry (offset 446 / 0x1BE)
                mbr[446] = 0x80; // Bootable / Active
                mbr[447] = 0x00; // Starting CHS
                mbr[448] = 0x02;
                mbr[449] = 0x00;
                mbr[450] = 0x0C; // Type = FAT32 LBA
                mbr[451] = 0xFF; // Ending CHS
                mbr[452] = 0xFF;
                mbr[453] = 0xFF;

                // Starting LBA = 2048
                mbr[454] = (byte)(startLba & 0xFF);
                mbr[455] = (byte)((startLba >> 8) & 0xFF);
                mbr[456] = (byte)((startLba >> 16) & 0xFF);
                mbr[457] = (byte)((startLba >> 24) & 0xFF);

                // Sector Count
                mbr[458] = (byte)(sectors32 & 0xFF);
                mbr[459] = (byte)((sectors32 >> 8) & 0xFF);
                mbr[460] = (byte)((sectors32 >> 16) & 0xFF);
                mbr[461] = (byte)((sectors32 >> 24) & 0xFF);

                // Signature 0x55, 0xAA
                mbr[510] = 0x55;
                mbr[511] = 0xAA;
                dev.WriteBlock(0, 1, ref mbr);

                // 2. Compute FAT32 Parameters
                ushort reservedSectors = 32;
                byte sectorsPerCluster = 8; // 4KB cluster
                byte numFats = 2;
                uint dataSectors = sectors32 > reservedSectors ? sectors32 - reservedSectors : 0;
                uint totalClusters = dataSectors / sectorsPerCluster;
                uint fatBytes = (totalClusters + 2) * 4;
                uint fatSectors = (fatBytes + 511) / 512;
                if (fatSectors < 32) fatSectors = 32;

                // 3. Write Volume Boot Record (BPB) at startLba (2048) and backup at 2054
                byte[] bpb = new byte[512];
                bpb[0] = 0xEB; bpb[1] = 0x58; bpb[2] = 0x90; // JMP short + NOP
                byte[] oem = Encoding.ASCII.GetBytes("MSWIN4.1");
                Array.Copy(oem, 0, bpb, 3, 8);

                bpb[11] = 0x00; bpb[12] = 0x02; // Bytes per sector = 512
                bpb[13] = sectorsPerCluster;     // Sectors per cluster = 8
                bpb[14] = (byte)(reservedSectors & 0xFF);
                bpb[15] = (byte)((reservedSectors >> 8) & 0xFF);
                bpb[16] = numFats;              // Number of FATs = 2
                bpb[17] = 0x00; bpb[18] = 0x00; // Root entries = 0
                bpb[19] = 0x00; bpb[20] = 0x00; // Total sectors 16 = 0
                bpb[21] = 0xF8;                 // Media descriptor = fixed disk
                bpb[22] = 0x00; bpb[23] = 0x00; // FAT size 16 = 0
                bpb[24] = 0x3F; bpb[25] = 0x00; // Sectors per track = 63
                bpb[26] = 0xFF; bpb[27] = 0x00; // Heads = 255
                // Hidden sectors = startLba (2048)
                bpb[28] = (byte)(startLba & 0xFF);
                bpb[29] = (byte)((startLba >> 8) & 0xFF);
                bpb[30] = (byte)((startLba >> 16) & 0xFF);
                bpb[31] = (byte)((startLba >> 24) & 0xFF);
                // Total sectors 32
                bpb[32] = (byte)(sectors32 & 0xFF);
                bpb[33] = (byte)((sectors32 >> 8) & 0xFF);
                bpb[34] = (byte)((sectors32 >> 16) & 0xFF);
                bpb[35] = (byte)((sectors32 >> 24) & 0xFF);

                // Extended FAT32 BPB
                bpb[36] = (byte)(fatSectors & 0xFF);
                bpb[37] = (byte)((fatSectors >> 8) & 0xFF);
                bpb[38] = (byte)((fatSectors >> 16) & 0xFF);
                bpb[39] = (byte)((fatSectors >> 24) & 0xFF);
                bpb[40] = 0x00; bpb[41] = 0x00; // Ext flags
                bpb[42] = 0x00; bpb[43] = 0x00; // FS version 0.0
                bpb[44] = 0x02; bpb[45] = 0x00; bpb[46] = 0x00; bpb[47] = 0x00; // Root cluster = 2
                bpb[48] = 0x01; bpb[49] = 0x00; // FSInfo sector = 1
                bpb[50] = 0x06; bpb[51] = 0x00; // Backup boot sector = 6
                bpb[64] = 0x80;                 // Physical drive number
                bpb[66] = 0x29;                 // Extended boot signature
                bpb[67] = 0x24; bpb[68] = 0x20; bpb[69] = 0x88; bpb[70] = 0x19; // Volume ID
                byte[] volLabel = Encoding.ASCII.GetBytes("WEATHEROS  ");
                Array.Copy(volLabel, 0, bpb, 71, 11);
                byte[] fsType = Encoding.ASCII.GetBytes("FAT32   ");
                Array.Copy(fsType, 0, bpb, 82, 8);
                bpb[510] = 0x55; bpb[511] = 0xAA;

                dev.WriteBlock(startLba, 1, ref bpb);
                dev.WriteBlock(startLba + 6, 1, ref bpb); // Backup boot sector

                // 4. Write FSInfo Sector at startLba + 1 (2049) and backup at 2055
                byte[] fsinfo = new byte[512];
                fsinfo[0] = 0x52; fsinfo[1] = 0x52; fsinfo[2] = 0x61; fsinfo[3] = 0x41; // 0x41615252 Lead Sig
                fsinfo[484] = 0x72; fsinfo[485] = 0x72; fsinfo[486] = 0x41; fsinfo[487] = 0x61; // 0x61417272 Struct Sig
                uint freeClusters = totalClusters > 2 ? totalClusters - 2 : 0;
                fsinfo[488] = (byte)(freeClusters & 0xFF);
                fsinfo[489] = (byte)((freeClusters >> 8) & 0xFF);
                fsinfo[490] = (byte)((freeClusters >> 16) & 0xFF);
                fsinfo[491] = (byte)((freeClusters >> 24) & 0xFF);
                fsinfo[492] = 0x03; fsinfo[493] = 0x00; fsinfo[494] = 0x00; fsinfo[495] = 0x00; // Next free cluster = 3
                fsinfo[508] = 0x00; fsinfo[509] = 0x00; fsinfo[510] = 0x55; fsinfo[511] = 0xAA; // 0xAA550000 Trail Sig

                dev.WriteBlock(startLba + 1, 1, ref fsinfo);
                dev.WriteBlock(startLba + 7, 1, ref fsinfo); // Backup FSInfo

                // 5. Write FAT1 and FAT2 Tables
                byte[] fatSector0 = new byte[512];
                // Cluster 0: Media descriptor
                fatSector0[0] = 0xF8; fatSector0[1] = 0xFF; fatSector0[2] = 0xFF; fatSector0[3] = 0x0F;
                // Cluster 1: EOF marker
                fatSector0[4] = 0xFF; fatSector0[5] = 0xFF; fatSector0[6] = 0xFF; fatSector0[7] = 0x0F;
                // Cluster 2: Root Directory EOF marker
                fatSector0[8] = 0xFF; fatSector0[9] = 0xFF; fatSector0[10] = 0xFF; fatSector0[11] = 0x0F;

                ulong fat1Lba = startLba + reservedSectors;
                ulong fat2Lba = fat1Lba + fatSectors;
                dev.WriteBlock(fat1Lba, 1, ref fatSector0);
                dev.WriteBlock(fat2Lba, 1, ref fatSector0);

                // Zero out next few sectors of FAT tables
                byte[] zeros = new byte[512];
                for (ulong s = 1; s < 4 && s < fatSectors; s++)
                {
                    dev.WriteBlock(fat1Lba + s, 1, ref zeros);
                    dev.WriteBlock(fat2Lba + s, 1, ref zeros);
                }

                // 6. Write Root Directory (Cluster 2)
                ulong rootDirLba = startLba + reservedSectors + (fatSectors * 2);
                byte[] rootDirSec = new byte[512];
                // Write Volume Label Entry at entry 0
                Array.Copy(volLabel, 0, rootDirSec, 0, 11);
                rootDirSec[11] = 0x08; // Volume Label Attribute

                dev.WriteBlock(rootDirLba, 1, ref rootDirSec);
                for (ulong r = 1; r < sectorsPerCluster; r++)
                {
                    dev.WriteBlock(rootDirLba + r, 1, ref zeros);
                }

                // 7. Sync Cosmos VFS if available
                try
                {
                    var vfsDisks = VFSManager.GetDisks();
                    if (vfsDisks != null && vfsDisks.Count > 0)
                    {
                        vfsDisks[0].FormatPartition(0, "Fat32", true);
                    }
                }
                catch { }

                FileSystemProbeService.ProbeAllPartitions();
                status = $"[SUCCESS] Disk formatted as FAT32 Primary Storage (Partition 1: {partSectors / 2048}MB, Label: WEATHEROS).";
                return true;
            }
            catch (Exception ex)
            {
                status = $"[ERR] FAT32 format failed: {ex.Message}";
                return false;
            }
        }

        public static bool WriteFileDirectToFat32(BlockDevice dev, string filename, string content, out string status)
        {
            if (dev == null)
            {
                status = "BlockDevice is null.";
                return false;
            }

            try
            {
                ulong startLba = 2048;
                ushort reservedSectors = 32;
                byte sectorsPerCluster = 8;
                ulong totalBlocks = dev.BlockCount;
                if (totalBlocks == 0 || totalBlocks > 0x000FFFFFFFFFFFFFUL) totalBlocks = 204800;

                ulong partSectors = totalBlocks > startLba ? totalBlocks - startLba : 0;
                uint sectors32 = (uint)partSectors;
                uint dataSectors = sectors32 > reservedSectors ? sectors32 - reservedSectors : 0;
                uint totalClusters = dataSectors / sectorsPerCluster;
                uint fatBytes = (totalClusters + 2) * 4;
                uint fatSectors = (fatBytes + 511) / 512;
                if (fatSectors < 32) fatSectors = 32;

                ulong fat1Lba = startLba + reservedSectors;
                ulong fat2Lba = fat1Lba + fatSectors;
                ulong rootDirLba = startLba + reservedSectors + (fatSectors * 2);

                // Cluster 3 starts immediately after root directory cluster 2
                ulong cluster3Lba = rootDirLba + sectorsPerCluster;

                // Read root directory sector 0
                byte[] rootDirSec = new byte[512];
                try { dev.ReadBlock(rootDirLba, 1, ref rootDirSec); } catch { }

                // Find free entry (entry 1 onwards)
                int entryOffset = 32; // Offset 32 is entry 1
                for (int e = 1; e < 16; e++)
                {
                    int off = e * 32;
                    if (rootDirSec[off] == 0x00 || rootDirSec[off] == 0xE5)
                    {
                        entryOffset = off;
                        break;
                    }
                }

                // Format 8.3 filename
                string rawName = filename.ToUpper();
                string name8 = "UNTITLED";
                string ext3 = "TXT";
                int dotIdx = rawName.LastIndexOf('.');
                if (dotIdx > 0)
                {
                    string n = rawName.Substring(0, dotIdx);
                    if (n.Length > 8) n = n.Substring(0, 8);
                    name8 = n.PadRight(8);

                    string x = rawName.Substring(dotIdx + 1);
                    if (x.Length > 3) x = x.Substring(0, 3);
                    ext3 = x.PadRight(3);
                }
                else
                {
                    if (rawName.Length > 8) rawName = rawName.Substring(0, 8);
                    name8 = rawName.PadRight(8);
                }

                byte[] nameBytes = Encoding.ASCII.GetBytes(name8 + ext3);
                Array.Copy(nameBytes, 0, rootDirSec, entryOffset, 11);
                rootDirSec[entryOffset + 11] = 0x20; // Archive attribute
                rootDirSec[entryOffset + 20] = 0x00; // Cluster High = 0
                rootDirSec[entryOffset + 21] = 0x00;
                rootDirSec[entryOffset + 26] = 0x03; // Cluster Low = 3
                rootDirSec[entryOffset + 27] = 0x00;

                byte[] contentBytes = Encoding.ASCII.GetBytes(content ?? "");
                uint fileSize = (uint)contentBytes.Length;
                rootDirSec[entryOffset + 28] = (byte)(fileSize & 0xFF);
                rootDirSec[entryOffset + 29] = (byte)((fileSize >> 8) & 0xFF);
                rootDirSec[entryOffset + 30] = (byte)((fileSize >> 16) & 0xFF);
                rootDirSec[entryOffset + 31] = (byte)((fileSize >> 24) & 0xFF);

                dev.WriteBlock(rootDirLba, 1, ref rootDirSec);

                // Update FAT tables for Cluster 3: EOF marker
                byte[] fatSec0 = new byte[512];
                try { dev.ReadBlock(fat1Lba, 1, ref fatSec0); } catch { }
                // Offset for Cluster 3: 3 * 4 = 12
                fatSec0[12] = 0xFF; fatSec0[13] = 0xFF; fatSec0[14] = 0xFF; fatSec0[15] = 0x0F;
                dev.WriteBlock(fat1Lba, 1, ref fatSec0);
                dev.WriteBlock(fat2Lba, 1, ref fatSec0);

                // Write file content into Cluster 3
                byte[] dataSec = new byte[512];
                Array.Copy(contentBytes, 0, dataSec, 0, Math.Min(contentBytes.Length, 512));
                dev.WriteBlock(cluster3Lba, 1, ref dataSec);

                status = $"Saved to FAT32 Disk: {name8.Trim()}.{ext3.Trim()} ({fileSize} bytes) [OK]";
                return true;
            }
            catch (Exception ex)
            {
                status = $"Direct FAT32 write error: {ex.Message}";
                return false;
            }
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
                diskIndex = 0;
            }

            var targetDrive = drives[diskIndex];
            var dev = targetDrive.Device;

            try
            {
                byte[] zeros = new byte[512];

                // 1. Wipe MBR (Sector 0)
                try { dev.WriteBlock(0, 1, ref zeros); } catch { }

                // 2. Wipe GPT Header & GPT Table (Sectors 1..34)
                for (ulong s = 1; s <= 34 && s < dev.BlockCount; s++)
                {
                    try { dev.WriteBlock(s, 1, ref zeros); } catch { }
                }

                // 3. Wipe partition alignment area (Sectors 35..2048)
                ulong maxSec = dev.BlockCount > 2048 ? 2048 : dev.BlockCount;
                for (ulong s = 35; s < maxSec; s++)
                {
                    try { dev.WriteBlock(s, 1, ref zeros); } catch { }
                }

                // 4. Format clean FAT32 volume with 1 partition
                string fmtStatus;
                bool ok = FormatDriveAsFat32(dev, out fmtStatus);

                if (ok)
                {
                    status = $"[SUCCESS] {targetDrive.Name} ({targetDrive.SizeDisplay}) wiped & formatted as FAT32!\nPartition 1 ready for read/write.";
                    return true;
                }
                else
                {
                    status = $"[ERR] Wiped but format failed: {fmtStatus}";
                    return false;
                }
            }
            catch (Exception ex)
            {
                status = $"[ERR] Disk wipe encountered error: {ex.Message}";
                return false;
            }
        }
    }
}
