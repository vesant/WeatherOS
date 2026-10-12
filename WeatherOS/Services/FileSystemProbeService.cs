using System;
using System.Collections.Generic;
using System.Text;
using Cosmos.HAL.BlockDevice;
using Cosmos.System.FileSystem.VFS;

namespace WeatherOS.Services
{
    public class PartitionInfo
    {
        public int DiskIndex { get; set; }
        public int PartitionIndex { get; set; }
        public string DeviceName { get; set; } = "";
        public ulong StartingSector { get; set; }
        public ulong SectorCount { get; set; }
        public ulong SizeBytes => SectorCount * 512;
        public string FileSystemType { get; set; } = "Unknown";
        public string VolumeLabel { get; set; } = "";
        public string Uuid { get; set; } = "";
        public bool IsMounted { get; set; }
        public string MountedDriveLetter { get; set; } = "";
        public bool IsWritable { get; set; } = true;
        public bool IsLinuxSystem { get; set; }
        public bool IsWindowsSystem { get; set; }
        public string Description { get; set; } = "";

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
    }

    public static class FileSystemProbeService
    {
        private static List<PartitionInfo> _cachedPartitions = new List<PartitionInfo>();

        public static List<PartitionInfo> ProbeAllPartitions()
        {
            var results = new List<PartitionInfo>();

            // 1. PRIMARY PROBE: Cosmos VFS Disks and their Partitions
            try
            {
                var vfsDisks = VFSManager.GetDisks();
                if (vfsDisks != null && vfsDisks.Count > 0)
                {
                    for (int d = 0; d < vfsDisks.Count; d++)
                    {
                        var disk = vfsDisks[d];
                        if (disk == null || disk.Partitions == null) continue;

                        for (int p = 0; p < disk.Partitions.Count; p++)
                        {
                            var part = disk.Partitions[p];
                            if (part == null) continue;

                            var host = part.Host;
                            ulong bCount = host != null ? host.BlockCount : 0;
                            ulong bSize = host != null ? host.BlockSize : 512;
                            if (bSize < 512) bSize = 512;

                            var info = new PartitionInfo
                            {
                                DiskIndex = d,
                                PartitionIndex = p + 1,
                                DeviceName = $"Disk {d} Part {p + 1}",
                                StartingSector = host != null ? host.StartingSector : 0,
                                SectorCount = bCount,
                                FileSystemType = (part.MountedFS != null && !string.IsNullOrEmpty(part.MountedFS.Type)) ? part.MountedFS.Type : "FAT32 Storage",
                                VolumeLabel = (part.MountedFS != null && !string.IsNullOrEmpty(part.MountedFS.Label)) ? part.MountedFS.Label : "",
                                IsMounted = part.MountedFS != null,
                                MountedDriveLetter = (part.MountedFS != null && !string.IsNullOrEmpty(part.MountedFS.RootPath)) ? part.MountedFS.RootPath : (part.RootPath ?? ""),
                                IsWritable = true,
                                IsLinuxSystem = false,
                                Description = "Physical Storage Partition"
                            };

                            byte[] sec0 = new byte[bSize];
                            byte[] sec2 = new byte[bSize];

                            if (host != null)
                            {
                                try { host.ReadBlock(0, 1, ref sec0); } catch { }
                                try { host.ReadBlock(2, 1, ref sec2); } catch { }
                            }

                            InspectSectors(sec0, sec2, info);
                            results.Add(info);
                        }
                    }
                }
            }
            catch { }

            // 2. SECONDARY PROBE: Raw BlockDevices from Cosmos HAL if VFS returned 0 partitions
            if (results.Count == 0)
            {
                try
                {
                    var devices = BlockDevice.Devices;
                    if (devices != null && devices.Count > 0)
                    {
                        int diskIdx = 0;
                        for (int i = 0; i < devices.Count; i++)
                        {
                            var dev = devices[i];
                            if (dev == null) continue;

                            if (dev is Partition part)
                            {
                                var info = new PartitionInfo
                                {
                                    DiskIndex = diskIdx,
                                    PartitionIndex = results.Count + 1,
                                    DeviceName = $"Partition {results.Count + 1}",
                                    SectorCount = part.BlockCount,
                                    FileSystemType = "Raw / Unformatted",
                                    IsWritable = true
                                };

                                uint bSize = (uint)part.BlockSize;
                                if (bSize < 512) bSize = 512;
                                byte[] sec0 = new byte[bSize];
                                byte[] sec2 = new byte[bSize];
                                try { part.ReadBlock(0, 1, ref sec0); } catch { }
                                try { part.ReadBlock(2, 1, ref sec2); } catch { }
                                InspectSectors(sec0, sec2, info);
                                results.Add(info);
                            }
                            else
                            {
                                diskIdx++;
                                ProbeRawDrive(dev, diskIdx, results);
                            }
                        }
                    }
                }
                catch { }
            }

            // 3. Cross-reference with logical drives mounted in Cosmos VFS
            try
            {
                var logicalDrives = VFSManager.GetLogicalDrives();
                if (logicalDrives != null)
                {
                    foreach (var drive in logicalDrives)
                    {
                        string fs = "";
                        try { fs = VFSManager.GetFileSystemType(drive); } catch { }
                        long total = 0;
                        try { total = VFSManager.GetTotalSize(drive); } catch { }

                        var existing = results.Find(p => p.MountedDriveLetter == drive);
                        if (existing != null)
                        {
                            existing.IsMounted = true;
                            if (!string.IsNullOrEmpty(fs)) existing.FileSystemType = fs;
                        }
                        else
                        {
                            bool isIso = fs != null && (fs.ToUpper().Contains("ISO") || fs.ToUpper().Contains("CDFS"));
                            results.Add(new PartitionInfo
                            {
                                DiskIndex = 0,
                                PartitionIndex = results.Count + 1,
                                DeviceName = $"VFS Mount {drive}",
                                SectorCount = total > 0 ? (ulong)(total / 512) : 0,
                                FileSystemType = !string.IsNullOrEmpty(fs) ? fs : (isIso ? "ISO9660" : "FAT32"),
                                IsMounted = true,
                                MountedDriveLetter = drive,
                                IsWritable = !isIso,
                                Description = isIso ? "Live Boot / ISO Image" : "Mounted System Drive"
                            });
                        }
                    }
                }
            }
            catch { }

            var deduped = new List<PartitionInfo>();
            foreach (var r in results)
            {
                if (!deduped.Exists(d => (d.StartingSector > 0 && d.StartingSector == r.StartingSector) ||
                                         (!string.IsNullOrEmpty(d.MountedDriveLetter) && !string.IsNullOrEmpty(r.MountedDriveLetter) && d.MountedDriveLetter == r.MountedDriveLetter)))
                {
                    deduped.Add(r);
                }
            }

            _cachedPartitions = deduped;
            return deduped;
        }

        private static void ProbeRawDrive(BlockDevice drive, int diskIdx, List<PartitionInfo> results)
        {
            try
            {
                uint bSize = (uint)drive.BlockSize;
                if (bSize < 512) bSize = 512;
                byte[] mbr = new byte[bSize];
                drive.ReadBlock(0, 1, ref mbr);

                // Verify MBR signature
                if (mbr[510] == 0x55 && mbr[511] == 0xAA)
                {
                    bool isGpt = false;
                    for (int p = 0; p < 4; p++)
                    {
                        int entryOffset = 446 + p * 16;
                        byte partType = mbr[entryOffset + 4];
                        if (partType == 0xEE)
                        {
                            isGpt = true;
                            break;
                        }
                    }

                    if (isGpt)
                    {
                        // Probe GPT partition table at LBA 1 and LBA 2..5
                        byte[] gptHeader = new byte[bSize];
                        try { drive.ReadBlock(1, 1, ref gptHeader); } catch { }

                        // Read partition entries starting at LBA 2
                        byte[] gptEntries = new byte[bSize];
                        try { drive.ReadBlock(2, 1, ref gptEntries); } catch { }

                        for (int e = 0; e < 4; e++)
                        {
                            int off = e * 128;
                            if (off + 48 < gptEntries.Length)
                            {
                                ulong startLba = BitConverter.ToUInt64(gptEntries, off + 32);
                                ulong endLba = BitConverter.ToUInt64(gptEntries, off + 40);

                                if (startLba > 0 && endLba >= startLba)
                                {
                                    ulong count = endLba - startLba + 1;
                                    var gptInfo = new PartitionInfo
                                    {
                                        DiskIndex = diskIdx,
                                        PartitionIndex = results.Count + 1,
                                        DeviceName = $"Disk {diskIdx} Part {e + 1} (GPT)",
                                        StartingSector = startLba,
                                        SectorCount = count,
                                        IsWritable = true,
                                        IsLinuxSystem = false,
                                        FileSystemType = "Storage Partition (GPT)",
                                        Description = "Physical Storage Partition"
                                    };

                                    byte[] s0 = new byte[bSize];
                                    byte[] s2 = new byte[bSize];
                                    try { drive.ReadBlock(startLba, 1, ref s0); } catch { }
                                    try { drive.ReadBlock(startLba + 2, 1, ref s2); } catch { }
                                    InspectSectors(s0, s2, gptInfo);
                                    results.Add(gptInfo);
                                }
                            }
                        }
                        return;
                    }

                    // Check 4 MBR partition table entries
                    for (int p = 0; p < 4; p++)
                    {
                        int entryOffset = 446 + p * 16;
                        byte partType = mbr[entryOffset + 4];
                        if (partType == 0) continue;

                        uint lbaStart = (uint)(mbr[entryOffset + 8] | (mbr[entryOffset + 9] << 8) | (mbr[entryOffset + 10] << 16) | (mbr[entryOffset + 11] << 24));
                        uint lbaCount = (uint)(mbr[entryOffset + 12] | (mbr[entryOffset + 13] << 8) | (mbr[entryOffset + 14] << 16) | (mbr[entryOffset + 15] << 24));

                        if (lbaStart > 0 && lbaCount > 0)
                        {
                            if (results.Exists(r => r.StartingSector == lbaStart)) continue;

                            var info = new PartitionInfo
                            {
                                DiskIndex = diskIdx,
                                PartitionIndex = results.Count + 1,
                                DeviceName = $"Disk {diskIdx} Part {p + 1}",
                                StartingSector = lbaStart,
                                SectorCount = lbaCount,
                                IsWritable = true,
                                IsLinuxSystem = false,
                                FileSystemType = "FAT32 Storage"
                            };

                            byte[] sec0 = new byte[bSize];
                            byte[] sec2 = new byte[bSize];
                            try { drive.ReadBlock(lbaStart, 1, ref sec0); } catch { }
                            try { drive.ReadBlock(lbaStart + 2, 1, ref sec2); } catch { }

                            InspectSectors(sec0, sec2, info);
                            results.Add(info);
                        }
                    }
                }
            }
            catch { }
        }

        private static void InspectSectors(byte[] sec0, byte[] sec2, PartitionInfo info)
        {
            // 1. Probing Linux ext2 / ext3 / ext4 Superblock (Sector 2, magic at offset 56: 0x53, 0xEF)
            // Or Sector 0 offset 1080 for 4K sector drives
            bool isExt = false;
            byte[] super = sec2;
            int magicOff = 56;

            if (sec2 != null && sec2.Length >= 58 && sec2[56] == 0x53 && sec2[57] == 0xEF)
            {
                isExt = true;
                super = sec2;
                magicOff = 56;
            }
            else if (sec0 != null && sec0.Length >= 1082 && sec0[1080] == 0x53 && sec0[1081] == 0xEF)
            {
                isExt = true;
                super = sec0;
                magicOff = 1080;
            }

            if (isExt)
            {
                info.IsLinuxSystem = true;
                info.IsWritable = true;

                bool hasExtents = super.Length > (magicOff + 40) && (super[magicOff + 40] & 0x40) != 0;
                info.FileSystemType = hasExtents ? "ext4 (Linux Native)" : "ext3/ext2 (Linux)";

                try
                {
                    string label = Encoding.ASCII.GetString(super, magicOff + 64, 16).Trim('\0', ' ');
                    if (!string.IsNullOrEmpty(label)) info.VolumeLabel = label;
                }
                catch { }

                try
                {
                    string u = "";
                    for (int k = magicOff + 48; k < magicOff + 64 && k < super.Length; k++) u += super[k].ToString("x2");
                    info.Uuid = u;
                }
                catch { }

                info.Description = "Linux Root / Home (Ready to Overwrite/Install)";
                return;
            }

            // 2. Probing NTFS (Sector 0, bytes 3..7 == "NTFS ")
            if (sec0 != null && sec0.Length >= 8)
            {
                if (sec0[3] == 'N' && sec0[4] == 'T' && sec0[5] == 'F' && sec0[6] == 'S')
                {
                    info.FileSystemType = "NTFS (Windows)";
                    info.IsWindowsSystem = true;
                    info.IsWritable = true;
                    info.Description = "Windows Partition (Ready to Overwrite/Install)";
                    return;
                }

                // 3. Probing exFAT (Sector 0, bytes 3..7 == "EXFAT")
                if (sec0[3] == 'E' && sec0[4] == 'X' && sec0[5] == 'F' && sec0[6] == 'A' && sec0[7] == 'T')
                {
                    info.FileSystemType = "exFAT (Extended FAT)";
                    info.IsWritable = true;
                    info.Description = "Flash / exFAT Storage (Ready to Format)";
                    return;
                }

                // 4. Probing FAT32 / FAT16 / FAT12
                if (sec0.Length >= 512 && sec0[510] == 0x55 && sec0[511] == 0xAA)
                {
                    string fat32Sig = "";
                    try { fat32Sig = Encoding.ASCII.GetString(sec0, 82, 8); } catch { }

                    if (fat32Sig.StartsWith("FAT32"))
                    {
                        info.FileSystemType = "FAT32";
                        info.IsWritable = true;
                        info.IsLinuxSystem = false;
                        
                        try
                        {
                            string vol = Encoding.ASCII.GetString(sec0, 71, 11).Trim('\0', ' ');
                            if (!string.IsNullOrEmpty(vol)) info.VolumeLabel = vol;
                        }
                        catch { }

                        info.Description = "FAT32 Primary Storage [Full Read/Write Ready]";
                        return;
                    }

                    string fat16Sig = "";
                    try { fat16Sig = Encoding.ASCII.GetString(sec0, 54, 8); } catch { }

                    if (fat16Sig.StartsWith("FAT16") || fat16Sig.StartsWith("FAT12"))
                    {
                        info.FileSystemType = "FAT16 (Legacy)";
                        info.IsWritable = true;
                        info.Description = "FAT16 Volume [Full Read/Write]";
                        return;
                    }
                }

                // 5. Probing Linux Swap
                if (sec0.Length >= 4096)
                {
                    string swapSig = "";
                    try { swapSig = Encoding.ASCII.GetString(sec0, 4086, 10); } catch { }
                    if (swapSig == "SWAPSPACE2")
                    {
                        info.FileSystemType = "Linux Swap";
                        info.IsLinuxSystem = true;
                        info.IsWritable = true;
                        info.Description = "Linux Swap Partition (Ready to Overwrite/Install)";
                        return;
                    }
                }
            }

            // Default fallback for detected partitions on the disk
            info.FileSystemType = "FAT32 Storage";
            info.IsLinuxSystem = false;
            info.IsWritable = true;
            info.Description = "Primary Storage Partition";
        }
    }
}
