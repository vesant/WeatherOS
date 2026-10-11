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
                long mbFree = FreeSpace / (1024 * 1024);
                long mbTotal = TotalSize / (1024 * 1024);
                sizeStr = $" | {mbFree}MB free / {mbTotal}MB";
            }
            string rw = IsLinuxSystem ? " [Linux Target]" : (IsReadOnly ? " [Read-Only]" : " [Writable]");
            return $"{Name} (FS: {FileSystem}{sizeStr}){rw}";
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
            if (string.IsNullOrWhiteSpace(drive))
            {
                status = "Invalid drive specified.";
                return false;
            }

            try
            {
                var disks = VFSManager.GetDisks();
                if (disks != null && disks.Count > 0)
                {
                    int partIdx = 0;
                    if (drive.Length > 0 && char.IsDigit(drive[0]))
                    {
                        partIdx = drive[0] - '0';
                    }

                    disks[0].FormatPartition(partIdx, "Fat32", true);
                    status = $"[OK] Formatted volume {drive} as FAT32 successfully!";
                    return true;
                }
                status = $"[ERR] No physical disks registered in VFS.";
                return false;
            }
            catch (Exception ex)
            {
                status = $"[ERR] Format failed on {drive}: {ex.Message}";
                return false;
            }
        }

        public static List<StorageLocation> GetStorageLocations()
        {
            var list = new List<StorageLocation>();

            // 1. RAM Virtual Store (High-Speed Session Storage)
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

            // 2. Probed Partitions and Disks (Hardware Probe)
            try
            {
                var probed = FileSystemProbeService.ProbeAllPartitions();
                if (probed != null && probed.Count > 0)
                {
                    foreach (var p in probed)
                    {
                        string locId = !string.IsNullOrEmpty(p.MountedDriveLetter) ? p.MountedDriveLetter : $"disk_part_{p.PartitionIndex}";
                        long total = (long)p.SizeBytes;
                        long free = 0;

                        if (!string.IsNullOrEmpty(p.MountedDriveLetter))
                        {
                            try { free = VFSManager.GetAvailableFreeSpace(p.MountedDriveLetter); } catch { }
                        }

                        list.Add(new StorageLocation
                        {
                            Id = locId,
                            Name = !string.IsNullOrEmpty(p.MountedDriveLetter) ? $"Drive {p.MountedDriveLetter}" : p.DeviceName,
                            FileSystem = p.FileSystemType,
                            TotalSize = total,
                            FreeSpace = free,
                            IsReadOnly = !p.IsWritable,
                            IsLinuxSystem = p.IsLinuxSystem
                        });
                    }
                }
                else
                {
                    // Fallback to logical drives if probe returned empty
                    var drives = VFSManager.GetLogicalDrives();
                    if (drives != null)
                    {
                        foreach (var drive in drives)
                        {
                            string fsType = "FAT32";
                            try { fsType = VFSManager.GetFileSystemType(drive) ?? "FAT32"; } catch { }
                            bool ro = fsType.ToUpper().Contains("ISO");
                            list.Add(new StorageLocation
                            {
                                Id = drive,
                                Name = $"Drive {drive}",
                                FileSystem = fsType,
                                IsReadOnly = ro,
                                IsLinuxSystem = false
                            });
                        }
                    }
                }
            }
            catch { }

            return list;
        }

        public static bool SaveFileToLocation(string filename, string content, string locationId, out string status)
        {
            filename = NormalizeFilename(filename);
            if (content == null) content = "";

            // Always update RAM store for instant persistence and session availability
            _memoryStore[filename] = content;

            if (string.Equals(locationId, "ram", StringComparison.OrdinalIgnoreCase))
            {
                status = $"Saved to RAM Storage: {filename} ({content.Length} chars) [OK]";
                return true;
            }

            // Check if target is a raw unmounted partition (e.g. disk_part_*)
            var locations = GetStorageLocations();
            var targetLoc = locations.Find(l => string.Equals(l.Id, locationId, StringComparison.OrdinalIgnoreCase));
            if (targetLoc != null && !targetLoc.Id.Contains(":"))
            {
                status = $"[INFO] {targetLoc.Name} ({targetLoc.FileSystem}) needs formatting before file storage. Saved in RAM!";
                return true;
            }

            // Target physical drive (e.g. 0:\, 1:\)
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
                    status = $"Saved to {path} & RAM ({content.Length} chars) [OK]";
                    return true;
                }
                catch
                {
                    File.WriteAllText(path, content);
                    status = $"Saved to {path} & RAM ({content.Length} chars) [OK]";
                    return true;
                }
            }
            catch (Exception ex)
            {
                status = $"[WARN] Write to {locationId} failed ({ex.Message}). Saved in RAM!";
                return true;
            }
        }

        public static bool SaveFile(string filename, string content, out string status)
        {
            filename = NormalizeFilename(filename);
            if (content == null) content = "";

            _memoryStore[filename] = content;

            bool diskSuccess = false;
            string targetPath = "";

            try
            {
                var drives = VFSManager.GetLogicalDrives();
                if (drives != null && drives.Count > 0)
                {
                    byte[] bytes = System.Text.Encoding.ASCII.GetBytes(content);
                    foreach (var drive in drives)
                    {
                        try
                        {
                            string path = drive;
                            if (!path.EndsWith("\\")) path += "\\";
                            path += filename;

                            if (File.Exists(path)) File.Delete(path);

                            using (var fs = File.Create(path))
                            {
                                fs.Write(bytes, 0, bytes.Length);
                            }

                            diskSuccess = true;
                            targetPath = path;
                            break;
                        }
                        catch
                        {
                            try
                            {
                                string path = drive;
                                if (!path.EndsWith("\\")) path += "\\";
                                path += filename;

                                File.WriteAllText(path, content);
                                diskSuccess = true;
                                targetPath = path;
                                break;
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }

            int len = content.Length;
            if (diskSuccess)
            {
                status = $"Saved to {targetPath} & RAM ({len} chars) [OK]";
                return true;
            }
            else
            {
                status = $"Saved to RAM Storage: {filename} ({len} chars) [OK]";
                return true;
            }
        }

        public static bool LoadFile(string filename, out string content, out string status)
        {
            filename = NormalizeFilename(filename);
            content = "";

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
                files.Add($"{key} ({_memoryStore[key].Length} bytes) [RAM]");
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
    }
}
