using System;
using System.Collections.Generic;
using System.IO;
using Cosmos.HAL.BlockDevice;
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
                long cleanSize = TotalSize;
                if (cleanSize > 2199023255552L) cleanSize = 1073741824L; // Sanitize corrupt overflow
                long mbTotal = cleanSize / (1024 * 1024);
                if (mbTotal >= 1024)
                {
                    long gb = mbTotal / 1024;
                    long dec = ((mbTotal % 1024) * 10) / 1024;
                    sizeStr = dec > 0 ? $" | {gb}.{dec}GB" : $" | {gb}GB";
                }
                else if (mbTotal > 0)
                {
                    sizeStr = $" | {mbTotal}MB";
                }
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
        public byte[] RawBytes { get; set; } = null;
    }

    public class StorageFileInfo
    {
        public string Name { get; set; } = "";
        public long Size { get; set; } = 0;
        public string LocationId { get; set; } = "";
        public string LocationName { get; set; } = "";
    }

    public static class StorageService
    {
        private static List<StoredFile> _memoryFiles = new List<StoredFile>();

        public static bool IsVfsAvailable()
        {
            try
            {
                var drives = Cosmos.System.FileSystem.VFS.VFSManager.GetLogicalDrives();
                if (drives != null && drives.Count > 0)
                {
                    for (int i = 0; i < drives.Count; i++)
                    {
                        if (string.Equals(drives[i].TrimEnd('\\'), "0:", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public static string NormalizeFilename(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename)) filename = "untitled.txt";
            filename = filename.Trim();
            if (!filename.Contains(".")) filename += ".txt";
            return filename;
        }

        public static void PutMemoryFile(string filename, string content)
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

        public static bool TryGetMemoryFile(string filename, out string content)
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

        public static void InitDefaultMemoryFiles()
        {
            if (_memoryFiles.Count > 0) return; // Already initialized

            // 1. Welcome Guide in Memory
            string welcome =
                "==============================================================================\n" +
                "               Welcome to WeatherOS (High-Speed RAM Mode)             \n" +
                "==============================================================================\n" +
                "WeatherOS is operating in full Memory Mode (RAM-FS Session Store)!\n\n" +
                "SYSTEM STATUS & MEMORY STORAGE:\n" +
                "- All utilities, apps, and services are loaded directly in memory.\n" +
                "- Notepad: Notes are saved instantly to RAM Virtual Store (no disk lag/errors).\n" +
                "- File Explorer: Browse, view, and manage all memory files under RAM:\\\n" +
                "- Network Transfer: Receive and send files via WeatherTransfer (port 7331).\n" +
                "- Weather & Astronomy: Real-time telemetry, 7-day forecast & lunar observatory.\n" +
                "- System Updates: Stage and test firmware packages (.pkg) seamlessly in RAM.\n\n" +
                "Enjoy full system functionality with maximum speed and zero hardware conflicts!\n" +
                "==============================================================================\n";
            PutMemoryFile("welcome.txt", welcome);

            // 2. Personal Notes in Memory
            string notes =
                "WeatherOS Personal Notes\n" +
                "------------------------\n" +
                "Location: RAM Virtual Store\n" +
                "Status  : Loaded in Memory\n\n" +
                "- Tested 7-Day weather forecast & lunar observatory.\n" +
                "- Tested TLS 1.2 bare-metal HTTPS client.\n" +
                "- Fast in-memory editing with [F2] Save.\n";
            PutMemoryFile("notes.txt", notes);

            // 3. Settings Configuration in Memory
            string settings =
                "DarkMode=false\n" +
                "AutoLoc=false\n" +
                "DefaultCity=Setubal\n" +
                "DebugLog=false\n" +
                "CustomKey=\n";
            PutMemoryFile("settings.ini", settings);

            // 4. System Version Manifest in Memory
            string version =
                "[WeatherOS]\n" +
                "Version=3.1.2-release\n" +
                "BuildDate=2026-10-13\n" +
                "Branch=32bit\n" +
                "LatestCommit=72cd642\n" +
                "CommitTitle=Add installer, OTA update, and live changelog features\n" +
                "Status=Loaded_In_Memory_RAM_Mode\n" +
                "BootTarget=RAM:\\boot\\WeatherOS.bin\n";
            PutMemoryFile("version.ini", version);

            // 5. Apps Manifest in Memory
            string apps =
                "[WeatherOS_Apps]\n" +
                "WeatherDashboard_Version=3.1\n" +
                "WeatherForecast_Version=2.0\n" +
                "LunarForecast_Version=2.0\n" +
                "ClockApp_Version=2.0\n" +
                "ClockApp_Engine=CMOS_RTC_Hardware\n" +
                "NotepadApp_Version=2.5\n" +
                "NetTransferApp_Version=2.0\n" +
                "SystemInfoApp_Version=3.1\n";
            PutMemoryFile("apps.ini", apps);

            // 6. Commits Changelog JSON in Memory
            string commitsJson =
                "[\n" +
                "  {\n" +
                "    \"sha\": \"72cd642\",\n" +
                "    \"commit\": {\n" +
                "      \"author\": { \"name\": \"Vicente\", \"date\": \"2026-10-13T17:50:00Z\" },\n" +
                "      \"message\": \"Add installer, OTA update, and live changelog features\\n\\n- Add interactive hard disk installer app with multi-step UI\\n- Refactor SystemInfoApp: live GitHub changelog, file picker, OTA update\\n- Implement UpdateManagerService for install, update, rollback, CRC32\\n- Add GitHubCommitsService for commit history (TLS 1.2 support)\\n- Integrate pure C# SHA-256 and TLS 1.2 client for secure updates\\n- Enhance BootMenu for disk/live/installer boot options\\n- Register installer in launcher and desktop; improve ESC handling\\n- Enable robust, non-destructive updates and user data protection\"\n" +
                "    }\n" +
                "  }\n" +
                "]\n";
            PutMemoryFile("commits.json", commitsJson);
        }

        public static void PutMemoryBinaryFile(string filename, byte[] data)
        {
            filename = NormalizeFilename(filename);
            for (int i = 0; i < _memoryFiles.Count; i++)
            {
                if (string.Equals(_memoryFiles[i].Name, filename, StringComparison.OrdinalIgnoreCase))
                {
                    _memoryFiles[i].RawBytes = data;
                    _memoryFiles[i].Content = (data != null && data.Length < 65536) ? System.Text.Encoding.ASCII.GetString(data) : "";
                    return;
                }
            }
            _memoryFiles.Add(new StoredFile { Name = filename, RawBytes = data, Content = (data != null && data.Length < 65536) ? System.Text.Encoding.ASCII.GetString(data) : "" });
        }

        public static bool TryGetMemoryBinaryFile(string filename, out byte[] data)
        {
            filename = NormalizeFilename(filename);
            for (int i = 0; i < _memoryFiles.Count; i++)
            {
                if (string.Equals(_memoryFiles[i].Name, filename, StringComparison.OrdinalIgnoreCase))
                {
                    if (_memoryFiles[i].RawBytes != null)
                    {
                        data = _memoryFiles[i].RawBytes;
                    }
                    else
                    {
                        data = System.Text.Encoding.ASCII.GetBytes(_memoryFiles[i].Content ?? "");
                    }
                    return true;
                }
            }
            data = null;
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

        private static BlockDevice GetTargetDisk(string locationId, out string diskName)
        {
            diskName = "Disk 0";
            var physDrives = DiskWipeService.GetPhysicalDrives();
            if (physDrives != null && physDrives.Count > 0)
            {
                int diskIdx = 0;
                if (locationId != null && locationId.StartsWith("disk") && locationId.Length > 4)
                {
                    int.TryParse(locationId.Substring(4), out diskIdx);
                }
                if (diskIdx < 0 || diskIdx >= physDrives.Count) diskIdx = 0;

                diskName = physDrives[diskIdx].Name;
                return physDrives[diskIdx].Device;
            }

            // Fallback to Cosmos BlockDevice.Devices directly
            try
            {
                if (BlockDevice.Devices != null && BlockDevice.Devices.Count > 0)
                {
                    for (int i = 0; i < BlockDevice.Devices.Count; i++)
                    {
                        var d = BlockDevice.Devices[i];
                        if (d != null && !(d is Partition) && d.BlockSize == 512)
                        {
                            diskName = $"Hardware Dev #{i}";
                            return d;
                        }
                    }
                    diskName = "Hardware Dev #0";
                    return BlockDevice.Devices[0];
                }
            }
            catch { }

            return null;
        }

        public static List<StorageLocation> GetStorageLocations()
        {
            var list = new List<StorageLocation>();

            // 1. RAM Virtual Store (High-Speed Session Storage in Memory) - Primary Default
            list.Add(new StorageLocation
            {
                Id = "ram",
                Name = "RAM Virtual Store",
                FileSystem = "RAM-FS Session",
                TotalSize = 64 * 1024 * 1024,
                FreeSpace = 64 * 1024 * 1024 - (_memoryFiles.Count * 2048),
                IsReadOnly = false,
                IsLinuxSystem = false
            });

            // 2. Physical Drives (Secondary / Optional)
            try
            {
                var physDrives = DiskWipeService.GetPhysicalDrives();
                if (physDrives != null && physDrives.Count > 0)
                {
                    for (int i = 0; i < physDrives.Count; i++)
                    {
                        var d = physDrives[i];
                        bool isCdOrIso = (d.Device != null && d.Device.BlockSize == 2048);
                        string dName = !string.IsNullOrEmpty(d.Name) ? d.Name : $"Disk {d.Index} (Storage Drive)";
                        list.Add(new StorageLocation
                        {
                            Id = $"disk{d.Index}",
                            Name = dName,
                            FileSystem = "FAT32 Storage",
                            TotalSize = (long)d.SizeBytes,
                            FreeSpace = (long)d.SizeBytes > 1048576 ? (long)d.SizeBytes - 1048576 : 0,
                            IsReadOnly = isCdOrIso,
                            IsLinuxSystem = false
                        });
                    }
                }
            }
            catch { }

            return list;
        }

        public static List<StorageFileInfo> GetFilesForLocation(string locationId)
        {
            var list = new List<StorageFileInfo>();

            if (string.Equals(locationId, "ram", StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 0; i < _memoryFiles.Count; i++)
                {
                    list.Add(new StorageFileInfo
                    {
                        Name = _memoryFiles[i].Name,
                        Size = _memoryFiles[i].Content.Length,
                        LocationId = "ram",
                        LocationName = "RAM Store"
                    });
                }
                return list;
            }

            try
            {
                string diskName;
                var dev = GetTargetDisk(locationId, out diskName);
                if (dev != null)
                {
                    var diskFiles = DiskWipeService.ListFilesDetailedDirectFromFat32(dev);
                    if (diskFiles != null)
                    {
                        for (int i = 0; i < diskFiles.Count; i++)
                        {
                            list.Add(new StorageFileInfo
                            {
                                Name = diskFiles[i].Name,
                                Size = diskFiles[i].Size,
                                LocationId = locationId ?? "disk0",
                                LocationName = diskName
                            });
                        }
                    }
                }
            }
            catch { }

            return list;
        }

        public static bool LoadFileFromLocation(string filename, string locationId, out string content, out string status)
        {
            filename = NormalizeFilename(filename);
            content = "";

            if (string.Equals(locationId, "ram", StringComparison.OrdinalIgnoreCase))
            {
                if (TryGetMemoryFile(filename, out var ramContent))
                {
                    content = ramContent;
                    status = $"Loaded from RAM Storage: {filename} ({content.Length} chars) [OK]";
                    return true;
                }
                status = $"File '{filename}' not found in RAM.";
                return false;
            }

            try
            {
                string diskName;
                var dev = GetTargetDisk(locationId, out diskName);
                if (dev != null)
                {
                    string readStatus;
                    if (DiskWipeService.ReadFileDirectFromFat32(dev, filename, out content, out readStatus))
                    {
                        status = $"Loaded from {diskName}: {filename} ({(content != null ? content.Length : 0)} bytes) [OK]";
                        PutMemoryFile(filename, content ?? "");
                        return true;
                    }
                }
            }
            catch { }

            return LoadFile(filename, out content, out status);
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

            string diskName;
            var targetDev = GetTargetDisk(locationId, out diskName);
            if (targetDev == null && !IsVfsAvailable())
            {
                PutMemoryFile(filename, content);
                status = $"[ERR] No physical drives detected. Saved to RAM Virtual Store.";
                return false;
            }

            if (IsVfsAvailable())
            {
                try
                {
                    File.WriteAllText("0:\\" + filename, content);
                    PutMemoryFile(filename, content);
                    status = $"Saved to {diskName}: {filename} ({content.Length} chars) [OK]";
                    return true;
                }
                catch { }
            }

            string directStatus = "No target disk available";
            bool directOk = (targetDev != null) && DiskWipeService.WriteFileDirectToFat32(targetDev, filename, content, out directStatus);

            // Always store in RAM so the session is never lost and preview works immediately
            PutMemoryFile(filename, content);

            if (directOk)
            {
                status = $"Saved to {diskName}: {filename} ({content.Length} bytes) [OK]";
                return true;
            }
            else
            {
                status = $"Disk save failed: {directStatus}. File saved in RAM temporary store.";
                return false;
            }
        }

        public static bool SaveBinaryFileToLocation(string filename, byte[] data, string locationId, out string status)
        {
            filename = NormalizeFilename(filename);
            if (data == null) data = new byte[0];

            if (string.Equals(locationId, "ram", StringComparison.OrdinalIgnoreCase))
            {
                PutMemoryBinaryFile(filename, data);
                status = $"Saved to RAM Storage: {filename} ({data.Length} bytes) [OK]";
                return true;
            }

            bool vfsSaved = false;
            if (IsVfsAvailable())
            {
                try
                {
                    File.WriteAllBytes("0:\\" + filename, data);
                    vfsSaved = true;
                }
                catch { }
            }

            string diskName;
            var targetDev = GetTargetDisk(locationId, out diskName);
            string directStatus = "";
            bool directOk = false;
            if (!vfsSaved && targetDev != null)
            {
                directOk = DiskWipeService.WriteBinaryFileDirectToFat32(targetDev, filename, data, out directStatus);
            }

            // Only cache in RAM if small (< 2MB) or if physical write failed
            if (!vfsSaved && !directOk)
            {
                PutMemoryBinaryFile(filename, data);
                status = $"Disk save failed: {directStatus}. File saved in RAM temporary store.";
                return false;
            }
            else if (data.Length < 2 * 1024 * 1024)
            {
                PutMemoryBinaryFile(filename, data);
            }

            status = $"Saved to {diskName}: {filename} ({data.Length} bytes) [OK]";
            return true;
        }

        public static bool LoadBinaryFileFromLocation(string filename, string locationId, out byte[] data, out string status)
        {
            filename = NormalizeFilename(filename);
            data = null;

            if (string.Equals(locationId, "ram", StringComparison.OrdinalIgnoreCase))
            {
                if (TryGetMemoryBinaryFile(filename, out var ramData))
                {
                    data = ramData;
                    status = $"Loaded from RAM Storage: {filename} ({data.Length} bytes) [OK]";
                    return true;
                }
                status = $"File '{filename}' not found in RAM.";
                return false;
            }

            // Check physical disk
            try
            {
                string diskName;
                var dev = GetTargetDisk(locationId, out diskName);
                if (dev != null)
                {
                    if (DiskWipeService.ReadFileDirectFromFat32(dev, filename, out string strContent, out string readStatus))
                    {
                        data = System.Text.Encoding.ASCII.GetBytes(strContent ?? "");
                        status = $"Loaded from {diskName}: {filename} ({data.Length} bytes) [OK]";
                        PutMemoryBinaryFile(filename, data);
                        return true;
                    }
                }
            }
            catch { }

            if (TryGetMemoryBinaryFile(filename, out var fallbackData))
            {
                data = fallbackData;
                status = $"Loaded from RAM Storage: {filename} ({data.Length} bytes) [OK]";
                return true;
            }

            status = $"File '{filename}' not found.";
            return false;
        }

        public static bool SaveFile(string filename, string content, out string status)
        {
            return SaveFileToLocation(filename, content, "ram", out status);
        }

        public static bool LoadFile(string filename, out string content, out string status)
        {
            filename = NormalizeFilename(filename);
            content = "";

            // 1. Check RAM store first (High-Speed Memory Mode)
            if (TryGetMemoryFile(filename, out var ramContent))
            {
                content = ramContent ?? "";
                status = $"Loaded from RAM Storage: {filename} ({content.Length} chars) [OK]";
                return true;
            }

            // 2. Check physical disk fallback
            try
            {
                string diskName;
                var dev = GetTargetDisk("disk0", out diskName);
                if (dev != null)
                {
                    string readStatus;
                    if (DiskWipeService.ReadFileDirectFromFat32(dev, filename, out content, out readStatus))
                    {
                        status = $"Loaded from {diskName}: {filename} ({(content != null ? content.Length : 0)} bytes) [OK]";
                        PutMemoryFile(filename, content ?? ""); // Keep RAM in sync
                        return true;
                    }
                }
            }
            catch { }

            status = $"File '{filename}' not found on RAM or Disk.";
            return false;
        }

        public static bool SaveBinaryFile(string filename, byte[] data, out string status)
        {
            return SaveBinaryFileToLocation(filename, data, "ram", out status);
        }

        public static bool LoadBinaryFile(string filename, out byte[] data, out string status)
        {
            return LoadBinaryFileFromLocation(filename, "ram", out data, out status);
        }

        public static bool DeleteFile(string filename, out string status)
        {
            filename = NormalizeFilename(filename);
            bool deleted = DeleteMemoryFile(filename);

            try
            {
                string diskName;
                var dev = GetTargetDisk("disk0", out diskName);
                if (dev != null)
                {
                    string delStatus;
                    if (DiskWipeService.DeleteFileDirectFromFat32(dev, filename, out delStatus))
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
                string diskName;
                var dev = GetTargetDisk("disk0", out diskName);
                if (dev != null)
                {
                    var diskFiles = DiskWipeService.ListFilesDirectFromFat32(dev);
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
            if (IsVfsAvailable())
            {
                try
                {
                    string[] vFiles = Directory.GetFiles("0:\\");
                    if (vFiles != null)
                    {
                        for (int i = 0; i < vFiles.Length; i++)
                        {
                            string fname = Path.GetFileName(vFiles[i]);
                            bool alreadyInList = false;
                            for (int j = 0; j < files.Count; j++)
                            {
                                if (files[j].StartsWith(fname, StringComparison.OrdinalIgnoreCase))
                                {
                                    alreadyInList = true;
                                    break;
                                }
                            }
                            if (!alreadyInList)
                            {
                                files.Add($"{fname} [DISK-VFS]");
                            }
                        }
                    }
                }
                catch { }
            }

            return files;
        }

        public static List<string> GetFileNamesOnly()
        {
            var list = new List<string>();
            for (int i = 0; i < _memoryFiles.Count; i++)
            {
                string name = _memoryFiles[i].Name;
                if (!string.IsNullOrWhiteSpace(name) && !list.Contains(name))
                {
                    list.Add(name);
                }
            }

            try
            {
                string diskName;
                var dev = GetTargetDisk("disk0", out diskName);
                if (dev != null)
                {
                    var diskFiles = DiskWipeService.ListFilesDirectFromFat32(dev);
                    if (diskFiles != null)
                    {
                        for (int i = 0; i < diskFiles.Count; i++)
                        {
                            string df = diskFiles[i];
                            if (!string.IsNullOrWhiteSpace(df) && !list.Contains(df))
                            {
                                list.Add(df);
                            }
                        }
                    }
                }
            }
            catch { }

            return list;
        }

        public static List<StoredFile> GetMemoryStore()
        {
            return _memoryFiles;
        }
    }
}
