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
                ulong mb = SizeBytes / (1024UL * 1024);
                if (mb > 20000000)
                    return "Raw / Unmeasured";
                if (mb >= 1024)
                {
                    ulong gb = mb / 1024;
                    ulong dec = ((mb % 1024) * 10) / 1024;
                    return dec > 0 ? $"{gb}.{dec} GB" : $"{gb} GB";
                }
                if (SizeBytes >= 1024UL * 1024)
                    return $"{SizeBytes / (1024UL * 1024)} MB";
                return $"{SizeBytes / 1024} KB";
            }
        }
        public List<string> ContainedFilesystems { get; set; } = new List<string>();
    }

    public class Fat32FileItem
    {
        public string Name { get; set; } = "";
        public uint Size { get; set; }
    }

    public class Fat32VolumeInfo
    {
        public ulong PartStartLba;
        public ushort BytesPerSector;
        public byte SectorsPerCluster;
        public ushort ReservedSectors;
        public byte NumFats;
        public uint FatSectors;
        public uint RootDirCluster;
        public ulong Fat1Lba => PartStartLba + ReservedSectors;
        public ulong Fat2Lba => Fat1Lba + FatSectors;
        public ulong DataRegionLba => PartStartLba + ReservedSectors + ((ulong)NumFats * FatSectors);

        public ulong GetClusterLba(uint cluster)
        {
            if (cluster < 2) return DataRegionLba;
            return DataRegionLba + ((ulong)(cluster - 2) * SectorsPerCluster);
        }
    }

    public static class DiskWipeService
    {
        public static List<DiskDriveInfo> GetPhysicalDrives()
        {
            var list = new List<DiskDriveInfo>();

            // 1. Probe Bare-Metal SATA AHCI driver first
            try
            {
                var ahciDrives = AhciDriverService.GetDrives();
                if (ahciDrives != null && ahciDrives.Count > 0)
                {
                    for (int a = 0; a < ahciDrives.Count; a++)
                    {
                        var ad = ahciDrives[a];
                        if (ad == null) continue;
                        if (ad.BlockCount == 0 || ad.BlockCount > 20000000000UL) continue;

                        var info = new DiskDriveInfo
                        {
                            Index = list.Count,
                            Device = ad,
                            Name = $"Disk {list.Count} (SATA AHCI: {ad.Model})",
                            SectorCount = ad.BlockCount
                        };

                        ProbeDriveFilesystems(ad, list.Count, info);
                        list.Add(info);
                    }
                }
            }
            catch { }

            // If AHCI controller found physical SATA drive(s), return them.
            // Do NOT probe the legacy IDE channels, as in AHCI mode they are floating bus phantoms!
            if (list.Count > 0)
            {
                return list;
            }

            // 2. Fallback: Probe Cosmos BlockDevice.Devices (IDE / VM / Compatibility mode)
            try
            {
                var devices = BlockDevice.Devices;
                if (devices != null)
                {
                    for (int i = 0; i < devices.Count; i++)
                    {
                        var dev = devices[i];
                        if (dev == null) continue;

                        // Only select raw parent drives, not individual partitions or AHCI devices
                        if (dev is Partition || dev is AhciBlockDevice) continue;

                        // Verify that reading sector 0 is responsive on this hardware channel
                        byte[] testSec = new byte[512];
                        try
                        {
                            dev.ReadBlock(0, 1, ref testSec);
                        }
                        catch
                        {
                            continue; // Unresponsive floating channel or empty optical drive
                        }

                        // Check for floating bus noise (all 0xFF)
                        bool allFF = true;
                        for (int b = 0; b < 16; b++)
                        {
                            if (testSec[b] != 0xFF) { allFF = false; break; }
                        }
                        if (allFF) continue; // Disconnected port returning floating bus 0xFF

                        // Probe ATA Hardware Port for True Model and 48-bit Capacity
                        ushort ioBase = (i == 0) ? (ushort)0x1F0 : (ushort)0x170;
                        byte drvSel = 0xA0; // Master
                        var ataInfo = AtaDeviceDiagnostics.ProbeAta(ioBase, drvSel);
                        if (!ataInfo.Exists && i == 1)
                        {
                            ataInfo = AtaDeviceDiagnostics.ProbeAta(0x1F0, 0xB0); // Primary Slave
                        }

                        // Filter out ATAPI Optical Drives (CD/DVD Drives) - they cannot store FAT32 user files
                        if (ataInfo.Exists && ataInfo.IsAtapi)
                        {
                            continue;
                        }

                        ulong safeCount = 0;
                        string driveName = "";

                        if (ataInfo.Exists && ataInfo.TotalSectors > 0)
                        {
                            safeCount = ataInfo.TotalSectors;
                            driveName = $"Disk {list.Count} ({ataInfo.Model} : {ataInfo.SizeDisplay})";
                        }

                        // Fallback 1: Inspect MBR partition table for partition bounds
                        if (safeCount == 0 && testSec[510] == 0x55 && testSec[511] == 0xAA)
                        {
                            ulong maxEnd = 0;
                            for (int p = 0; p < 4; p++)
                            {
                                int off = 446 + (p * 16);
                                uint sLba = (uint)(testSec[off + 8] | (testSec[off + 9] << 8) | (testSec[off + 10] << 16) | (testSec[off + 11] << 24));
                                uint cSec = (uint)(testSec[off + 12] | (testSec[off + 13] << 8) | (testSec[off + 14] << 16) | (testSec[off + 15] << 24));
                                if (sLba > 0 && cSec > 0 && (sLba + cSec) > maxEnd)
                                {
                                    maxEnd = sLba + cSec;
                                }
                            }
                            if (maxEnd > 0) safeCount = maxEnd;
                        }

                        // Fallback 2: Sanitize HAL BlockCount if reasonable (< 2 TB)
                        if (safeCount == 0)
                        {
                            if (dev.BlockCount > 0 && dev.BlockCount <= 4000000000UL)
                            {
                                safeCount = dev.BlockCount;
                            }
                            else
                            {
                                ulong masked28 = dev.BlockCount & 0x0FFFFFFF;
                                if (masked28 > 2048)
                                {
                                    safeCount = masked28;
                                }
                                else
                                {
                                    safeCount = 2097152UL; // 1 GB fallback
                                }
                            }
                        }

                        if (string.IsNullOrEmpty(driveName))
                        {
                            ulong mb = (safeCount * 512) / (1024UL * 1024);
                            string szDisplay = "";
                            if (mb >= 1024)
                            {
                                ulong gb = mb / 1024;
                                ulong dec = ((mb % 1024) * 10) / 1024;
                                szDisplay = dec > 0 ? $"{gb}.{dec} GB" : $"{gb} GB";
                            }
                            else
                            {
                                szDisplay = $"{mb} MB";
                            }
                            driveName = $"Disk {list.Count} (Hardware Dev #{i} : {szDisplay})";
                        }

                        var info = new DiskDriveInfo
                        {
                            Index = list.Count,
                            Device = dev,
                            Name = driveName,
                            SectorCount = safeCount
                        };

                        ProbeDriveFilesystems(dev, list.Count, info);
                        list.Add(info);
                    }
                }
            }
            catch { }

            // Sort physical drives so that writable storage drives come first, and live USB boot medium comes last
            if (list.Count > 1)
            {
                for (int i = 0; i < list.Count - 1; i++)
                {
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        bool aUsb = (list[i].SectorCount > 0 && list[i].SectorCount < 10000000UL);
                        bool bUsb = (list[j].SectorCount > 0 && list[j].SectorCount < 10000000UL);
                        if (aUsb && !bUsb)
                        {
                            var temp = list[i];
                            list[i] = list[j];
                            list[j] = temp;
                        }
                    }
                }
                for (int idx = 0; idx < list.Count; idx++)
                {
                    list[idx].Index = idx;
                }
            }

            // 3. Fallback: If raw drives were not found, probe Cosmos VFS Disks & Partitions directly
            if (list.Count == 0)
            {
                try
                {
                    var vfsDisks = VFSManager.GetDisks();
                    if (vfsDisks != null && vfsDisks.Count > 0)
                    {
                        for (int d = 0; d < vfsDisks.Count; d++)
                        {
                            var vdisk = vfsDisks[d];
                            if (vdisk == null || vdisk.Partitions == null) continue;

                            for (int p = 0; p < vdisk.Partitions.Count; p++)
                            {
                                var vpart = vdisk.Partitions[p];
                                if (vpart != null && vpart.Host != null && vpart.Host.BlockCount > 0)
                                {
                                    ulong pCount = vpart.Host.BlockCount;
                                    if (pCount > 4000000000UL) pCount = 2097152UL;
                                    ulong gb = (pCount * 512) / (1024UL * 1024 * 1024);
                                    string szDisplay = gb >= 1 ? $"{gb}GB" : $"{(pCount * 512) / (1024 * 1024)}MB";

                                    var info = new DiskDriveInfo
                                    {
                                        Index = list.Count,
                                        Device = vpart.Host,
                                        Name = $"Disk {d} Part {p + 1} ({szDisplay})",
                                        SectorCount = pCount
                                    };
                                    list.Add(info);
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            return list;
        }

        private static void ProbeDriveFilesystems(BlockDevice dev, int diskIdx, DiskDriveInfo info)
        {
            try
            {
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
            }
            catch { }
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
                if (totalBlocks == 0 || totalBlocks > 4000000000UL)
                {
                    byte[] curMbr = new byte[512];
                    ulong mbrSize = 0;
                    try
                    {
                        dev.ReadBlock(0, 1, ref curMbr);
                        if (curMbr[510] == 0x55 && curMbr[511] == 0xAA)
                        {
                            for (int p = 0; p < 4; p++)
                            {
                                int off = 446 + (p * 16);
                                uint sLba = (uint)(curMbr[off + 8] | (curMbr[off + 9] << 8) | (curMbr[off + 10] << 16) | (curMbr[off + 11] << 24));
                                uint cSec = (uint)(curMbr[off + 12] | (curMbr[off + 13] << 8) | (curMbr[off + 14] << 16) | (curMbr[off + 15] << 24));
                                if (sLba > 0 && cSec > 0 && (sLba + cSec) > mbrSize)
                                {
                                    mbrSize = sLba + cSec;
                                }
                            }
                        }
                    }
                    catch { }

                    if (mbrSize > 2048 && mbrSize < 4000000000UL)
                    {
                        totalBlocks = mbrSize;
                    }
                    else
                    {
                        totalBlocks = 2097152UL; // Safe 1GB standard
                    }
                }

                bool isPartition = dev is Partition;
                ulong startLba = isPartition ? 0UL : 2048UL;
                ulong partSectors = totalBlocks > startLba ? totalBlocks - startLba : totalBlocks;
                if (partSectors > 0xFFFFFFFF) partSectors = 0xFFFFFFFF;
                uint sectors32 = (uint)partSectors;

                // 1. Write MBR at Sector 0 with 1 clean active FAT32 partition (only on raw parent disk)
                if (!isPartition)
                {
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
                }

                // 2. Compute FAT32 Parameters
                ushort reservedSectors = 32;
                byte sectorsPerCluster = 8; // 4KB cluster default
                if (sectors32 > 67108864) sectorsPerCluster = 64; // 32KB clusters for >32GB
                else if (sectors32 > 33554432) sectorsPerCluster = 32; // 16KB clusters for >16GB
                else if (sectors32 > 16777216) sectorsPerCluster = 16; // 8KB clusters for >8GB
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

        public static bool FindFat32Volume(BlockDevice dev, out Fat32VolumeInfo vol)
        {
            vol = null;
            if (dev == null) return false;

            // 1. Check partition table in MBR (Sector 0)
            try
            {
                byte[] mbr = new byte[512];
                dev.ReadBlock(0, 1, ref mbr);
                if (mbr[510] == 0x55 && mbr[511] == 0xAA)
                {
                    for (int p = 0; p < 4; p++)
                    {
                        int off = 446 + (p * 16);
                        uint startLba = (uint)(mbr[off + 8] | (mbr[off + 9] << 8) | (mbr[off + 10] << 16) | (mbr[off + 11] << 24));
                        if (startLba > 0 && startLba < dev.BlockCount)
                        {
                            if (CheckFat32Bpb(dev, startLba, out vol))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch { }

            // 2. Check standard 1MB alignment LBA 2048
            if (CheckFat32Bpb(dev, 2048, out vol))
            {
                return true;
            }

            // 3. Check Superfloppy / LBA 0
            if (CheckFat32Bpb(dev, 0, out vol))
            {
                return true;
            }

            return false;
        }

        private static bool CheckFat32Bpb(BlockDevice dev, ulong lba, out Fat32VolumeInfo vol)
        {
            vol = null;
            try
            {
                byte[] bpb = new byte[512];
                dev.ReadBlock(lba, 1, ref bpb);
                if (bpb[510] != 0x55 || bpb[511] != 0xAA) return false;

                ushort bytesPerSec = (ushort)(bpb[11] | (bpb[12] << 8));
                byte secPerClus = bpb[13];
                ushort resSec = (ushort)(bpb[14] | (bpb[15] << 8));
                byte numFats = bpb[16];
                uint fatSec = (uint)(bpb[36] | (bpb[37] << 8) | (bpb[38] << 16) | (bpb[39] << 24));
                uint rootClus = (uint)(bpb[44] | (bpb[45] << 8) | (bpb[46] << 16) | (bpb[47] << 24));

                // Sanity checks for FAT32 BPB
                if (bytesPerSec != 512) return false;
                if (secPerClus == 0 || (secPerClus & (secPerClus - 1)) != 0) return false;
                if (resSec == 0 || numFats == 0 || numFats > 2) return false;
                if (fatSec == 0 || rootClus < 2) return false;

                vol = new Fat32VolumeInfo
                {
                    PartStartLba = lba,
                    BytesPerSector = bytesPerSec,
                    SectorsPerCluster = secPerClus,
                    ReservedSectors = resSec,
                    NumFats = numFats,
                    FatSectors = fatSec,
                    RootDirCluster = rootClus
                };
                return true;
            }
            catch
            {
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
                Fat32VolumeInfo vol;
                if (!FindFat32Volume(dev, out vol))
                {
                    string fmtStatus;
                    if (!FormatDriveAsFat32(dev, out fmtStatus))
                    {
                        status = $"Auto-format failed: {fmtStatus}";
                        return false;
                    }
                    if (!FindFat32Volume(dev, out vol))
                    {
                        status = "FAT32 volume could not be initialized.";
                        return false;
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

                string target83 = name8 + ext3;
                byte[] nameBytes = Encoding.ASCII.GetBytes(target83);

                // Read Root Directory sector
                ulong rootDirLba = vol.GetClusterLba(vol.RootDirCluster);
                byte[] rootDirSec = new byte[512];
                try { dev.ReadBlock(rootDirLba, 1, ref rootDirSec); } catch { }

                // Find matching entry or free entry (entries 1..15, entry 0 reserved for Volume Label)
                int entryOffset = -1;
                int freeOffset = -1;
                uint existingCluster = 0;

                for (int e = 1; e < 16; e++)
                {
                    int off = e * 32;
                    byte b0 = rootDirSec[off];

                    if (b0 == 0x00 || b0 == 0xE5)
                    {
                        if (freeOffset == -1) freeOffset = off;
                        if (b0 == 0x00) break;
                        continue;
                    }

                    if (rootDirSec[off + 11] == 0x0F || (rootDirSec[off + 11] & 0x08) != 0) continue;

                    string curEntry = Encoding.ASCII.GetString(rootDirSec, off, 11);
                    if (string.Equals(curEntry, target83, StringComparison.OrdinalIgnoreCase))
                    {
                        entryOffset = off;
                        ushort cLo = (ushort)(rootDirSec[off + 26] | (rootDirSec[off + 27] << 8));
                        ushort cHi = (ushort)(rootDirSec[off + 20] | (rootDirSec[off + 21] << 8));
                        existingCluster = (uint)(cLo | (cHi << 16));
                        break;
                    }
                }

                if (entryOffset == -1)
                {
                    entryOffset = freeOffset != -1 ? freeOffset : 32;
                }

                int chosenIndex = entryOffset / 32;
                if (chosenIndex < 1) chosenIndex = 1;
                uint targetCluster = existingCluster >= 3 ? existingCluster : (uint)(2 + chosenIndex);

                // Populate Directory Entry
                Array.Copy(nameBytes, 0, rootDirSec, entryOffset, 11);
                rootDirSec[entryOffset + 11] = 0x20; // Archive attribute
                rootDirSec[entryOffset + 20] = (byte)((targetCluster >> 16) & 0xFF); // Cluster High
                rootDirSec[entryOffset + 21] = (byte)((targetCluster >> 24) & 0xFF);
                rootDirSec[entryOffset + 26] = (byte)(targetCluster & 0xFF);         // Cluster Low
                rootDirSec[entryOffset + 27] = (byte)((targetCluster >> 8) & 0xFF);

                byte[] contentBytes = Encoding.ASCII.GetBytes(content ?? "");
                uint fileSize = (uint)contentBytes.Length;
                rootDirSec[entryOffset + 28] = (byte)(fileSize & 0xFF);
                rootDirSec[entryOffset + 29] = (byte)((fileSize >> 8) & 0xFF);
                rootDirSec[entryOffset + 30] = (byte)((fileSize >> 16) & 0xFF);
                rootDirSec[entryOffset + 31] = (byte)((fileSize >> 24) & 0xFF);

                // Write directory sector
                dev.WriteBlock(rootDirLba, 1, ref rootDirSec);

                // Update FAT tables for targetCluster: 0x0FFFFFFF (EOF)
                ulong fatSecOffset = (ulong)(targetCluster * 4) / 512;
                uint fatEntryByteOffset = (uint)((targetCluster * 4) % 512);

                byte[] fatSec = new byte[512];
                try { dev.ReadBlock(vol.Fat1Lba + fatSecOffset, 1, ref fatSec); } catch { }
                fatSec[fatEntryByteOffset] = 0xFF;
                fatSec[fatEntryByteOffset + 1] = 0xFF;
                fatSec[fatEntryByteOffset + 2] = 0xFF;
                fatSec[fatEntryByteOffset + 3] = 0x0F;

                dev.WriteBlock(vol.Fat1Lba + fatSecOffset, 1, ref fatSec);
                if (vol.NumFats > 1)
                {
                    dev.WriteBlock(vol.Fat2Lba + fatSecOffset, 1, ref fatSec);
                }

                // Write file content into target cluster
                ulong clusterLba = vol.GetClusterLba(targetCluster);
                byte[] dataSec = new byte[512];
                Array.Copy(contentBytes, 0, dataSec, 0, Math.Min(contentBytes.Length, 512));
                dev.WriteBlock(clusterLba, 1, ref dataSec);

                // Flush hardware controller write cache
                AtaDeviceDiagnostics.FlushCache(0x1F0, 0xA0);

                // Read-back verification
                byte[] verifySec = new byte[512];
                dev.ReadBlock(rootDirLba, 1, ref verifySec);
                if (verifySec[entryOffset] != nameBytes[0])
                {
                    status = $"[ERR] Hardware disk write mismatch: Read 0x{verifySec[entryOffset]:X2}, expected 0x{nameBytes[0]:X2}.";
                    return false;
                }

                status = $"Saved to FAT32 Disk: {name8.Trim()}.{ext3.Trim()} ({fileSize} bytes) [OK]";
                return true;
            }
            catch (Exception ex)
            {
                status = $"Direct FAT32 write error: {ex.Message}";
                return false;
            }
        }

        public static bool ReadFileDirectFromFat32(BlockDevice dev, string filename, out string content, out string status)
        {
            content = "";
            if (dev == null)
            {
                status = "BlockDevice is null.";
                return false;
            }

            try
            {
                Fat32VolumeInfo vol;
                if (!FindFat32Volume(dev, out vol))
                {
                    status = "No FAT32 volume found on device.";
                    return false;
                }

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

                string target83 = name8 + ext3;

                ulong rootDirLba = vol.GetClusterLba(vol.RootDirCluster);
                byte[] rootDirSec = new byte[512];
                try { dev.ReadBlock(rootDirLba, 1, ref rootDirSec); } catch { }

                for (int e = 0; e < 16; e++)
                {
                    int off = e * 32;
                    if (rootDirSec[off] == 0x00) break;
                    if (rootDirSec[off] == 0xE5) continue;
                    if (rootDirSec[off + 11] == 0x0F || (rootDirSec[off + 11] & 0x08) != 0) continue;

                    string entryName = Encoding.ASCII.GetString(rootDirSec, off, 11);
                    if (string.Equals(entryName, target83, StringComparison.OrdinalIgnoreCase))
                    {
                        ushort clusLow = (ushort)(rootDirSec[off + 26] | (rootDirSec[off + 27] << 8));
                        ushort clusHigh = (ushort)(rootDirSec[off + 20] | (rootDirSec[off + 21] << 8));
                        uint cluster = (uint)(clusLow | (clusHigh << 16));
                        uint fileSize = (uint)(rootDirSec[off + 28] | (rootDirSec[off + 29] << 8) | (rootDirSec[off + 30] << 16) | (rootDirSec[off + 31] << 24));

                        if (cluster >= 2 && fileSize > 0)
                        {
                            ulong clusterLba = vol.GetClusterLba(cluster);
                            byte[] dataSec = new byte[512];
                            try { dev.ReadBlock(clusterLba, 1, ref dataSec); } catch { }

                            int len = Math.Min((int)fileSize, 512);
                            content = Encoding.ASCII.GetString(dataSec, 0, len);
                            status = $"Loaded from FAT32 Disk ({len} bytes) [OK]";
                            return true;
                        }
                        else
                        {
                            content = "";
                            status = "Empty file [OK]";
                            return true;
                        }
                    }
                }

                status = $"File '{filename}' not found on FAT32 disk.";
                return false;
            }
            catch (Exception ex)
            {
                status = $"FAT32 read error: {ex.Message}";
                return false;
            }
        }

        public static bool DeleteFileDirectFromFat32(BlockDevice dev, string filename, out string status)
        {
            if (dev == null)
            {
                status = "BlockDevice is null.";
                return false;
            }

            try
            {
                Fat32VolumeInfo vol;
                if (!FindFat32Volume(dev, out vol))
                {
                    status = "No FAT32 volume found on device.";
                    return false;
                }

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

                string target83 = name8 + ext3;

                ulong rootDirLba = vol.GetClusterLba(vol.RootDirCluster);
                byte[] rootDirSec = new byte[512];
                try { dev.ReadBlock(rootDirLba, 1, ref rootDirSec); } catch { }

                for (int e = 0; e < 16; e++)
                {
                    int off = e * 32;
                    if (rootDirSec[off] == 0x00) break;
                    if (rootDirSec[off] == 0xE5) continue;
                    if (rootDirSec[off + 11] == 0x0F || (rootDirSec[off + 11] & 0x08) != 0) continue;

                    string entryName = Encoding.ASCII.GetString(rootDirSec, off, 11);
                    if (string.Equals(entryName, target83, StringComparison.OrdinalIgnoreCase))
                    {
                        ushort clusLow = (ushort)(rootDirSec[off + 26] | (rootDirSec[off + 27] << 8));
                        ushort clusHigh = (ushort)(rootDirSec[off + 20] | (rootDirSec[off + 21] << 8));
                        uint cluster = (uint)(clusLow | (clusHigh << 16));

                        rootDirSec[off] = 0xE5;
                        dev.WriteBlock(rootDirLba, 1, ref rootDirSec);

                        if (cluster >= 2)
                        {
                            ulong fatSecOffset = (ulong)(cluster * 4) / 512;
                            uint fatEntryByteOffset = (uint)((cluster * 4) % 512);

                            byte[] fatSec = new byte[512];
                            try { dev.ReadBlock(vol.Fat1Lba + fatSecOffset, 1, ref fatSec); } catch { }
                            fatSec[fatEntryByteOffset] = 0;
                            fatSec[fatEntryByteOffset + 1] = 0;
                            fatSec[fatEntryByteOffset + 2] = 0;
                            fatSec[fatEntryByteOffset + 3] = 0;
                            dev.WriteBlock(vol.Fat1Lba + fatSecOffset, 1, ref fatSec);
                            if (vol.NumFats > 1) dev.WriteBlock(vol.Fat2Lba + fatSecOffset, 1, ref fatSec);
                        }

                        status = $"File '{filename}' deleted from FAT32 disk [OK]";
                        return true;
                    }
                }

                status = $"File '{filename}' not found on FAT32 disk.";
                return false;
            }
            catch (Exception ex)
            {
                status = $"FAT32 delete error: {ex.Message}";
                return false;
            }
        }

        public static List<Fat32FileItem> ListFilesDetailedDirectFromFat32(BlockDevice dev)
        {
            var list = new List<Fat32FileItem>();
            if (dev == null) return list;

            Fat32VolumeInfo vol;
            if (!FindFat32Volume(dev, out vol))
            {
                return list;
            }

            try
            {
                ulong rootDirLba = vol.GetClusterLba(vol.RootDirCluster);
                byte[] rootDirSec = new byte[512];
                dev.ReadBlock(rootDirLba, 1, ref rootDirSec);

                for (int e = 0; e < 16; e++)
                {
                    int off = e * 32;
                    byte firstByte = rootDirSec[off];
                    if (firstByte == 0x00) break; // End of directory marker
                    if (firstByte == 0xE5) continue; // Deleted entry

                    byte attr = rootDirSec[off + 11];
                    if (attr == 0x0F) continue; // Long File Name entry
                    if ((attr & 0x08) != 0) continue; // Volume label

                    // Validate printable ASCII in filename
                    bool validAscii = true;
                    for (int i = 0; i < 11; i++)
                    {
                        byte b = rootDirSec[off + i];
                        if (b < 32 || b > 126 || b == 0xFF)
                        {
                            validAscii = false;
                            break;
                        }
                    }
                    if (!validAscii) continue;

                    string raw8 = Encoding.ASCII.GetString(rootDirSec, off, 8).Trim();
                    string raw3 = Encoding.ASCII.GetString(rootDirSec, off + 8, 3).Trim();
                    if (string.IsNullOrEmpty(raw8)) continue;

                    string fname = string.IsNullOrEmpty(raw3) ? raw8 : $"{raw8}.{raw3}";
                    uint sz = (uint)(rootDirSec[off + 28] | (rootDirSec[off + 29] << 8) | (rootDirSec[off + 30] << 16) | (rootDirSec[off + 31] << 24));

                    list.Add(new Fat32FileItem
                    {
                        Name = fname,
                        Size = sz
                    });
                }
            }
            catch { }

            return list;
        }

        public static List<string> ListFilesDirectFromFat32(BlockDevice dev)
        {
            var list = new List<string>();
            var detailed = ListFilesDetailedDirectFromFat32(dev);
            for (int i = 0; i < detailed.Count; i++)
            {
                list.Add(detailed[i].Name);
            }
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
