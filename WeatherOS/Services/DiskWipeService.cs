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
        public byte[] Sector0Cache { get; set; } = null;
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
        public string VolumeLabel = "";
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
        public static string FormatSectors(ulong sectors)
        {
            if (sectors == 0) return "0 MB";
            if (sectors > 4000000000UL) sectors = 2097152UL;
            ulong mb = (sectors * 512UL) / (1024UL * 1024UL);
            if (mb >= 1024)
            {
                ulong gb = mb / 1024;
                ulong dec = ((mb % 1024) * 10) / 1024;
                return dec > 0 ? $"{gb}.{dec} GB" : $"{gb} GB";
            }
            return $"{mb} MB";
        }

        public static bool IsLiveBootDrive(BlockDevice dev, ulong sectorCount, string model, byte[] sec0)
        {
            if (dev is Cosmos.HAL.BlockDevice.Ata || dev is AhciBlockDevice || dev is CustomPciAtaBlockDevice)
            {
                // Physical ATA / SATA drives are internal hardware fixed disks!
                return false;
            }

            if (!string.IsNullOrEmpty(model))
            {
                if (model.Contains("USB") || model.Contains("Live") || model.Contains("Boot Medium") || model.Contains("Flash"))
                    return true;
            }

            return false;
        }

        private static bool IsDuplicateOfExisting(List<DiskDriveInfo> existingList, BlockDevice newDev, byte[] sec0)
        {
            if (existingList == null || existingList.Count == 0 || newDev == null) return false;

            for (int i = 0; i < existingList.Count; i++)
            {
                var ex = existingList[i];
                if (ex.Device == newDev) return true;

                // Match Custom PCI ATA device duplicates
                if (ex.Device is CustomPciAtaBlockDevice cpEx && newDev is CustomPciAtaBlockDevice cpNew)
                {
                    if (cpEx.CmdBase == cpNew.CmdBase && cpEx.DriveSelect == cpNew.DriveSelect) return true;
                }

                // Match between Cosmos ATA driver and Custom PCI ATA driver on the same port
                if (ex.Device is Cosmos.HAL.BlockDevice.Ata ataEx && newDev is CustomPciAtaBlockDevice cpNew2)
                {
                    ushort exBase = (ataEx.ControllerID == Cosmos.HAL.BlockDevice.Ata.ControllerIdEnum.Primary) ? (ushort)0x1F0 : (ushort)0x170;
                    byte exSel = (ataEx.BusPosition == Cosmos.HAL.BlockDevice.Ata.BusPositionEnum.Master) ? (byte)0xA0 : (byte)0xB0;
                    if (exBase == cpNew2.CmdBase && exSel == cpNew2.DriveSelect) return true;
                }
                if (ex.Device is CustomPciAtaBlockDevice cpEx2 && newDev is Cosmos.HAL.BlockDevice.Ata ataNew)
                {
                    ushort newBase = (ataNew.ControllerID == Cosmos.HAL.BlockDevice.Ata.ControllerIdEnum.Primary) ? (ushort)0x1F0 : (ushort)0x170;
                    byte newSel = (ataNew.BusPosition == Cosmos.HAL.BlockDevice.Ata.BusPositionEnum.Master) ? (byte)0xA0 : (byte)0xB0;
                    if (cpEx2.CmdBase == newBase && cpEx2.DriveSelect == newSel) return true;
                }
            }
            return false;
        }

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

                        byte[] testSec = new byte[512];
                        try { ad.ReadBlock(0, 1, ref testSec); } catch { continue; }
                        if (IsDuplicateOfExisting(list, ad, testSec)) continue;

                        var info = new DiskDriveInfo
                        {
                            Index = list.Count,
                            Device = ad,
                            Name = $"Disk {list.Count} (SATA AHCI: {ad.Model})",
                            SectorCount = ad.BlockCount,
                            Sector0Cache = testSec
                        };

                        ProbeDriveFilesystems(ad, list.Count, info);
                        list.Add(info);
                    }
                }
            }
            catch { }

            // 1.5. Probe Bare-Metal PCI ATA/IDE driver (Standard & Native PCI ports on Intel ICH7-M)
            try
            {
                var pciAtaDrives = CustomPciAtaDriverService.GetDevices();
                if (pciAtaDrives != null && pciAtaDrives.Count > 0)
                {
                    for (int p = 0; p < pciAtaDrives.Count; p++)
                    {
                        var pd = pciAtaDrives[p];
                        if (pd == null || pd.BlockCount == 0) continue;

                        byte[] testSec = new byte[512];
                        try { pd.ReadBlock(0, 1, ref testSec); } catch { continue; }
                        if (IsDuplicateOfExisting(list, pd, testSec)) continue;

                        bool isUsb = IsLiveBootDrive(pd, pd.BlockCount, pd.Model, testSec);
                        if (isUsb && list.Exists(x => x.Name.Contains("Live USB") || x.Name.Contains("Boot Medium") || x.Name.Contains("Boot Drive")))
                        {
                            continue; // Don't duplicate Live USB
                        }

                        string cleanModel = CustomPciAtaBlockDevice.SanitizeAtaModel(pd.Model);
                        if (string.IsNullOrEmpty(cleanModel)) cleanModel = isUsb ? "Live USB Boot Drive" : "Internal Fixed Storage";

                        var info = new DiskDriveInfo
                        {
                            Index = list.Count,
                            Device = pd,
                            Name = isUsb ? $"Disk {list.Count} (Live USB Boot Drive, {FormatSectors(pd.BlockCount)})" : $"Disk {list.Count} ({cleanModel}, {FormatSectors(pd.BlockCount)})",
                            SectorCount = pd.BlockCount,
                            Sector0Cache = testSec
                        };

                        ProbeDriveFilesystems(pd, list.Count, info);
                        if (info.ContainedFilesystems != null && info.ContainedFilesystems.Contains("WEATHEROS"))
                        {
                            info.Name = $"Disk {list.Count} (Internal Hard Disk [WEATHEROS], {FormatSectors(pd.BlockCount)})";
                        }
                        list.Add(info);
                    }
                }
            }
            catch { }

            // 2. Always probe Cosmos BlockDevice.Devices to discover IDE, SATA and VM virtual disks
            try
            {
                var devices = BlockDevice.Devices;
                if (devices != null)
                {
                    for (int i = 0; i < devices.Count; i++)
                    {
                        var dev = devices[i];
                        if (dev == null) continue;

                        // Only select raw parent drives, not individual partitions, AHCI, or Custom PCI ATA devices already checked
                        if (dev is Partition || dev is AhciBlockDevice || dev is CustomPciAtaBlockDevice) continue;

                        if (list.Exists(x => x.Device == dev)) continue;

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

                        if (IsDuplicateOfExisting(list, dev, testSec)) continue;

                        // Probe ATA Hardware Port for True Model and 48-bit Capacity
                        ushort ioBase = 0x1F0;
                        byte drvSel = 0xA0; // Master
                        if (dev is Cosmos.HAL.BlockDevice.Ata ataDev)
                        {
                            ioBase = (ataDev.ControllerID == Cosmos.HAL.BlockDevice.Ata.ControllerIdEnum.Primary) ? (ushort)0x1F0 : (ushort)0x170;
                            drvSel = (ataDev.BusPosition == Cosmos.HAL.BlockDevice.Ata.BusPositionEnum.Master) ? (byte)0xA0 : (byte)0xB0;
                        }
                        else
                        {
                            ioBase = (i == 0) ? (ushort)0x1F0 : (ushort)0x170;
                            drvSel = (i == 1) ? (byte)0xB0 : (byte)0xA0;
                        }

                        var ataInfo = (dev is Cosmos.HAL.BlockDevice.Ata) 
                            ? AtaDeviceDiagnostics.ProbeAta(ioBase, drvSel) 
                            : new AtaIdentifyInfo();

                        // Filter out ATAPI Optical Drives (CD/DVD Drives) - they cannot store FAT32 user files
                        if (ataInfo.Exists && ataInfo.IsAtapi)
                        {
                            continue;
                        }

                        ulong safeCount = 0;
                        if (ataInfo.Exists && ataInfo.TotalSectors > 0)
                        {
                            safeCount = ataInfo.TotalSectors;
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

                        bool isUsbDev = IsLiveBootDrive(dev, safeCount, ataInfo.Model, testSec);
                        if (isUsbDev && list.Exists(x => x.Name.Contains("Live USB") || x.Name.Contains("Boot Medium") || x.Name.Contains("Boot Drive")))
                        {
                            continue; // Don't duplicate Live USB
                        }

                        string cleanModel = "";
                        if (dev is Cosmos.HAL.BlockDevice.ATA_PIO pioDev && !string.IsNullOrWhiteSpace(pioDev.ModelNo))
                        {
                            cleanModel = CustomPciAtaBlockDevice.SanitizeAtaModel(pioDev.ModelNo);
                        }
                        if (string.IsNullOrEmpty(cleanModel) && ataInfo.Exists && !string.IsNullOrWhiteSpace(ataInfo.Model))
                        {
                            cleanModel = CustomPciAtaBlockDevice.SanitizeAtaModel(ataInfo.Model);
                        }
                        if (string.IsNullOrEmpty(cleanModel))
                        {
                            cleanModel = isUsbDev ? "Live USB Boot Drive" : $"Internal Hard Disk #{i}";
                        }

                        string driveName = isUsbDev ? $"Disk {list.Count} (Live USB Boot Drive, {FormatSectors(safeCount)})" : $"Disk {list.Count} ({cleanModel}, {FormatSectors(safeCount)})";

                        var info = new DiskDriveInfo
                        {
                            Index = list.Count,
                            Device = dev,
                            Name = driveName,
                            SectorCount = safeCount,
                            Sector0Cache = testSec
                        };

                        ProbeDriveFilesystems(dev, list.Count, info);
                        if (info.ContainedFilesystems != null && info.ContainedFilesystems.Contains("WEATHEROS"))
                        {
                            info.Name = $"Disk {list.Count} (Internal Hard Disk [WEATHEROS], {FormatSectors(safeCount)})";
                        }
                        list.Add(info);
                    }
                }
            }
            catch { }

            // 3. Fallback: Probe Cosmos VFS Disks directly (resolving physical parent drive)
            try
            {
                var vfsDisks = VFSManager.GetDisks();
                if (vfsDisks != null && vfsDisks.Count > 0)
                {
                    for (int d = 0; d < vfsDisks.Count; d++)
                    {
                        var vdisk = vfsDisks[d];
                        if (vdisk == null || vdisk.Partitions == null || vdisk.Partitions.Count == 0) continue;

                        BlockDevice rootDev = null;
                        ulong totalPartSectors = 0;
                        for (int p = 0; p < vdisk.Partitions.Count; p++)
                        {
                            var vpart = vdisk.Partitions[p];
                            if (vpart != null && vpart.Host != null)
                            {
                                BlockDevice h = vpart.Host;
                                while (h is Cosmos.HAL.BlockDevice.Partition pHost)
                                {
                                    h = pHost.Host;
                                }
                                if (rootDev == null && h != null) rootDev = h;
                                if (vpart.Host.BlockCount > 0) totalPartSectors += vpart.Host.BlockCount;
                            }
                        }

                        if (rootDev == null) continue;
                        if (list.Exists(x => x.Device == rootDev)) continue;

                        byte[] testSec = new byte[512];
                        try { rootDev.ReadBlock(0, 1, ref testSec); } catch { continue; }
                        if (IsDuplicateOfExisting(list, rootDev, testSec)) continue;

                        ulong safeDiskSectors = rootDev.BlockCount;
                        if (safeDiskSectors == 0 || safeDiskSectors > 4000000000UL || safeDiskSectors < totalPartSectors)
                        {
                            safeDiskSectors = (totalPartSectors > 0) ? totalPartSectors : 2097152UL;
                        }

                        bool isUsbVfs = IsLiveBootDrive(rootDev, safeDiskSectors, "", testSec);
                        if (isUsbVfs && list.Exists(x => x.Name.Contains("Live USB") || x.Name.Contains("Boot Medium") || x.Name.Contains("Boot Drive")))
                        {
                            continue; // Don't duplicate Live USB
                        }

                        var info = new DiskDriveInfo
                        {
                            Index = list.Count,
                            Device = rootDev,
                            Name = isUsbVfs ? $"Disk {list.Count} (Live USB Boot Drive, {FormatSectors(safeDiskSectors)})" : $"Disk {list.Count} (Internal Hard Disk, {FormatSectors(safeDiskSectors)})",
                            SectorCount = safeDiskSectors,
                            Sector0Cache = testSec
                        };

                        ProbeDriveFilesystems(rootDev, list.Count, info);
                        list.Add(info);
                    }
                }
            }
            catch { }

            // Sort physical drives so that internal hard disks come first, and live USB boot medium comes last
            if (list.Count > 1)
            {
                for (int i = 0; i < list.Count - 1; i++)
                {
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        bool aUsb = list[i].Name.Contains("Live USB") || list[i].Name.Contains("Boot Medium") || list[i].Name.Contains("Boot Drive");
                        bool bUsb = list[j].Name.Contains("Live USB") || list[j].Name.Contains("Boot Medium") || list[j].Name.Contains("Boot Drive");
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
                    bool isUsb = list[idx].Name.Contains("Live USB") || list[idx].Name.Contains("Boot Medium") || list[idx].Name.Contains("Boot Drive");
                    if (isUsb)
                    {
                        list[idx].Name = $"Disk {idx} (Live USB Boot Drive, {list[idx].SizeDisplay})";
                    }
                    else
                    {
                        int pOpen = list[idx].Name.IndexOf('(');
                        if (pOpen > 0)
                        {
                            list[idx].Name = $"Disk {idx} " + list[idx].Name.Substring(pOpen);
                        }
                        else
                        {
                            list[idx].Name = $"Disk {idx} (Internal Hard Disk, {list[idx].SizeDisplay})";
                        }
                    }
                }
            }

            return list;
        }

        private static void ProbeDriveFilesystems(BlockDevice dev, int diskIdx, DiskDriveInfo info)
        {
            try
            {
                Fat32VolumeInfo fatVol;
                if (FindFat32Volume(dev, out fatVol))
                {
                    string label = !string.IsNullOrEmpty(fatVol.VolumeLabel) ? fatVol.VolumeLabel : "FAT32 Storage";
                    if (!info.ContainedFilesystems.Contains("FAT32"))
                    {
                        info.ContainedFilesystems.Add($"FAT32 ({label})");
                    }
                    if (!string.IsNullOrEmpty(fatVol.VolumeLabel) && !info.Name.Contains(fatVol.VolumeLabel))
                    {
                        if (!info.Name.Contains("Live USB") && !info.Name.Contains("Boot Drive"))
                        {
                            string cleanLabel = CustomPciAtaBlockDevice.SanitizeAtaModel(fatVol.VolumeLabel);
                            if (!string.IsNullOrEmpty(cleanLabel))
                            {
                                info.Name = $"Disk {diskIdx} ({cleanLabel}, {info.SizeDisplay})";
                            }
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

                // Resolve parent physical BlockDevice if dev is a Partition slice
                BlockDevice rootDev = dev;
                while (rootDev is Partition partObj)
                {
                    rootDev = partObj.Host;
                }

                ulong startLba = 2048UL;
                ulong partSectors = totalBlocks > startLba ? totalBlocks - startLba : totalBlocks;
                if (partSectors > 0xFFFFFFFF) partSectors = 0xFFFFFFFF;
                uint sectors32 = (uint)partSectors;

                // 1. Write executable 16-bit real mode MBR bootloader at Sector 0 with active FAT32 partition
                {
                    byte[] mbr = new byte[512];

                    // Standard 16-bit Real Mode x86 MBR Bootloader (relocates to 0x0600, locates active partition 0x80, builds DAP at 0x05F0 and boots VBR via INT 13h Extensions)
                    byte[] mbrBootCode = new byte[]
                    {
                        0x31, 0xC0, 0x8E, 0xD0, 0xBC, 0x00, 0x7C, 0x8E, 0xD8, 0x8E, 0xC0, 0xBE, 0x00, 0x7C, 0xBF, 0x00,
                        0x06, 0xB9, 0x00, 0x01, 0xFC, 0xF3, 0xA5, 0xEA, 0x1C, 0x06, 0x00, 0x00, 0xBE, 0xBE, 0x07, 0xB1,
                        0x04, 0x80, 0x3C, 0x80, 0x74, 0x0B, 0x83, 0xC6, 0x10, 0xFE, 0xC9, 0x75, 0xF4, 0xCD, 0x18, 0xEB,
                        0xFE, 0xC7, 0x06, 0xF0, 0x05, 0x10, 0x00, 0xC7, 0x06, 0xF2, 0x05, 0x01, 0x00, 0xC7, 0x06, 0xF4,
                        0x05, 0x00, 0x7C, 0xC7, 0x06, 0xF6, 0x05, 0x00, 0x00, 0x8B, 0x44, 0x08, 0xA3, 0xF8, 0x05, 0x8B,
                        0x44, 0x0A, 0xA3, 0xFA, 0x05, 0x31, 0xC0, 0xA3, 0xFC, 0x05, 0xA3, 0xFE, 0x05, 0xBE, 0xF0, 0x05,
                        0xB4, 0x42, 0xB2, 0x80, 0xCD, 0x13, 0x72, 0x05, 0xEA, 0x00, 0x7C, 0x00, 0x00, 0xCD, 0x18, 0xEB,
                        0xFE
                    };
                    Array.Copy(mbrBootCode, 0, mbr, 0, mbrBootCode.Length);

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
                    rootDev.WriteBlock(0, 1, ref mbr);

                    // Read-back verification: ensure MBR partition table actually wrote to hardware
                    byte[] checkMbr = new byte[512];
                    try { rootDev.ReadBlock(0, 1, ref checkMbr); } catch { }
                    if (checkMbr[510] != 0x55 || checkMbr[511] != 0xAA)
                    {
                        status = "[ERR] Hardware write failed: MBR partition table was not accepted by target disk.";
                        return false;
                    }
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

                // Read-back verification: ensure FAT32 BPB actually wrote to hardware
                byte[] checkBpb = new byte[512];
                try { dev.ReadBlock(startLba, 1, ref checkBpb); } catch { }
                if (checkBpb[510] != 0x55 || checkBpb[511] != 0xAA || checkBpb[0] != 0xEB)
                {
                    status = "[ERR] Hardware write failed: FAT32 boot sector was not accepted by target disk.";
                    return false;
                }

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

                string vLabel = "";
                try
                {
                    vLabel = Encoding.ASCII.GetString(bpb, 71, 11).Trim();
                    if (vLabel == "NO NAME") vLabel = "";
                }
                catch { }

                if (string.IsNullOrEmpty(vLabel))
                {
                    try
                    {
                        ulong rLba = lba + resSec + ((ulong)numFats * fatSec) + ((ulong)(rootClus - 2) * secPerClus);
                        byte[] rSec = new byte[512];
                        dev.ReadBlock(rLba, 1, ref rSec);
                        for (int e = 0; e < 16; e++)
                        {
                            int off = e * 32;
                            if (rSec[off] == 0x00) break;
                            if (rSec[off] == 0xE5) continue;
                            byte attr = rSec[off + 11];
                            if (attr == 0x08)
                            {
                                string lab = Encoding.ASCII.GetString(rSec, off, 11).Trim();
                                if (!string.IsNullOrEmpty(lab) && lab != "NO NAME")
                                {
                                    vLabel = lab;
                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                }

                vol = new Fat32VolumeInfo
                {
                    VolumeLabel = vLabel,
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
            byte[] bytes = Encoding.ASCII.GetBytes(content ?? "");
            return WriteBinaryFileDirectToFat32(dev, filename, bytes, out status);
        }

        public static bool WriteBinaryFileDirectToFat32(BlockDevice dev, string filename, byte[] contentBytes, out string status)
        {
            if (dev == null)
            {
                status = "BlockDevice is null.";
                return false;
            }
            if (contentBytes == null) contentBytes = new byte[0];

            try
            {
                Fat32VolumeInfo vol;
                if (!FindFat32Volume(dev, out vol))
                {
                    status = "No FAT32 partition found on disk.";
                    return false;
                }

                // Format 8.3 filename
                string rawName = filename.ToUpper();
                string name8 = "UNTITLED";
                string ext3 = "BIN";
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
                int secCount = (contentBytes.Length + 511) / 512;
                if (secCount > (int)vol.SectorsPerCluster) secCount = (int)vol.SectorsPerCluster;
                if (secCount == 0) secCount = 1;

                for (int s = 0; s < secCount; s++)
                {
                    byte[] dataSec = new byte[512];
                    int byteOffset = s * 512;
                    int toCopy = Math.Min(512, contentBytes.Length - byteOffset);
                    if (toCopy > 0)
                    {
                        Array.Copy(contentBytes, byteOffset, dataSec, 0, toCopy);
                    }
                    dev.WriteBlock(clusterLba + (ulong)s, 1, ref dataSec);
                }

                // Read-back verification with retries to allow write to settle
                bool verified = false;
                for (int retry = 0; retry < 5; retry++)
                {
                    byte[] verifySec = new byte[512];
                    try { dev.ReadBlock(rootDirLba, 1, ref verifySec); } catch { }
                    if (verifySec[entryOffset] == nameBytes[0])
                    {
                        verified = true;
                        break;
                    }
                }

                if (!verified)
                {
                    status = $"[ERR] File verification failed: {name8.Trim()}.{ext3.Trim()} could not be verified on disk.";
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
