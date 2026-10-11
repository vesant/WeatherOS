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
        public bool IsWritable { get; set; }
        public bool IsLinuxSystem { get; set; }
        public bool IsWindowsSystem { get; set; }
        public string Description { get; set; } = "";

        public string SizeDisplay
        {
            get
            {
                if (SizeBytes >= 1024UL * 1024 * 1024)
                {
                    ulong gb = SizeBytes / (1024UL * 1024 * 1024);
                    return $"{gb} GB";
                }
                if (SizeBytes >= 1024UL * 1024)
                {
                    ulong mb = SizeBytes / (1024UL * 1024);
                    return $"{mb} MB";
                }
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

            try
            {
                int partIdx = 0;
                int diskIdx = 0;

                // 1. Scan low-level hardware BlockDevices from Cosmos HAL
                var devices = BlockDevice.Devices;
                if (devices != null && devices.Count > 0)
                {
                    for (int i = 0; i < devices.Count; i++)
                    {
                        var dev = devices[i];
                        if (dev == null) continue;

                        if (dev is Partition part)
                        {
                            partIdx++;
                            var info = ProbePartitionDevice(part, diskIdx, partIdx);
                            results.Add(info);
                        }
                        else
                        {
                            diskIdx++;
                            // Probe parent raw drive directly if it has MBR/GPT
                            ProbeRawDrive(dev, diskIdx, results);
                        }
                    }
                }

                // 2. Cross-reference with logical drives mounted in Cosmos VFS
                var logicalDrives = VFSManager.GetLogicalDrives();
                if (logicalDrives != null)
                {
                    foreach (var drive in logicalDrives)
                    {
                        string fs = "";
                        try { fs = VFSManager.GetFileSystemType(drive); } catch { }
                        long total = 0;
                        try { total = VFSManager.GetTotalSize(drive); } catch { }

                        // Check if already mapped
                        var existing = results.Find(p => p.MountedDriveLetter == drive);
                        if (existing == null)
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

            _cachedPartitions = results;
            return results;
        }

        private static PartitionInfo ProbePartitionDevice(Partition part, int diskIdx, int partIdx)
        {
            var info = new PartitionInfo
            {
                DiskIndex = diskIdx,
                PartitionIndex = partIdx,
                DeviceName = $"Partition {partIdx}",
                SectorCount = part.BlockCount,
                FileSystemType = "Raw / Unformatted"
            };

            byte[] sec0 = new byte[512];
            byte[] sec2 = new byte[512];

            try { part.ReadBlock(0, 1, ref sec0); } catch { }
            try { part.ReadBlock(2, 1, ref sec2); } catch { }

            InspectSectors(sec0, sec2, info);
            return info;
        }

        private static void ProbeRawDrive(BlockDevice drive, int diskIdx, List<PartitionInfo> results)
        {
            try
            {
                byte[] mbr = new byte[512];
                drive.ReadBlock(0, 1, ref mbr);

                // Verify MBR signature
                if (mbr[510] == 0x55 && mbr[511] == 0xAA)
                {
                    // Check 4 MBR partition table entries (offset 0x1BE = 446)
                    for (int p = 0; p < 4; p++)
                    {
                        int entryOffset = 446 + p * 16;
                        byte partType = mbr[entryOffset + 4];
                        if (partType == 0) continue;

                        uint lbaStart = (uint)(mbr[entryOffset + 8] | (mbr[entryOffset + 9] << 8) | (mbr[entryOffset + 10] << 16) | (mbr[entryOffset + 11] << 24));
                        uint lbaCount = (uint)(mbr[entryOffset + 12] | (mbr[entryOffset + 13] << 8) | (mbr[entryOffset + 14] << 16) | (mbr[entryOffset + 15] << 24));

                        if (lbaStart > 0 && lbaCount > 0)
                        {
                            // Avoid duplicate if HAL already created Partition
                            if (results.Exists(r => r.StartingSector == lbaStart)) continue;

                            var info = new PartitionInfo
                            {
                                DiskIndex = diskIdx,
                                PartitionIndex = results.Count + 1,
                                DeviceName = $"Disk {diskIdx} Part {p + 1}",
                                StartingSector = lbaStart,
                                SectorCount = lbaCount
                            };

                            byte[] sec0 = new byte[512];
                            byte[] sec2 = new byte[512];
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
            if (sec2 != null && sec2.Length >= 58)
            {
                if (sec2[56] == 0x53 && sec2[57] == 0xEF)
                {
                    info.IsLinuxSystem = true;
                    info.IsWritable = true; // Ready for OS install / format

                    // Check for ext4 features (offset 96 is s_feature_incompat; 0x40 = Extents)
                    bool hasExtents = sec2.Length > 96 && (sec2[96] & 0x40) != 0;
                    info.FileSystemType = hasExtents ? "ext4 (Linux Native)" : "ext3/ext2 (Linux)";

                    // Volume label at offset 120 (0x78) up to 16 bytes
                    try
                    {
                        string label = Encoding.ASCII.GetString(sec2, 120, 16).Trim('\0', ' ');
                        if (!string.IsNullOrEmpty(label)) info.VolumeLabel = label;
                    }
                    catch { }

                    // UUID at offset 104 (16 bytes)
                    try
                    {
                        string u = "";
                        for (int k = 104; k < 120 && k < sec2.Length; k++) u += sec2[k].ToString("x2");
                        info.Uuid = u;
                    }
                    catch { }

                    info.Description = "Linux Root / Home (Ready to Overwrite/Install)";
                    return;
                }
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
                        info.FileSystemType = "FAT32 (EFI / Boot)";
                        info.IsWritable = true;
                        
                        try
                        {
                            string vol = Encoding.ASCII.GetString(sec0, 71, 11).Trim('\0', ' ');
                            if (!string.IsNullOrEmpty(vol)) info.VolumeLabel = vol;
                        }
                        catch { }

                        info.Description = "Linux EFI System Partition / FAT32 [Full Read/Write]";
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
            }
        }
    }
}
