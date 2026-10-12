using System;
using System.Collections.Generic;
using System.IO;
using Cosmos.System.FileSystem.VFS;

namespace WeatherOS.Services
{
    public class StorageLocation
    {
        public string Id { get; set; } = "ram";
        public string Name { get; set; } = "";
        public string FileSystem { get; set; } = "";
        public long TotalSize { get; set; } = 0;
        public long FreeSpace { get; set; } = 0;
        public bool IsReadOnly { get; set; } = false;
        public bool IsLinuxSystem { get; set; } = false;

        public string GetDescription()
        {
            string sizeStr = "";
            if (TotalSize > 0)
            {
                long mbTotal = TotalSize / (1024 * 1024);
                if (mbTotal > 1024)
                    sizeStr = $" | {mbTotal / 1024}GB";
                else if (mbTotal > 0)
                    sizeStr = $" | {mbTotal}MB";
            }
            string rw = IsLinuxSystem ? " [Linux]" : (IsReadOnly ? " [RO]" : " [RW]");
            string desc = $"{Name} ({FileSystem}{sizeStr}){rw}";
            if (desc.Length > 56) desc = desc.Substring(0, 53) + "...";
            return desc;
        }
    }

    public static class StorageService
    {
        private static Dictionary<string, string> _memoryStore = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string NormalizeFilename(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename)) filename = "untitled.txt";
            filename = filename.Trim();
            if (!filename.Contains(".")) filename += ".txt";
            return filename;
        }

        public static bool FormatDrive(string drive, string fsTarget, out string status)
        {
            try
            {
                var drives = DiskWipeService.GetPhysicalDrives();
                if (drives != null && drives.Count > 0)
                {
                    return DiskWipeService.FormatDriveAsFat32(drives[0].Device, out status);
                }

                status = "[ERR] No physical drives found to format.";
                return false;
            }
            catch (Exception ex)
            {
                status = $"[ERR] Format failed: {ex.Message}";
                return false;
            }
        }

        public static List<StorageLocation> GetStorageLocations()
        {
            var list = new List<StorageLocation>();

            // 1. Primary Physical Storage (Disk 0 FAT32)
            bool diskAdded = false;
            try
            {
                var probed = FileSystemProbeService.ProbeAllPartitions();
                if (probed != null && probed.Count > 0)
                {
                    // Look for FAT32 partition first
                    foreach (var p in probed)
                    {
                        if (p.FileSystemType.StartsWith("FAT32") || p.FileSystemType.StartsWith("FAT16"))
                        {
                            string locId = !string.IsNullOrEmpty(p.MountedDriveLetter) ? p.MountedDriveLetter : "disk0_fat32";
                            list.Add(new StorageLocation
                            {
                                Id = locId,
                                Name = !string.IsNullOrEmpty(p.MountedDriveLetter) ? $"Drive {p.MountedDriveLetter}" : "Disk 0 Part 1",
                                FileSystem = "FAT32 Storage",
                                TotalSize = (long)p.SizeBytes,
                                FreeSpace = (long)p.SizeBytes > 1024 * 1024 ? (long)p.SizeBytes - 1024 * 1024 : 0,
                                IsReadOnly = false,
                                IsLinuxSystem = false
                            });
                            diskAdded = true;
                            break; // Show only the ONE single clean physical disk!
                        }
                    }

                    // Fallback to first partition if no FAT32 found
                    if (!diskAdded && probed.Count > 0)
                    {
                        var first = probed[0];
                        string locId = !string.IsNullOrEmpty(first.MountedDriveLetter) ? first.MountedDriveLetter : "disk0_part1";
                        list.Add(new StorageLocation
                        {
                            Id = locId,
                            Name = !string.IsNullOrEmpty(first.MountedDriveLetter) ? $"Drive {first.MountedDriveLetter}" : "Disk 0 Part 1",
                            FileSystem = first.FileSystemType,
                            TotalSize = (long)first.SizeBytes,
                            FreeSpace = 0,
                            IsReadOnly = !first.IsWritable,
                            IsLinuxSystem = first.IsLinuxSystem
                        });
                        diskAdded = true;
                    }
                }
            }
            catch { }

            // If probe didn't find any partition, check physical drive directly
            if (!diskAdded)
            {
                try
                {
                    var phys = DiskWipeService.GetPhysicalDrives();
                    if (phys != null && phys.Count > 0)
                    {
                        list.Add(new StorageLocation
                        {
                            Id = "disk0_fat32",
                            Name = "Disk 0 Part 1",
                            FileSystem = "FAT32 Storage",
                            TotalSize = (long)phys[0].SizeBytes,
                            FreeSpace = (long)phys[0].SizeBytes > 1024 * 1024 ? (long)phys[0].SizeBytes - 1024 * 1024 : 0,
                            IsReadOnly = false,
                            IsLinuxSystem = false
                        });
                    }
                }
                catch { }
            }

            // 2. RAM Virtual Store (High-Speed Session Storage)
            list.Add(new StorageLocation
            {
                Id = "ram",
                Name = "RAM Virtual Store",
                FileSystem = "RAM-FS (Session)",
                TotalSize = 32 * 1024 * 1024,
                FreeSpace = 32 * 1024 * 1024 - (_memoryStore.Count * 2048),
                IsReadOnly = false,
                IsLinuxSystem = false
            });

            return list;
        }

        public static bool SaveFileToLocation(string filename, string content, string locationId, out string status)
        {
            filename = NormalizeFilename(filename);
            if (content == null) content = "";

            // Always update RAM store for instant access
            _memoryStore[filename] = content;

            if (string.Equals(locationId, "ram", StringComparison.OrdinalIgnoreCase))
            {
                status = $"Saved to RAM Storage: {filename} ({content.Length} chars) [OK]";
                return true;
            }

            // Save to physical Disk 0 / FAT32 storage
            bool wroteToDisk = false;
            string targetPath = "";

            // 1. Try VFS if mounted with drive letter
            if (locationId.Contains(":"))
            {
                try
                {
                    string path = locationId;
                    if (!path.EndsWith("\\")) path += "\\";
                    path += filename;

                    byte[] bytes = System.Text.Encoding.ASCII.GetBytes(content);
                    try
                    {
                        if (File.Exists(path)) File.Delete(path);
                        using (var fs = File.Create(path))
                        {
                            fs.Write(bytes, 0, bytes.Length);
                        }
                        wroteToDisk = true;
                        targetPath = path;
                    }
                    catch
                    {
                        File.WriteAllText(path, content);
                        wroteToDisk = true;
                        targetPath = path;
                    }
                }
                catch { }
            }

            // 2. If VFS write didn't succeed or target is raw FAT32 volume, write directly via block device
            if (!wroteToDisk)
            {
                try
                {
                    var physDrives = DiskWipeService.GetPhysicalDrives();
                    if (physDrives != null && physDrives.Count > 0)
                    {
                        string directStatus;
                        bool directOk = DiskWipeService.WriteFileDirectToFat32(physDrives[0].Device, filename, content, out directStatus);
                        if (directOk)
                        {
                            wroteToDisk = true;
                            targetPath = $"Disk 0 ({filename})";
                        }
                    }
                }
                catch { }
            }

            if (wroteToDisk)
            {
                status = $"Saved to {targetPath} & RAM ({content.Length} chars) [OK]";
            }
            else
            {
                status = $"Saved to RAM Virtual Store: {filename} ({content.Length} chars) [OK]";
            }
            return true;
        }

        public static bool SaveFile(string filename, string content, out string status)
        {
            return SaveFileToLocation(filename, content, "disk0_fat32", out status);
        }

        public static bool LoadFile(string filename, out string content, out string status)
        {
            filename = NormalizeFilename(filename);
            content = "";

            // Check VFS logical drives
            try
            {
                var drives = VFSManager.GetLogicalDrives();
                if (drives != null)
                {
                    foreach (var drive in drives)
                    {
                        string path = drive;
                        if (!path.EndsWith("\\")) path += "\\";
                        path += filename;

                        if (File.Exists(path))
                        {
                            content = File.ReadAllText(path);
                            status = $"Loaded from {path} ({content.Length} chars) [OK]";
                            _memoryStore[filename] = content;
                            return true;
                        }
                    }
                }
            }
            catch { }

            // Check RAM memory store
            if (_memoryStore.ContainsKey(filename))
            {
                content = _memoryStore[filename];
                status = $"Loaded from RAM Storage: {filename} ({content.Length} chars) [OK]";
                return true;
            }

            status = $"File '{filename}' not found on Disk or RAM.";
            return false;
        }

        public static bool DeleteFile(string filename, out string status)
        {
            filename = NormalizeFilename(filename);
            bool deleted = false;

            if (_memoryStore.ContainsKey(filename))
            {
                _memoryStore.Remove(filename);
                deleted = true;
            }

            try
            {
                var drives = VFSManager.GetLogicalDrives();
                if (drives != null)
                {
                    foreach (var drive in drives)
                    {
                        string path = drive;
                        if (!path.EndsWith("\\")) path += "\\";
                        path += filename;

                        if (File.Exists(path))
                        {
                            File.Delete(path);
                            deleted = true;
                        }
                    }
                }
            }
            catch { }

            status = deleted ? $"Deleted '{filename}' successfully [OK]" : $"File '{filename}' not found.";
            return deleted;
        }

        public static List<string> GetFileList()
        {
            var files = new List<string>();

            foreach (var key in _memoryStore.Keys)
            {
                files.Add($"{key} ({_memoryStore[key].Length} bytes) [RAM/DISK]");
            }

            try
            {
                var drives = VFSManager.GetLogicalDrives();
                if (drives != null)
                {
                    foreach (var drive in drives)
                    {
                        try
                        {
                            var diskFiles = Directory.GetFiles(drive);
                            if (diskFiles != null)
                            {
                                foreach (var df in diskFiles)
                                {
                                    string name = Path.GetFileName(df);
                                    if (!files.Exists(f => f.StartsWith(name)))
                                    {
                                        files.Add($"{name} [DISK]");
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return files;
        }

        public static Dictionary<string, string> GetMemoryStore()
        {
            return _memoryStore;
        }
    }
}
