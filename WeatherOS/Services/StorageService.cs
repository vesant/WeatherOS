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

    public class StoredFile
    {
        public string Name { get; set; } = "";
        public string Content { get; set; } = "";
    }

    public static class StorageService
    {
        private static List<StoredFile> _memoryFiles = new List<StoredFile>();

        public static string NormalizeFilename(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename)) filename = "untitled.txt";
            filename = filename.Trim();
            if (!filename.Contains(".")) filename += ".txt";
            return filename;
        }

        private static void PutMemoryFile(string filename, string content)
        {
            filename = NormalizeFilename(filename);
            for (int i = 0; i < _memoryFiles.Count; i++)
            {
                if (string.Equals(_memoryFiles[i].Name, filename, StringComparison.OrdinalIgnoreCase))
                {
                    _memoryFiles[i].Content = content ?? "";
                    return;
                }
            }
            _memoryFiles.Add(new StoredFile { Name = filename, Content = content ?? "" });
        }

        private static bool TryGetMemoryFile(string filename, out string content)
        {
            filename = NormalizeFilename(filename);
            for (int i = 0; i < _memoryFiles.Count; i++)
            {
                if (string.Equals(_memoryFiles[i].Name, filename, StringComparison.OrdinalIgnoreCase))
                {
                    content = _memoryFiles[i].Content;
                    return true;
                }
            }
            content = "";
            return false;
        }

        private static bool DeleteMemoryFile(string filename)
        {
            filename = NormalizeFilename(filename);
            for (int i = 0; i < _memoryFiles.Count; i++)
            {
                if (string.Equals(_memoryFiles[i].Name, filename, StringComparison.OrdinalIgnoreCase))
                {
                    _memoryFiles.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        public static List<StoredFile> GetMemoryFiles()
        {
            return _memoryFiles;
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

            try
            {
                var physDrives = DiskWipeService.GetPhysicalDrives();
                if (physDrives != null && physDrives.Count > 0)
                {
                    for (int i = 0; i < physDrives.Count; i++)
                    {
                        var d = physDrives[i];
                        list.Add(new StorageLocation
                        {
                            Id = $"disk{d.Index}",
                            Name = $"Disk {d.Index} (Hardware Dev #{i})",
                            FileSystem = "FAT32 Storage",
                            TotalSize = (long)d.SizeBytes,
                            FreeSpace = (long)d.SizeBytes > 1048576 ? (long)d.SizeBytes - 1048576 : 0,
                            IsReadOnly = false,
                            IsLinuxSystem = false
                        });
                    }
                }
            }
            catch { }

            if (list.Count == 0)
            {
                list.Add(new StorageLocation
                {
                    Id = "disk0",
                    Name = "Disk 0 (Primary Storage)",
                    FileSystem = "FAT32 Storage",
                    TotalSize = 1024L * 1024 * 1024,
                    FreeSpace = 1024L * 1024 * 1024 - (1024 * 1024),
                    IsReadOnly = false,
                    IsLinuxSystem = false
                });
            }

            // RAM Virtual Store (High-Speed Session Storage)
            list.Add(new StorageLocation
            {
                Id = "ram",
                Name = "RAM Virtual Store",
                FileSystem = "RAM-FS Session",
                TotalSize = 32 * 1024 * 1024,
                FreeSpace = 32 * 1024 * 1024 - (_memoryFiles.Count * 2048),
                IsReadOnly = false,
                IsLinuxSystem = false
            });

            return list;
        }

        public static bool SaveFileToLocation(string filename, string content, string locationId, out string status)
        {
            filename = NormalizeFilename(filename);
            if (content == null) content = "";

            if (string.Equals(locationId, "ram", StringComparison.OrdinalIgnoreCase))
            {
                PutMemoryFile(filename, content);
                status = $"Saved to RAM Storage: {filename} ({content.Length} chars) [OK]";
                return true;
            }

            // Target is physical disk (e.g. disk0)
            var physDrives = DiskWipeService.GetPhysicalDrives();
            if (physDrives == null || physDrives.Count == 0)
            {
                PutMemoryFile(filename, content);
                status = $"[ERR] No physical drives detected. Saved to RAM Virtual Store.";
                return false;
            }

            int diskIdx = 0;
            if (locationId != null && locationId.StartsWith("disk") && locationId.Length > 4)
            {
                int.TryParse(locationId.Substring(4), out diskIdx);
            }
            if (diskIdx < 0 || diskIdx >= physDrives.Count) diskIdx = 0;

            string directStatus;
            bool directOk = DiskWipeService.WriteFileDirectToFat32(physDrives[diskIdx].Device, filename, content, out directStatus);

            if (directOk)
            {
                // File saved exclusively to disk - do not duplicate in RAM store!
                status = $"Saved to {physDrives[diskIdx].Name}: {filename} ({content.Length} bytes) [OK]";
                return true;
            }
            else
            {
                // Fallback to RAM so user work is never lost
                PutMemoryFile(filename, content);
                status = $"Disk save failed: {directStatus}. File saved in RAM temporary store.";
                return false;
            }
        }

        public static bool SaveFile(string filename, string content, out string status)
        {
            return SaveFileToLocation(filename, content, "disk0", out status);
        }

        public static bool LoadFile(string filename, out string content, out string status)
        {
            filename = NormalizeFilename(filename);
            content = "";

            // 1. Check physical disk first
            try
            {
                var physDrives = DiskWipeService.GetPhysicalDrives();
                if (physDrives != null && physDrives.Count > 0)
                {
                    string readStatus;
                    if (DiskWipeService.ReadFileDirectFromFat32(physDrives[0].Device, filename, out content, out readStatus))
                    {
                        status = $"Loaded from Disk 0: {filename} ({content.Length} bytes) [OK]";
                        return true;
                    }
                }
            }
            catch { }

            // 2. Check RAM store fallback
            if (TryGetMemoryFile(filename, out content))
            {
                status = $"Loaded from RAM Storage: {filename} ({content.Length} chars) [OK]";
                return true;
            }

            status = $"File '{filename}' not found on Disk or RAM.";
            return false;
        }

        public static bool DeleteFile(string filename, out string status)
        {
            filename = NormalizeFilename(filename);
            bool deleted = DeleteMemoryFile(filename);

            try
            {
                var physDrives = DiskWipeService.GetPhysicalDrives();
                if (physDrives != null && physDrives.Count > 0)
                {
                    string delStatus;
                    if (DiskWipeService.DeleteFileDirectFromFat32(physDrives[0].Device, filename, out delStatus))
                    {
                        deleted = true;
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

            for (int i = 0; i < _memoryFiles.Count; i++)
            {
                var f = _memoryFiles[i];
                files.Add($"{f.Name} ({f.Content.Length} bytes) [RAM/DISK]");
            }

            try
            {
                var physDrives = DiskWipeService.GetPhysicalDrives();
                if (physDrives != null && physDrives.Count > 0)
                {
                    var diskFiles = DiskWipeService.ListFilesDirectFromFat32(physDrives[0].Device);
                    if (diskFiles != null)
                    {
                        for (int i = 0; i < diskFiles.Count; i++)
                        {
                            string df = diskFiles[i];
                            bool alreadyInList = false;
                            for (int j = 0; j < files.Count; j++)
                            {
                                if (files[j].StartsWith(df, StringComparison.OrdinalIgnoreCase))
                                {
                                    alreadyInList = true;
                                    break;
                                }
                            }
                            if (!alreadyInList)
                            {
                                files.Add($"{df} [DISK]");
                            }
                        }
                    }
                }
            }
            catch { }

            return files;
        }

        public static List<StoredFile> GetMemoryStore()
        {
            return _memoryFiles;
        }
    }
}
