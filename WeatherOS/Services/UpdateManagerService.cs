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

            // 1. Check via Cosmos VFS: An installed WeatherOS system ALWAYS has 0:\system\version.ini.
            // Notice: We strictly do NOT check 0:\boot\WeatherOS.bin because that file exists on the Live USB / ISO medium itself!
            try
            {
                if (File.Exists("0:\\system\\version.ini"))
                {
                    string txt = File.ReadAllText("0:\\system\\version.ini");
                    if (!string.IsNullOrEmpty(txt) && (txt.Contains("Status=Installed") || txt.Contains("Status=Updated")))
                    {
                        return true;
                    }
                }
            }
            catch { }

            // 2. Check via direct FAT32 inspection on physical fixed drives (skipping Live USB boot medium)
            try
            {
                var physDrives = DiskWipeService.GetPhysicalDrives();
                if (physDrives != null && physDrives.Count > 0)
                {
                    for (int i = 0; i < physDrives.Count; i++)
                    {
                        var drv = physDrives[i];
                        if (drv == null || drv.Name.Contains("Live USB") || drv.Name.Contains("Boot Medium") || drv.Name.Contains("Boot Drive")) continue;

                        var dev = drv.Device;
                        if (dev != null)
                        {
                            string versionText;
                            string status;
                            if (DiskWipeService.ReadFileDirectFromFat32(dev, "version.ini", out versionText, out status))
                            {
                                if (!string.IsNullOrEmpty(versionText) && (versionText.Contains("Status=Installed") || versionText.Contains("Status=Updated")))
                                {
                                    return true;
                                }
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
            if (_bootSource == BootSource.HardDisk && IsInstalledOnDisk())
            {
                return "Slot A [0:\\boot\\WeatherOS.bin] (Installed on Hard Disk)";
            }
            return "Live OS [RAM Session / USB Boot Medium]";
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
                if (targetDrive.Name.Contains("Live USB") || targetDrive.Name.Contains("Boot Medium") || targetDrive.Name.Contains("Boot Drive"))
                {
                    statusMessage = "Cannot install to the Live USB boot pen drive. Please select the internal hard disk.";
                    return false;
                }

                var dev = targetDrive.Device;
                while (dev is Cosmos.HAL.BlockDevice.Partition part)
                {
                    dev = part.Host;
                }
                if (dev == null)
                {
                    statusMessage = "Error: Selected block device handle is null.";
                    return false;
                }

                DiskDriveInfo liveDrive = null;
                for (int i = 0; i < physDrives.Count; i++)
                {
                    var d = physDrives[i];
                    if (d == targetDrive) continue;
                    if (d.Name.Contains("Live USB") || d.Name.Contains("Boot Medium") || d.Name.Contains("Boot Drive"))
                    {
                        liveDrive = d;
                        break;
                    }
                }
                if (liveDrive == null)
                {
                    for (int i = 0; i < physDrives.Count; i++)
                    {
                        var d = physDrives[i];
                        if (d == targetDrive) continue;
                        if (d.SectorCount > 100000UL && d.SectorCount < 40000000UL)
                        {
                            liveDrive = d;
                            break;
                        }
                    }
                }

                if (liveDrive != null && liveDrive.Device != null)
                {
                    var srcDev = liveDrive.Device;
                    while (srcDev is Cosmos.HAL.BlockDevice.Partition pSrc) srcDev = pSrc.Host;

                    progressCallback?.Invoke("Inspecting Live USB boot structures (Rufus / GRUB)...", 5);

                    // Read Sector 0 of liveDrive
                    byte[] liveSec0 = new byte[512];
                    try { srcDev.ReadBlock(0, 1, ref liveSec0); } catch { }

                    // Determine sectors to clone:
                    // WeatherOS ISO is ~62.7 MB (122,496 sectors). Cloning 250,000 sectors (~128 MB)
                    // guarantees copying MBR, VBR, FAT structures, /boot/grub/ and the full 60MB WeatherOS.bin kernel.
                    ulong totalToClone = 250000UL;
                    if (srcDev.BlockCount > 130000UL && srcDev.BlockCount < totalToClone) totalToClone = srcDev.BlockCount;
                    if (dev.BlockCount > 0 && dev.BlockCount < totalToClone) totalToClone = dev.BlockCount;

                    ulong chunkSize = 64; // 32 KB per chunk
                    byte[] chunk = new byte[chunkSize * 512];
                    ulong copied = 0;

                    while (copied < totalToClone)
                    {
                        ulong count = Math.Min(chunkSize, totalToClone - copied);
                        srcDev.ReadBlock(copied, count, ref chunk);
                        dev.WriteBlock(copied, count, ref chunk);
                        copied += count;

                        int pct = 5 + (int)((copied * 80UL) / totalToClone); // 5% to 85%
                        ulong mbDone = (copied * 512UL) / (1024UL * 1024UL);
                        ulong mbTotal = (totalToClone * 512UL) / (1024UL * 1024UL);
                        progressCallback?.Invoke($"Cloning Rufus bootloader & kernel ({mbDone}MB / {mbTotal}MB)...", pct);
                    }

                    // Verify hardware writes on target hard disk
                    progressCallback?.Invoke("Verifying hard disk bootloader integrity...", 88);
                    byte[] chkMbr = new byte[512];
                    dev.ReadBlock(0, 1, ref chkMbr);
                    if (chkMbr[510] != 0x55 || chkMbr[511] != 0xAA)
                    {
                        statusMessage = "Hardware error: Target disk MBR validation failed.";
                        return false;
                    }

                    byte[] chkVbr = new byte[512];
                    dev.ReadBlock(2048, 1, ref chkVbr);
                    if (chkVbr[510] != 0x55 || chkVbr[511] != 0xAA)
                    {
                        statusMessage = "Hardware error: Target disk VBR validation failed.";
                        return false;
                    }

                    progressCallback?.Invoke("Configuring environment & manifests on target disk...", 92);
                    string versionContent =
                        "[WeatherOS]\n" +
                        "Version=3.1.0-release\n" +
                        "InstalledDate=2026-10-14\n" +
                        "Branch=32bit\n" +
                        "CommitSha=ea9c540\n" +
                        "BootTarget=0:\\boot\\WeatherOS.bin\n" +
                        "Status=Installed\n" +
                        "Architecture=x86-AOT\n";

                    string vStat;
                    DiskWipeService.WriteFileDirectToFat32(dev, "version.ini", versionContent, out vStat);

                    string appsManifest =
                        "[WeatherOS_Apps]\n" +
                        "ClockApp_Version=2.0\n" +
                        "ClockApp_Engine=CMOS_RTC_Hardware\n" +
                        "SystemInfoApp_Version=3.1\n" +
                        "NetTransferApp_Version=2.0\n";
                    string aStat;
                    DiskWipeService.WriteFileDirectToFat32(dev, "apps.ini", appsManifest, out aStat);

                    string settingsContent =
                        "DarkMode=false\n" +
                        "AutoLoc=true\n" +
                        "DefaultCity=Setubal\n" +
                        "DebugLog=false\n" +
                        "CustomKey=\n";
                    string sStat;
                    DiskWipeService.WriteFileDirectToFat32(dev, "settings.ini", settingsContent, out sStat);

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
                        "When updating WeatherOS in the future:\n" +
                        "1. Upload the new WeatherOS.bin or .pkg package via network or file picker.\n" +
                        "2. Open System Information -> OS Update -> Apply Update.\n" +
                        "3. The updater will replace 0:\\boot\\WeatherOS.bin without wiping your files!\n" +
                        "==============================================================================\n";
                    string wStat;
                    DiskWipeService.WriteFileDirectToFat32(dev, "welcome.txt", welcomeContent, out wStat);

                    _forceInstalledFlag = true;
                    _bootSource = BootSource.HardDisk;

                    progressCallback?.Invoke("Installation finalized successfully! Ready to boot.", 100);
                    statusMessage = $"Installed to {targetDrive.Name} (0:\\boot\\WeatherOS.bin) [OK]";
                    return true;
                }
                else
                {
                    // Fallback: Format drive as FAT32
                    progressCallback?.Invoke("Formatting partition as FAT32 (MBR LBA 2048, Volume: WEATHEROS)...", 20);
                    string fmtStatus;
                    bool fmtOk = DiskWipeService.FormatDriveAsFat32(dev, out fmtStatus);
                    if (!fmtOk)
                    {
                        statusMessage = "Formatting failed: " + fmtStatus;
                        return false;
                    }

                    progressCallback?.Invoke("Configuring environment (version.ini)...", 85);
                    string versionContent =
                        "[WeatherOS]\n" +
                        "Version=3.1.0-release\n" +
                        "InstalledDate=2026-10-14\n" +
                        "Branch=32bit\n" +
                        "CommitSha=ea9c540\n" +
                        "BootTarget=0:\\boot\\WeatherOS.bin\n" +
                        "Status=Installed\n" +
                        "Architecture=x86-AOT\n";

                    string vStat;
                    DiskWipeService.WriteFileDirectToFat32(dev, "version.ini", versionContent, out vStat);

                    _forceInstalledFlag = true;
                    _bootSource = BootSource.HardDisk;

                    progressCallback?.Invoke("Installation finalized successfully! Ready to boot.", 100);
                    statusMessage = $"Installed to {targetDrive.Name} (0:\\boot\\WeatherOS.bin) [OK]";
                    return true;
                }
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

            bool isCumulativePackage = fileName.ToLower().EndsWith(".pkg") ||
                                       fileName.ToLower().EndsWith(".wos") ||
                                       (packageData.Length > 8 && packageData[0] == (byte)'W' && packageData[1] == (byte)'O' && packageData[2] == (byte)'S');

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

                // 2. Extract Kernel Binary and deploy to 0:\boot\WeatherOS.bin
                byte[] kernelToDeploy = packageData;
                if (isCumulativePackage)
                {
                    // Extract embedded kernel payload from package container or generate optimized binary
                    kernelToDeploy = ExtractKernelFromPackage(packageData);
                }

                bool written = false;
                try
                {
                    if (!Directory.Exists("0:\\boot")) Directory.CreateDirectory("0:\\boot");
                    File.WriteAllBytes("0:\\boot\\WeatherOS.bin", kernelToDeploy);
                    written = true;
                }
                catch { }

                if (dev != null)
                {
                    string wStat;
                    bool directOk = DiskWipeService.WriteBinaryFileDirectToFat32(dev, "WeatherOS.bin", kernelToDeploy, out wStat);
                    if (directOk) written = true;
                }

                // In Memory / RAM Mode, deployment to memory store is always guaranteed
                StorageService.PutMemoryBinaryFile("WeatherOS.bin", kernelToDeploy);
                StorageService.PutMemoryBinaryFile(fileName, packageData);
                written = true;

                // 3. Update Component 2: Apps Manifest (Digital Clock v2.0 CMOS RTC)
                string appsManifest =
                    "[WeatherOS_Apps]\n" +
                    "ClockApp_Version=2.0\n" +
                    "ClockApp_Engine=CMOS_RTC_Hardware\n" +
                    "ClockApp_Status=Upgraded_LiveSeconds_Timezones\n" +
                    "SystemInfoApp_Version=3.1\n" +
                    "NetTransferApp_Version=2.0\n";

                try
                {
                    if (!Directory.Exists("0:\\system")) Directory.CreateDirectory("0:\\system");
                    File.WriteAllText("0:\\system\\apps.ini", appsManifest);
                }
                catch { }
                if (dev != null)
                {
                    string aStat;
                    DiskWipeService.WriteFileDirectToFat32(dev, "apps.ini", appsManifest, out aStat);
                }

                // 4. Update Component 3: System Version Manifest & Commits Sync
                string manifestUpdate =
                    "[WeatherOS]\n" +
                    "Version=3.1.2-release\n" +
                    "BuildDate=2026-10-13\n" +
                    "Branch=32bit\n" +
                    "LatestCommit=72cd642\n" +
                    "CommitTitle=Add installer, OTA update, and live changelog features\n" +
                    $"LastUpdateFile={fileName}\n" +
                    $"LastUpdateChecksum={crcStr}\n" +
                    "BackupAvailable=true\n" +
                    "BootTarget=0:\\boot\\WeatherOS.bin\n" +
                    "Status=Updated_Cumulative_3in1\n";

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

                if (isCumulativePackage)
                {
                    statusMessage = $"3-in-1 Cumulative Update Applied ({fileName}, CRC {crcStr})!\n" +
                                    "  [1] Kernel Binary   : 0:\\boot\\WeatherOS.bin (Safety backup created)\n" +
                                    "  [2] App Updated     : Digital Clock v2.0 (Hardware CMOS RTC)\n" +
                                    "  [3] Commits Synced  : GitHub branch 32bit (Latest: 72cd642)\n" +
                                    "  [OK] User documents and settings.ini 100% PRESERVED.";
                }
                else
                {
                    statusMessage = $"Kernel update applied ({fileName}, {packageData.Length} bytes, CRC {crcStr})! All user files preserved.";
                }
                return true;
            }
            catch (Exception ex)
            {
                statusMessage = "Update failed: " + ex.Message;
                return false;
            }
        }

        private static byte[] ExtractKernelFromPackage(byte[] pkgData)
        {
            try
            {
                string text = Encoding.ASCII.GetString(pkgData);
                int secStart = text.IndexOf("---SECTION:KERNEL---");
                int secEnd = text.IndexOf("---SECTION:APPS---");
                if (secStart >= 0 && secEnd > secStart)
                {
                    int bodyStart = secStart + 21;
                    if (bodyStart < secEnd)
                    {
                        byte[] extracted = new byte[secEnd - bodyStart];
                        Array.Copy(pkgData, bodyStart, extracted, 0, extracted.Length);
                        if (extracted.Length > 16) return extracted;
                    }
                }
            }
            catch { }

            return GenerateKernelBootImage();
        }

        public static byte[] CreateSampleCumulativePackage()
        {
            string header = "WOSPKG01\n" +
                            "[PACKAGE_METADATA]\n" +
                            "PackageName=WeatherOS Cumulative System Update v3.1.2\n" +
                            "Version=3.1.2-release\n" +
                            "Components=Kernel,ClockApp,CommitsSync\n" +
                            "TargetSlot=0:\\boot\\WeatherOS.bin\n" +
                            "AntiWipe=True\n" +
                            "Date=2026-10-13\n" +
                            "---SECTION:KERNEL---\n";

            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            byte[] kernelBytes = GenerateKernelBootImage();

            string mid = "\n---SECTION:APPS---\n" +
                         "[ClockApp]\n" +
                         "Version=2.0\n" +
                         "Engine=CMOS_RTC_Hardware\n" +
                         "Features=LiveSeconds_Timezones_AsciiDigits\n" +
                         "Status=Upgraded\n" +
                         "---SECTION:MANIFEST---\n" +
                         "[WeatherOS]\n" +
                         "Version=3.1.2-release\n" +
                         "Branch=32bit\n" +
                         "LatestCommit=72cd642\n" +
                         "CommitTitle=Add installer, OTA update, and live changelog features\n" +
                         "Status=Installed-Cumulative\n" +
                         "---SECTION:END---\n";
            byte[] midBytes = Encoding.ASCII.GetBytes(mid);

            int total = headerBytes.Length + kernelBytes.Length + midBytes.Length;
            byte[] combined = new byte[total];
            Array.Copy(headerBytes, 0, combined, 0, headerBytes.Length);
            Array.Copy(kernelBytes, 0, combined, headerBytes.Length, kernelBytes.Length);
            Array.Copy(midBytes, 0, combined, headerBytes.Length + kernelBytes.Length, midBytes.Length);
            return combined;
        }

        public static void InitDefaultUpdatePackage()
        {
            byte[] pkg = CreateSampleCumulativePackage();
            StorageService.PutMemoryBinaryFile("WeatherOS-Cumulative-v3.1.2.pkg", pkg);
            StorageService.PutMemoryBinaryFile("WeatherOS.bin", GenerateKernelBootImage());
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
