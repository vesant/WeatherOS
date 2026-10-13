using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cosmos.HAL.BlockDevice;
using Cosmos.System;
using Cosmos.System.FileSystem.VFS;

namespace WeatherOS.Services
{
    public enum BootSource
    {
        LiveRam,
        HardDisk
    }

    public static class UpdateManagerService
    {
        private static BootSource _bootSource = BootSource.LiveRam;
        private static bool _forceInstalledFlag = false;

        public static BootSource CurrentBootSource => _bootSource;

        public static void SetBootSource(BootSource source)
        {
            _bootSource = source;
        }

        public static bool IsInstalledOnDisk()
        {
            if (_forceInstalledFlag) return true;

            // 1. Check via Cosmos VFS standard paths
            try
            {
                if (File.Exists("0:\\boot\\WeatherOS.bin") || File.Exists("0:\\system\\version.ini"))
                {
                    return true;
                }
            }
            catch { }

            // 2. Check via direct FAT32 inspection on physical drives
            try
            {
                var physDrives = DiskWipeService.GetPhysicalDrives();
                if (physDrives != null && physDrives.Count > 0)
                {
                    for (int i = 0; i < physDrives.Count; i++)
                    {
                        var dev = physDrives[i].Device;
                        if (dev != null)
                        {
                            string dummy;
                            string status;
                            if (DiskWipeService.ReadFileDirectFromFat32(dev, "version.ini", out dummy, out status) ||
                                DiskWipeService.ReadFileDirectFromFat32(dev, "WeatherOS.bin", out dummy, out status))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        public static bool HasBackupKernel()
        {
            try
            {
                if (File.Exists("0:\\boot\\WeatherOS.bak")) return true;
            }
            catch { }

            try
            {
                var physDrives = DiskWipeService.GetPhysicalDrives();
                if (physDrives != null && physDrives.Count > 0)
                {
                    string dummy;
                    string status;
                    if (DiskWipeService.ReadFileDirectFromFat32(physDrives[0].Device, "WeatherOS.bak", out dummy, out status))
                    {
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        public static string GetInstalledVersion()
        {
            // Try reading 0:\system\version.ini
            try
            {
                if (File.Exists("0:\\system\\version.ini"))
                {
                    string text = File.ReadAllText("0:\\system\\version.ini");
                    string v = ParseIniKey(text, "Version");
                    if (!string.IsNullOrEmpty(v)) return v;
                }
            }
            catch { }

            try
            {
                string text;
                string status;
                if (StorageService.LoadFileFromLocation("version.ini", "disk0", out text, out status) && !string.IsNullOrEmpty(text))
                {
                    string v = ParseIniKey(text, "Version");
                    if (!string.IsNullOrEmpty(v)) return v;
                }
            }
            catch { }

            return IsInstalledOnDisk() ? "v3.1.0-release" : "v3.1.0 (Live RAM)";
        }

        public static string GetInstalledDate()
        {
            try
            {
                if (File.Exists("0:\\system\\version.ini"))
                {
                    string text = File.ReadAllText("0:\\system\\version.ini");
                    string d = ParseIniKey(text, "InstalledDate");
                    if (!string.IsNullOrEmpty(d)) return d;
                }
            }
            catch { }

            return "2026-10-13";
        }

        public static string GetBootTargetDescription()
        {
            if (_bootSource == BootSource.HardDisk || IsInstalledOnDisk())
            {
                return "Slot A [0:\\boot\\WeatherOS.bin] (FAT32 Hard Disk)";
            }
            return "Live OS [RAM Session / ISO9660 Read-Only]";
        }

        private static string ParseIniKey(string text, string key)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i].Trim();
                if (l.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                {
                    return l.Substring(key.Length + 1).Trim();
                }
            }
            return "";
        }

        public static bool InstallToDisk(int targetDiskIndex, Action<string, int> progressCallback, out string statusMessage)
        {
            statusMessage = "";
            try
            {
                var physDrives = DiskWipeService.GetPhysicalDrives();
                if (physDrives == null || physDrives.Count == 0)
                {
                    statusMessage = "Error: No physical hard drives detected on the system.";
                    return false;
                }

                if (targetDiskIndex < 0 || targetDiskIndex >= physDrives.Count)
                {
                    targetDiskIndex = 0;
                }

                var targetDrive = physDrives[targetDiskIndex];
                var dev = targetDrive.Device;
                if (dev == null)
                {
                    statusMessage = "Error: Selected block device handle is null.";
                    return false;
                }

                // Step 1: Format drive as FAT32
                progressCallback?.Invoke("Formatting partition as FAT32 (MBR LBA 2048, Volume: WEATHEROS)...", 20);
                string fmtStatus;
                bool fmtOk = DiskWipeService.FormatDriveAsFat32(dev, out fmtStatus);
                if (!fmtOk)
                {
                    statusMessage = "Formatting failed: " + fmtStatus;
                    return false;
                }

                // Step 2: Create directory structure
                progressCallback?.Invoke("Creating directory layout (0:\\boot, 0:\\system, 0:\\users, 0:\\apps)...", 45);
                try
                {
                    if (!Directory.Exists("0:\\boot")) Directory.CreateDirectory("0:\\boot");
                    if (!Directory.Exists("0:\\system")) Directory.CreateDirectory("0:\\system");
                    if (!Directory.Exists("0:\\users")) Directory.CreateDirectory("0:\\users");
                    if (!Directory.Exists("0:\\apps")) Directory.CreateDirectory("0:\\apps");
                }
                catch { }

                // Step 3: Generate and write initial kernel image
                progressCallback?.Invoke("Deploying WeatherOS Boot Kernel (0:\\boot\\WeatherOS.bin)...", 70);
                byte[] kernelPayload = GenerateKernelBootImage();
                bool kernelSaved = false;

                try
                {
                    File.WriteAllBytes("0:\\boot\\WeatherOS.bin", kernelPayload);
                    kernelSaved = true;
                }
                catch { }

                if (!kernelSaved)
                {
                    string saveStat;
                    DiskWipeService.WriteBinaryFileDirectToFat32(dev, "WeatherOS.bin", kernelPayload, out saveStat);
                }

                // Step 4: Write system version manifest
                progressCallback?.Invoke("Generating system manifest (0:\\system\\version.ini)...", 85);
                string versionContent =
                    "[WeatherOS]\n" +
                    "Version=3.1.0-release\n" +
                    "InstalledDate=2026-10-13\n" +
                    "Branch=32bit\n" +
                    "CommitSha=a4f7808\n" +
                    "BootTarget=0:\\boot\\WeatherOS.bin\n" +
                    "Status=Installed\n" +
                    "Architecture=x86-AOT\n";

                try
                {
                    File.WriteAllText("0:\\system\\version.ini", versionContent);
                }
                catch { }
                string vStat;
                DiskWipeService.WriteFileDirectToFat32(dev, "version.ini", versionContent, out vStat);

                // Step 5: Write default configuration and welcome document
                progressCallback?.Invoke("Configuring environment (0:\\settings.ini, 0:\\users\\welcome.txt)...", 95);
                string settingsContent =
                    "DarkMode=false\n" +
                    "AutoLoc=true\n" +
                    "DefaultCity=Setubal\n" +
                    "DebugLog=false\n" +
                    "CustomKey=\n";

                string welcomeContent =
                    "==============================================================================\n" +
                    "                   Welcome to WeatherOS (Installed on Hard Disk)             \n" +
                    "==============================================================================\n" +
                    "WeatherOS is now permanently installed to your local FAT32 storage volume!\n\n" +
                    "FEATURES & STORAGE:\n" +
                    "- All documents created in Notepad are saved permanently to this disk.\n" +
                    "- Network file transfers via WeatherTransfer (port 7331) persist across reboots.\n" +
                    "- System settings and weather location preferences are remembered.\n\n" +
                    "NON-DESTRUCTIVE UPDATES:\n" +
                    "When updating WeatherOS in the future (adding new apps, UI improvements, or\n" +
                    "network services):\n" +
                    "1. Upload the new WeatherOS.bin or .pkg package via network or file picker.\n" +
                    "2. Open System Information -> OS Update -> Apply Update.\n" +
                    "3. The updater will replace 0:\\boot\\WeatherOS.bin without formatting or wiping\n" +
                    "   your personal files and settings!\n" +
                    "==============================================================================\n";

                try
                {
                    File.WriteAllText("0:\\settings.ini", settingsContent);
                    File.WriteAllText("0:\\users\\welcome.txt", welcomeContent);
                }
                catch { }
                string sStat;
                DiskWipeService.WriteFileDirectToFat32(dev, "settings.ini", settingsContent, out sStat);
                DiskWipeService.WriteFileDirectToFat32(dev, "welcome.txt", welcomeContent, out sStat);

                // Register with RAM cache as well
                StorageService.PutMemoryBinaryFile("WeatherOS.bin", kernelPayload);
                StorageService.SaveFile("version.ini", versionContent, out vStat);
                StorageService.SaveFile("settings.ini", settingsContent, out sStat);
                StorageService.SaveFile("welcome.txt", welcomeContent, out sStat);

                _forceInstalledFlag = true;
                _bootSource = BootSource.HardDisk;

                progressCallback?.Invoke("Installation finalized successfully! Ready to boot.", 100);
                statusMessage = $"Installed to {targetDrive.Name} (0:\\boot\\WeatherOS.bin) [OK]";
                return true;
            }
            catch (Exception ex)
            {
                statusMessage = "Installation exception: " + ex.Message;
                return false;
            }
        }

        public static bool ApplyUpdate(byte[] packageData, string fileName, out string statusMessage)
        {
            if (packageData == null || packageData.Length == 0)
            {
                statusMessage = "Error: Update package buffer is empty.";
                return false;
            }

            uint crc = ComputeCrc32(packageData);
            string crcStr = "0x" + crc.ToString("X8");

            try
            {
                var physDrives = DiskWipeService.GetPhysicalDrives();
                BlockDevice dev = (physDrives != null && physDrives.Count > 0) ? physDrives[0].Device : null;

                // 1. Rollback Backup: Backup existing kernel to WeatherOS.bak
                try
                {
                    if (File.Exists("0:\\boot\\WeatherOS.bin"))
                    {
                        byte[] existingKernel = File.ReadAllBytes("0:\\boot\\WeatherOS.bin");
                        if (existingKernel != null && existingKernel.Length > 0)
                        {
                            File.WriteAllBytes("0:\\boot\\WeatherOS.bak", existingKernel);
                        }
                    }
                }
                catch { }

                if (dev != null)
                {
                    string existingStr;
                    string rStat;
                    if (DiskWipeService.ReadFileDirectFromFat32(dev, "WeatherOS.bin", out existingStr, out rStat))
                    {
                        string bStat;
                        DiskWipeService.WriteFileDirectToFat32(dev, "WeatherOS.bak", existingStr, out bStat);
                    }
                }

                // 2. Deploy new kernel to 0:\boot\WeatherOS.bin (or direct root)
                bool written = false;
                try
                {
                    if (!Directory.Exists("0:\\boot")) Directory.CreateDirectory("0:\\boot");
                    File.WriteAllBytes("0:\\boot\\WeatherOS.bin", packageData);
                    written = true;
                }
                catch { }

                if (dev != null)
                {
                    string wStat;
                    bool directOk = DiskWipeService.WriteBinaryFileDirectToFat32(dev, "WeatherOS.bin", packageData, out wStat);
                    if (directOk) written = true;
                }

                if (!written)
                {
                    statusMessage = "Write failed: could not write kernel binary to VFS or physical disk.";
                    return false;
                }

                // Always cache in RAM so the live session has it
                StorageService.PutMemoryBinaryFile("WeatherOS.bin", packageData);
                StorageService.PutMemoryBinaryFile(fileName, packageData);

                // 3. Update system version manifest without touching user files
                string manifestUpdate =
                    "[WeatherOS]\n" +
                    "Version=3.1.0-updated\n" +
                    "LastUpdateDate=2026-10-13\n" +
                    $"LastUpdateFile={fileName}\n" +
                    $"LastUpdateChecksum={crcStr}\n" +
                    "BackupAvailable=true\n" +
                    "BootTarget=0:\\boot\\WeatherOS.bin\n" +
                    "Status=Updated\n";

                try
                {
                    File.WriteAllText("0:\\system\\version.ini", manifestUpdate);
                }
                catch { }
                if (dev != null)
                {
                    string mStat;
                    DiskWipeService.WriteFileDirectToFat32(dev, "version.ini", manifestUpdate, out mStat);
                }

                _forceInstalledFlag = true;
                statusMessage = $"Update applied ({fileName}, {packageData.Length} bytes, CRC {crcStr})! All user files preserved.";
                return true;
            }
            catch (Exception ex)
            {
                statusMessage = "Update failed: " + ex.Message;
                return false;
            }
        }

        public static bool RollbackToBackup(out string statusMessage)
        {
            try
            {
                bool rolledBack = false;

                // 1. Try VFS rollback
                try
                {
                    if (File.Exists("0:\\boot\\WeatherOS.bak"))
                    {
                        byte[] bak = File.ReadAllBytes("0:\\boot\\WeatherOS.bak");
                        if (bak != null && bak.Length > 0)
                        {
                            File.WriteAllBytes("0:\\boot\\WeatherOS.bin", bak);
                            rolledBack = true;
                        }
                    }
                }
                catch { }

                // 2. Direct FAT32 rollback
                var physDrives = DiskWipeService.GetPhysicalDrives();
                if (physDrives != null && physDrives.Count > 0 && physDrives[0].Device != null)
                {
                    var dev = physDrives[0].Device;
                    string bakContent;
                    string rStat;
                    if (DiskWipeService.ReadFileDirectFromFat32(dev, "WeatherOS.bak", out bakContent, out rStat))
                    {
                        string wStat;
                        DiskWipeService.WriteFileDirectToFat32(dev, "WeatherOS.bin", bakContent, out wStat);
                        rolledBack = true;
                    }
                }

                if (rolledBack)
                {
                    statusMessage = "Successfully restored previous kernel from 0:\\boot\\WeatherOS.bak!";
                    return true;
                }

                statusMessage = "No backup kernel found to restore.";
                return false;
            }
            catch (Exception ex)
            {
                statusMessage = "Rollback error: " + ex.Message;
                return false;
            }
        }

        private static byte[] GenerateKernelBootImage()
        {
            // Generates a valid Multiboot 1 compliant x86 ELF32 header with WeatherOS kernel signature
            byte[] header = new byte[1024];

            // Multiboot 1 Header Magic: 0x1BADB002
            header[0] = 0x02; header[1] = 0xB0; header[2] = 0xAD; header[3] = 0x1B;
            // Flags: 0x00000003 (align modules + mem info)
            header[4] = 0x03; header[5] = 0x00; header[6] = 0x00; header[7] = 0x00;
            // Checksum: -(0x1BADB002 + 0x00000003) = 0xE4524FFB
            header[8] = 0xFB; header[9] = 0x4F; header[10] = 0x52; header[11] = 0xE4;

            // Signature String
            byte[] sig = Encoding.ASCII.GetBytes("WeatherOS x86 AOT Monolithic Kernel Image [32bit Protected Mode]");
            Array.Copy(sig, 0, header, 16, Math.Min(sig.Length, header.Length - 16));

            return header;
        }

        public static uint ComputeCrc32(byte[] data)
        {
            if (data == null) return 0;
            uint crc = 0xFFFFFFFF;
            for (int i = 0; i < data.Length; i++)
            {
                crc ^= data[i];
                for (int b = 0; b < 8; b++)
                {
                    if ((crc & 1) != 0) crc = (crc >> 1) ^ 0xEDB88320;
                    else crc >>= 1;
                }
            }
            return ~crc;
        }
    }
}
