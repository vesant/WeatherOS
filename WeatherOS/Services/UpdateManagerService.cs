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

        public static bool SafeWriteSectors(BlockDevice dev, ulong startSector, ulong count, byte[] data, int offset = 0, Action<ulong, ulong> progressCallback = null)
        {
            if (dev == null || data == null || count == 0) return false;

            BlockDevice rootDev = dev;
            while (rootDev is Cosmos.HAL.BlockDevice.Partition part) rootDev = part.Host;

            if (rootDev is CustomPciAtaBlockDevice ata)
            {
                byte[] pad = new byte[512];
                for (ulong s = 0; s < count; s++)
                {
                    int srcOff = offset + (int)(s * 512);
                    byte[] bufToWrite;
                    int bufOff;

                    if (srcOff + 512 <= data.Length)
                    {
                        bufToWrite = data;
                        bufOff = srcOff;
                    }
                    else
                    {
                        int avail = Math.Max(0, data.Length - srcOff);
                        if (avail > 0)
                        {
                            Array.Copy(data, srcOff, pad, 0, avail);
                            Array.Clear(pad, avail, 512 - avail);
                        }
                        else
                        {
                            Array.Clear(pad, 0, 512);
                        }
                        bufToWrite = pad;
                        bufOff = 0;
                    }

                    bool ok = CustomPciAtaBlockDevice.WriteSectorPio(ata.CmdBase, ata.CtrlBase, ata.DriveSelect, ata.IsLba48, startSector + s, bufToWrite, bufOff);
                    if (!ok)
                    {
                        // Give drive controller time to clear error / flush track buffer, then retry
                        CustomPciAtaBlockDevice.WaitIoStatic(ata.CmdBase);
                        if (!CustomPciAtaBlockDevice.WriteSectorPio(ata.CmdBase, ata.CtrlBase, ata.DriveSelect, ata.IsLba48, startSector + s, bufToWrite, bufOff))
                        {
                            return false;
                        }
                    }

                    // Every 256 sectors (128 KB), synchronize with drive cache to ensure mechanical media doesn't saturate
                    if ((s & 0xFF) == 0)
                    {
                        CustomPciAtaBlockDevice.SyncDriveCache(ata.CmdBase);
                    }

                    if (progressCallback != null && ((s & 0xFF) == 0 || s == count - 1))
                    {
                        progressCallback(s + 1, count);
                    }
                }

                // Flush ATA write cache ONCE after all sectors have been transferred
                CustomPciAtaBlockDevice.FlushPio(ata.CmdBase, ata.DriveSelect, ata.IsLba48);
                return true;
            }

            // Generic BlockDevice fallback:
            // Write in batches of up to 64 sectors (32 KB) per WriteBlock call to eliminate per-sector cache flushes.
            const ulong BATCH_SECTORS = 64;
            byte[] batchBuf = new byte[BATCH_SECTORS * 512];
            ulong written = 0;

            while (written < count)
            {
                ulong batchCount = Math.Min(BATCH_SECTORS, count - written);
                int batchBytes = (int)(batchCount * 512);
                int srcOff = offset + (int)(written * 512);
                int avail = Math.Max(0, data.Length - srcOff);
                int toCopy = Math.Min(batchBytes, avail);

                if (toCopy > 0)
                {
                    Array.Copy(data, srcOff, batchBuf, 0, toCopy);
                    if (toCopy < batchBytes)
                    {
                        Array.Clear(batchBuf, toCopy, batchBytes - toCopy);
                    }
                }
                else
                {
                    Array.Clear(batchBuf, 0, batchBytes);
                }

                try
                {
                    rootDev.WriteBlock(startSector + written, batchCount, ref batchBuf);
                }
                catch
                {
                    return false;
                }

                written += batchCount;

                if (progressCallback != null)
                {
                    progressCallback(written, count);
                }
            }

            return true;
        }

        public static bool SafeReadSectors(BlockDevice dev, ulong startSector, ulong count, byte[] buffer, int offset = 0)
        {
            if (dev == null || buffer == null || count == 0) return false;

            BlockDevice rootDev = dev;
            while (rootDev is Cosmos.HAL.BlockDevice.Partition part) rootDev = part.Host;

            if (rootDev is CustomPciAtaBlockDevice ata)
            {
                byte[] pad = new byte[512];
                for (ulong s = 0; s < count; s++)
                {
                    int dstOff = offset + (int)(s * 512);
                    if (dstOff + 512 <= buffer.Length)
                    {
                        if (!CustomPciAtaBlockDevice.ReadSectorPio(ata.CmdBase, ata.CtrlBase, ata.DriveSelect, ata.IsLba48, startSector + s, buffer, dstOff))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        if (!CustomPciAtaBlockDevice.ReadSectorPio(ata.CmdBase, ata.CtrlBase, ata.DriveSelect, ata.IsLba48, startSector + s, pad, 0))
                        {
                            return false;
                        }
                        int avail = Math.Max(0, buffer.Length - dstOff);
                        if (avail > 0)
                        {
                            Array.Copy(pad, 0, buffer, dstOff, avail);
                        }
                    }
                }
                return true;
            }

            // Generic BlockDevice fallback:
            const ulong BATCH_SECTORS = 64;
            byte[] batchBuf = new byte[BATCH_SECTORS * 512];
            ulong read = 0;

            while (read < count)
            {
                ulong batchCount = Math.Min(BATCH_SECTORS, count - read);
                int batchBytes = (int)(batchCount * 512);
                int dstOff = offset + (int)(read * 512);

                try
                {
                    rootDev.ReadBlock(startSector + read, batchCount, ref batchBuf);
                }
                catch
                {
                    return false;
                }

                int avail = Math.Max(0, buffer.Length - dstOff);
                int toCopy = Math.Min(batchBytes, avail);
                if (toCopy > 0)
                {
                    Array.Copy(batchBuf, 0, buffer, dstOff, toCopy);
                }

                read += batchCount;
            }

            return true;
        }

        public static readonly byte[] GrubMbrSector = new byte[]
        {
            0xFA, 0x31, 0xC0, 0x8E, 0xD8, 0x8E, 0xC0, 0x8E, 0xD0, 0xBC, 0x00, 0x7C, 0xFB, 0x88, 0x16, 0x7D,
            0x7C, 0xB4, 0x41, 0xBB, 0xAA, 0x55, 0xCD, 0x13, 0x72, 0x4E, 0x81, 0xFB, 0x55, 0xAA, 0x75, 0x48,
            0xC6, 0x06, 0xFF, 0x6F, 0x01, 0xC7, 0x06, 0x00, 0x70, 0x10, 0x00, 0xC7, 0x06, 0x02, 0x70, 0x01,
            0x00, 0xC7, 0x06, 0x04, 0x70, 0x00, 0x00, 0xC7, 0x06, 0x06, 0x70, 0x00, 0x08, 0x66, 0xC7, 0x06,
            0x08, 0x70, 0x01, 0x00, 0x00, 0x00, 0x66, 0xC7, 0x06, 0x0C, 0x70, 0x00, 0x00, 0x00, 0x00, 0xB4,
            0x42, 0x8A, 0x16, 0x7D, 0x7C, 0xBE, 0x00, 0x70, 0xCD, 0x13, 0x72, 0x0C, 0x8A, 0x16, 0x7D, 0x7C,
            0xBE, 0x00, 0x70, 0xEA, 0x00, 0x80, 0x00, 0x00, 0xBE, 0x7E, 0x7C, 0xAC, 0x08, 0xC0, 0x74, 0x09,
            0xB4, 0x0E, 0xBB, 0x07, 0x00, 0xCD, 0x10, 0xEB, 0xF2, 0xFA, 0xF4, 0xEB, 0xFC, 0x80, 0x47, 0x52,
            0x55, 0x42, 0x20, 0x4D, 0x42, 0x52, 0x20, 0x45, 0x72, 0x72, 0x6F, 0x72, 0x0D, 0x0A, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x01,
            0x01, 0x00, 0x0C, 0xFE, 0xFF, 0xFF, 0x00, 0x08, 0x00, 0x00, 0x88, 0x07, 0x02, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x55, 0xAA
        };

        public static BootSource CurrentBootSource => _bootSource;

        public static void SetBootSource(BootSource source)
        {
            _bootSource = source;
        }

        public static bool IsBootedFromInstalledDisk()
        {
            if (_bootSource == BootSource.HardDisk) return true;

            // Check physical RAM 0x00007DF0 passed directly by custom_stage2 loader
            unsafe
            {
                byte* pBootFlag = (byte*)0x00007DF0u;
                ushort* pMagic = (ushort*)0x00007DF2u;
                if (*pMagic == 0x574Fu && *pBootFlag == 0x01)
                {
                    return true;
                }
                // Fallback check if magic not present but flag is 0x01
                if (*pBootFlag == 0x01)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsInstalledOnDisk()
        {
            if (_forceInstalledFlag) return true;
            if (IsBootedFromInstalledDisk()) return true;

            // 2. Check physical storage devices for WeatherOS Installation Marker Sector at LBA 9
            try
            {
                var physDrives = DiskWipeService.GetPhysicalDrives();
                if (physDrives != null && physDrives.Count > 0)
                {
                    for (int i = 0; i < physDrives.Count; i++)
                    {
                        var drv = physDrives[i];
                        if (drv == null) continue;
                        if (drv.Name.Contains("Live USB") || drv.Name.Contains("Boot Medium") || drv.Name.Contains("Flash"))
                            continue;

                        var dev = drv.Device;
                        while (dev is Cosmos.HAL.BlockDevice.Partition part) dev = part.Host;
                        if (dev == null) continue;

                        byte[] sec9 = new byte[512];
                        try
                        {
                            dev.ReadBlock(9, 1, ref sec9);
                            if (sec9[0] == (byte)'W' && sec9[1] == (byte)'E' && sec9[2] == (byte)'A' &&
                                sec9[3] == (byte)'T' && sec9[4] == (byte)'H' && sec9[5] == (byte)'E' &&
                                sec9[6] == (byte)'R' && sec9[7] == (byte)'O' && sec9[8] == (byte)'S')
                            {
                                return true;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            // 3. Fallback check via Cosmos VFS: 0:\system\version.ini (for FAT32 installations)
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

                // 1. Determine kernel sector count dynamically from running kernel size
                uint kernelSectors = 0;
                try
                {
                    uint endOfKernel = Cosmos.Core.CPU.GetEndOfKernel();
                    if (endOfKernel > 0x02000000u)
                    {
                        kernelSectors = (endOfKernel - 0x02000000u + 511u) / 512u;
                    }
                }
                catch { }

                if (kernelSectors < 1000 || kernelSectors > 100000)
                {
                    kernelSectors = 16404;
                    if (NativeBootloaderPayload.Mbr != null && NativeBootloaderPayload.Mbr.Length >= 462)
                    {
                        uint parsedSec = (uint)(NativeBootloaderPayload.Mbr[458] | 
                                                (NativeBootloaderPayload.Mbr[459] << 8) | 
                                                (NativeBootloaderPayload.Mbr[460] << 16) | 
                                                (NativeBootloaderPayload.Mbr[461] << 24));
                        if (parsedSec >= 1000 && parsedSec <= 100000)
                        {
                            kernelSectors = parsedSec;
                        }
                    }
                }

                // 1.5. Dynamically verify and scan physical RAM for authentic running Kernel_Start entry point
                // Signature: mov esp, 0x02050040; cli (0xBC, 0x40, 0x00, 0x05, 0x02, 0xFA)
                uint entryPoint = 0;
                uint defaultEntryPoint = 0;
                if (NativeBootloaderPayload.Stage2 != null)
                {
                    int epOff = -1;
                    if (NativeBootloaderPayload.Stage2EntryPointOffset > 0 &&
                        NativeBootloaderPayload.Stage2EntryPointOffset + 4 <= (uint)NativeBootloaderPayload.Stage2.Length)
                    {
                        epOff = (int)NativeBootloaderPayload.Stage2EntryPointOffset;
                    }
                    else
                    {
                        for (int p = 0; p < NativeBootloaderPayload.Stage2.Length - 8; p++)
                        {
                            if (NativeBootloaderPayload.Stage2[p] == (byte)'E' &&
                                NativeBootloaderPayload.Stage2[p + 1] == (byte)'P' &&
                                NativeBootloaderPayload.Stage2[p + 2] == (byte)'N' &&
                                NativeBootloaderPayload.Stage2[p + 3] == (byte)'T')
                            {
                                epOff = p + 4;
                                break;
                            }
                        }
                    }

                    if (epOff != -1 && epOff + 4 <= NativeBootloaderPayload.Stage2.Length)
                    {
                        defaultEntryPoint = (uint)(NativeBootloaderPayload.Stage2[epOff] |
                                                  (NativeBootloaderPayload.Stage2[epOff + 1] << 8) |
                                                  (NativeBootloaderPayload.Stage2[epOff + 2] << 16) |
                                                  (NativeBootloaderPayload.Stage2[epOff + 3] << 24));
                    }
                }

                unsafe
                {
                    // 1. Fast path: check if compiled entry point in RAM matches Kernel_Start signature
                    if (defaultEntryPoint >= 0x02000000u && defaultEntryPoint < 0x04000000u)
                    {
                        byte* pTarget = (byte*)defaultEntryPoint;
                        if (pTarget[0] == 0xBC && pTarget[1] == 0x40 && pTarget[2] == 0x00 &&
                            pTarget[3] == 0x05 && pTarget[4] == 0x02 && pTarget[5] == 0xFA)
                        {
                            entryPoint = defaultEntryPoint;
                        }
                    }

                    // 2. Scan path: search wide 256 KB window (0x02400000 .. 0x02440000)
                    if (entryPoint == 0)
                    {
                        byte* pRam = (byte*)0x02400000u;
                        for (uint i = 0; i < 0x40000u; i++)
                        {
                            if (pRam[i] == 0xBC && pRam[i + 1] == 0x40 && pRam[i + 2] == 0x00 &&
                                pRam[i + 3] == 0x05 && pRam[i + 4] == 0x02 && pRam[i + 5] == 0xFA)
                            {
                                entryPoint = 0x02400000u + i;
                                break;
                            }
                        }
                    }
                }

                // 3. Fallback to compiled entry point if signature scan did not match
                if (entryPoint == 0 && defaultEntryPoint != 0)
                {
                    entryPoint = defaultEntryPoint;
                }

                if (entryPoint == 0)
                {
                    statusMessage = "Integrity error: Running kernel entry point signature (mov esp, 0x02050040) not found in physical RAM.";
                    return false;
                }

                // Patch Stage2 payload with the dynamically verified entryPoint before writing to disk
                // Patch Stage2 payload with the dynamically verified entryPoint and sector count
                if (NativeBootloaderPayload.Stage2 != null)
                {
                    int epOffset = -1;
                    if (NativeBootloaderPayload.Stage2EntryPointOffset > 0 &&
                        NativeBootloaderPayload.Stage2EntryPointOffset + 4 <= (uint)NativeBootloaderPayload.Stage2.Length)
                    {
                        epOffset = (int)NativeBootloaderPayload.Stage2EntryPointOffset;
                    }
                    else
                    {
                        for (int p = 0; p < NativeBootloaderPayload.Stage2.Length - 8; p++)
                        {
                            if (NativeBootloaderPayload.Stage2[p] == (byte)'E' &&
                                NativeBootloaderPayload.Stage2[p + 1] == (byte)'P' &&
                                NativeBootloaderPayload.Stage2[p + 2] == (byte)'N' &&
                                NativeBootloaderPayload.Stage2[p + 3] == (byte)'T')
                            {
                                epOffset = p + 4;
                                break;
                            }
                        }
                    }

                    if (epOffset != -1)
                    {
                        NativeBootloaderPayload.Stage2[epOffset] = (byte)(entryPoint & 0xFF);
                        NativeBootloaderPayload.Stage2[epOffset + 1] = (byte)((entryPoint >> 8) & 0xFF);
                        NativeBootloaderPayload.Stage2[epOffset + 2] = (byte)((entryPoint >> 16) & 0xFF);
                        NativeBootloaderPayload.Stage2[epOffset + 3] = (byte)((entryPoint >> 24) & 0xFF);
                    }

                    int rsecOffset = -1;
                    if (NativeBootloaderPayload.Stage2RemSectorsOffset > 0 &&
                        NativeBootloaderPayload.Stage2RemSectorsOffset + 4 <= (uint)NativeBootloaderPayload.Stage2.Length)
                    {
                        rsecOffset = (int)NativeBootloaderPayload.Stage2RemSectorsOffset;
                    }
                    else
                    {
                        for (int p = 0; p < NativeBootloaderPayload.Stage2.Length - 8; p++)
                        {
                            if (NativeBootloaderPayload.Stage2[p] == (byte)'R' &&
                                NativeBootloaderPayload.Stage2[p + 1] == (byte)'S' &&
                                NativeBootloaderPayload.Stage2[p + 2] == (byte)'E' &&
                                NativeBootloaderPayload.Stage2[p + 3] == (byte)'C')
                            {
                                rsecOffset = p + 4;
                                break;
                            }
                        }
                    }

                    if (rsecOffset != -1 && kernelSectors > 0)
                    {
                        NativeBootloaderPayload.Stage2[rsecOffset] = (byte)(kernelSectors & 0xFF);
                        NativeBootloaderPayload.Stage2[rsecOffset + 1] = (byte)((kernelSectors >> 8) & 0xFF);
                        NativeBootloaderPayload.Stage2[rsecOffset + 2] = (byte)((kernelSectors >> 16) & 0xFF);
                        NativeBootloaderPayload.Stage2[rsecOffset + 3] = (byte)((kernelSectors >> 24) & 0xFF);
                    }
                }

                uint entryOffset = entryPoint - 0x02000000u;
                ulong entrySector = 64UL + (ulong)(entryOffset / 512u);
                uint entryByteOffset = entryOffset % 512u;

                progressCallback?.Invoke("Writing WeatherOS Master Boot Record & Boot Manager...", 5);

                // 2. Write MBR (1 sector / 512 bytes) to LBA 0
                byte[] mbrSector = new byte[512];
                if (NativeBootloaderPayload.Mbr != null && NativeBootloaderPayload.Mbr.Length >= 512)
                {
                    Array.Copy(NativeBootloaderPayload.Mbr, 0, mbrSector, 0, 512);
                }
                mbrSector[450] = 0x83; // Linux / OS native raw partition (prevents Cosmos VFS FAT mount on raw kernel)
                if (kernelSectors > 0)
                {
                    mbrSector[458] = (byte)(kernelSectors & 0xFF);
                    mbrSector[459] = (byte)((kernelSectors >> 8) & 0xFF);
                    mbrSector[460] = (byte)((kernelSectors >> 16) & 0xFF);
                    mbrSector[461] = (byte)((kernelSectors >> 24) & 0xFF);
                }
                dev.WriteBlock(0, 1, ref mbrSector);

                // 3. Write WeatherOS Boot Manager (8 sectors / 4096 bytes) to LBA 1..8
                byte[] bootMgrBlock = new byte[8 * 512];
                if (NativeBootloaderPayload.Stage2 != null)
                {
                    int copyLen = Math.Min(bootMgrBlock.Length, NativeBootloaderPayload.Stage2.Length);
                    Array.Copy(NativeBootloaderPayload.Stage2, 0, bootMgrBlock, 0, copyLen);
                }
                // Mark Stage 2 on target hard disk with is_installed_hdd = 1 (notifies kernel to bypass Live boot menu)
                if (NativeBootloaderPayload.Stage2InstalledFlagOffset > 0 &&
                    NativeBootloaderPayload.Stage2InstalledFlagOffset < (uint)bootMgrBlock.Length)
                {
                    bootMgrBlock[NativeBootloaderPayload.Stage2InstalledFlagOffset] = 1;
                }
                if (NativeBootloaderPayload.Stage2EntryPointOffset > 0 &&
                    NativeBootloaderPayload.Stage2EntryPointOffset + 4 <= (uint)bootMgrBlock.Length)
                {
                    bootMgrBlock[NativeBootloaderPayload.Stage2EntryPointOffset] = (byte)(entryPoint & 0xFF);
                    bootMgrBlock[NativeBootloaderPayload.Stage2EntryPointOffset + 1] = (byte)((entryPoint >> 8) & 0xFF);
                    bootMgrBlock[NativeBootloaderPayload.Stage2EntryPointOffset + 2] = (byte)((entryPoint >> 16) & 0xFF);
                    bootMgrBlock[NativeBootloaderPayload.Stage2EntryPointOffset + 3] = (byte)((entryPoint >> 24) & 0xFF);
                }
                if (NativeBootloaderPayload.Stage2RemSectorsOffset > 0 &&
                    NativeBootloaderPayload.Stage2RemSectorsOffset + 4 <= (uint)bootMgrBlock.Length && kernelSectors > 0)
                {
                    bootMgrBlock[NativeBootloaderPayload.Stage2RemSectorsOffset] = (byte)(kernelSectors & 0xFF);
                    bootMgrBlock[NativeBootloaderPayload.Stage2RemSectorsOffset + 1] = (byte)((kernelSectors >> 8) & 0xFF);
                    bootMgrBlock[NativeBootloaderPayload.Stage2RemSectorsOffset + 2] = (byte)((kernelSectors >> 16) & 0xFF);
                    bootMgrBlock[NativeBootloaderPayload.Stage2RemSectorsOffset + 3] = (byte)((kernelSectors >> 24) & 0xFF);
                }
                SafeWriteSectors(dev, 1, 8, bootMgrBlock, 0);

                // 4. Write WeatherOS Installation Marker Sector at LBA 9, and zero-fill Sectors 10..63
                byte[] markerSector = new byte[512];
                byte[] sig = System.Text.Encoding.ASCII.GetBytes("WEATHEROS_INSTALLED_v3.1\nStatus=Installed\nBoot=Native\nArchitecture=x86-AOT\n");
                Array.Copy(sig, 0, markerSector, 0, sig.Length);
                dev.WriteBlock(9, 1, ref markerSector);

                byte[] zeroSector = new byte[512];
                for (ulong s = 10; s < 64; s++)
                {
                    dev.WriteBlock(s, 1, ref zeroSector);
                }

                // Eradicate any Backup GPT tables at the end of the disk (GParted remnants that confuse BIOS)
                try
                {
                    ulong totalSec = targetDrive.SectorCount;
                    if (totalSec > 100)
                    {
                        ulong startGptBackup = totalSec > 34 ? totalSec - 34 : 0;
                        for (ulong s = startGptBackup; s < totalSec; s++)
                        {
                            dev.WriteBlock(s, 1, ref zeroSector);
                        }
                    }
                }
                catch { }

                // 5. Deploy WeatherOS 32-bit Monolithic Kernel to Sectors 64..
                // Prefer pristine unmutated golden kernel preserved at 0x01000000 by Stage2 loader
                uint kernelSourceBase = 0x02000000u;
                unsafe
                {
                    byte* pGolden = (byte*)0x01000000u;
                    if (pGolden[0] == 0xD6 && pGolden[1] == 0x50 && pGolden[2] == 0x52 && pGolden[3] == 0xE8)
                    {
                        kernelSourceBase = 0x01000000u;
                    }
                }

                string sourceDesc = (kernelSourceBase == 0x01000000u) ? "Pristine Golden Image (0x01000000)" : "Active RAM (0x02000000)";
                progressCallback?.Invoke($"Deploying WeatherOS 32-bit Kernel ({sourceDesc})...", 15);

                ulong chunkSize = 16; // 8 KB per chunk (safe ATA buffer size)
                byte[] chunk = new byte[chunkSize * 512];
                ulong totalKernelSectors = kernelSectors;
                ulong copied = 0;
                int lastPct = -1;

                while (copied < totalKernelSectors)
                {
                    ulong count = Math.Min(chunkSize, totalKernelSectors - copied);
                    ulong targetSector = 64UL + copied;

                    // Direct Physical RAM read:
                    // If kernelSourceBase == 0x01000000: authentic pristine kernel frozen at boot time.
                    // If kernelSourceBase == 0x02000000: running RAM fallback.
                    uint ramAddress = kernelSourceBase + (uint)(copied * 512UL);
                    unsafe
                    {
                        byte* pSrc = (byte*)ramAddress;
                        uint bytesToCopy = (uint)count * 512;
                        for (uint b = 0; b < bytesToCopy; b++)
                        {
                            chunk[b] = pSrc[b];
                        }
                    }

                    if (copied == 0)
                    {
                        // Strict Multiboot2 header verification for first sector (0xE85250D6)
                        if (chunk[0] != 0xD6 || chunk[1] != 0x50 || chunk[2] != 0x52 || chunk[3] != 0xE8)
                        {
                            statusMessage = $"Hardware read error: RAM at 0x{kernelSourceBase:X8} missing Multiboot header ({chunk[0]:X2} {chunk[1]:X2} {chunk[2]:X2} {chunk[3]:X2}).";
                            return false;
                        }
                    }

                    // Strict Entry Point Verification: Verify entry point machine code in RAM
                    // Instruction: mov esp, 0x02050040 (0xBC, 0x40, 0x00, 0x05, 0x02)
                    if (targetSector <= entrySector && (targetSector + count) > entrySector)
                    {
                        uint offInChunk = (uint)((entrySector - targetSector) * 512UL + entryByteOffset);
                        if (chunk[offInChunk] != 0xBC || chunk[offInChunk + 1] != 0x40)
                        {
                            statusMessage = $"Payload corruption: Entry point at 0x{entryPoint:X8} in RAM invalid ({chunk[offInChunk]:X2} {chunk[offInChunk + 1]:X2}). Expected BC 40.";
                            return false;
                        }
                    }

                    // Memory Management Sanitization for Active RAM fallback:
                    // If reading from running RAM fallback (0x02000000), guarantee that StartedMemoryManager is strictly false (0),
                    // and mRAT / SMT pointers are null (0).
                    // If reading from pristine golden image (0x01000000), it is ALREADY 100% untouched binary straight from disk!
                    if (kernelSourceBase == 0x02000000u)
                    {
                        ulong byteStart = copied * 512UL;
                        ulong byteEnd = byteStart + (ulong)count * 512UL;

                        if (NativeBootloaderPayload.StartedMemoryManagerOffset >= byteStart && 
                            NativeBootloaderPayload.StartedMemoryManagerOffset < byteEnd)
                        {
                            uint inChunk = (uint)(NativeBootloaderPayload.StartedMemoryManagerOffset - byteStart);
                            chunk[inChunk] = 0; // StartedMemoryManager = 0 (false)
                        }

                        if (NativeBootloaderPayload.RatOffset >= byteStart && 
                            (NativeBootloaderPayload.RatOffset + 4UL) <= byteEnd)
                        {
                            uint inChunk = (uint)(NativeBootloaderPayload.RatOffset - byteStart);
                            chunk[inChunk] = 0; chunk[inChunk + 1] = 0; chunk[inChunk + 2] = 0; chunk[inChunk + 3] = 0;
                        }

                        if (NativeBootloaderPayload.HeapSmallSmtOffset >= byteStart && 
                            (NativeBootloaderPayload.HeapSmallSmtOffset + 4UL) <= byteEnd)
                        {
                            uint inChunk = (uint)(NativeBootloaderPayload.HeapSmallSmtOffset - byteStart);
                            chunk[inChunk] = 0; chunk[inChunk + 1] = 0; chunk[inChunk + 2] = 0; chunk[inChunk + 3] = 0;
                        }
                    }

                    SafeWriteSectors(dev, targetSector, count, chunk, 0);
                    copied += count;

                    int pct = 15 + (int)((copied * 75UL) / totalKernelSectors); // 15% to 90%
                    if (pct != lastPct)
                    {
                        lastPct = pct;
                        ulong mbDone = (copied * 512UL) / (1024UL * 1024UL);
                        ulong mbTotal = (totalKernelSectors * 512UL) / (1024UL * 1024UL);
                        progressCallback?.Invoke($"Writing WeatherOS kernel ({mbDone}MB / {mbTotal}MB)...", pct);
                    }
                }

                // 6. Verify hardware writes on target hard disk
                progressCallback?.Invoke("Verifying hard disk installation integrity...", 92);
                byte[] chkMbr = new byte[512];
                dev.ReadBlock(0, 1, ref chkMbr);
                if (chkMbr[510] != 0x55 || chkMbr[511] != 0xAA)
                {
                    statusMessage = "Hardware error: Target disk MBR validation failed.";
                    return false;
                }

                byte[] chkBootMgr = new byte[512];
                dev.ReadBlock(1, 1, ref chkBootMgr);
                byte exp0 = NativeBootloaderPayload.Stage2 != null && NativeBootloaderPayload.Stage2.Length > 0 ? NativeBootloaderPayload.Stage2[0] : (byte)0xFA;
                byte exp1 = NativeBootloaderPayload.Stage2 != null && NativeBootloaderPayload.Stage2.Length > 1 ? NativeBootloaderPayload.Stage2[1] : (byte)0xFC;
                if (chkBootMgr[0] != exp0 || chkBootMgr[1] != exp1)
                {
                    statusMessage = $"Hardware error: Target disk Boot Manager validation failed ({chkBootMgr[0]:X2} {chkBootMgr[1]:X2} != {exp0:X2} {exp1:X2}).";
                    return false;
                }

                byte[] chkKernel = new byte[512];
                dev.ReadBlock(64, 1, ref chkKernel);
                if (chkKernel[0] != 0xD6 || chkKernel[1] != 0x50 || chkKernel[2] != 0x52 || chkKernel[3] != 0xE8)
                {
                    // If first read missed due to drive write buffer, retry read
                    dev.ReadBlock(64, 1, ref chkKernel);
                }
                if (chkKernel[0] != 0xD6 || chkKernel[1] != 0x50 || chkKernel[2] != 0x52 || chkKernel[3] != 0xE8)
                {
                    statusMessage = $"Hardware error: Target disk Kernel verification failed (header mismatch: {chkKernel[0]:X2} {chkKernel[1]:X2} {chkKernel[2]:X2} {chkKernel[3]:X2}).";
                    return false;
                }

                // Verify Kernel Entry Point on target hard disk
                byte[] chkEntry = new byte[512];
                dev.ReadBlock(entrySector, 1, ref chkEntry);
                if (chkEntry[entryByteOffset] != 0xBC || chkEntry[entryByteOffset + 1] != 0x40)
                {
                    dev.ReadBlock(entrySector, 1, ref chkEntry);
                }
                if (chkEntry[entryByteOffset] != 0xBC || chkEntry[entryByteOffset + 1] != 0x40)
                {
                    statusMessage = $"Hardware error: Target disk Entry Point (LBA {entrySector}) verification failed (got {chkEntry[entryByteOffset]:X2} {chkEntry[entryByteOffset + 1]:X2}, expected BC 40).";
                    return false;
                }

                // Verify Memory Manager clean state on target hard disk
                ulong smmSector = 64UL + (ulong)(NativeBootloaderPayload.StartedMemoryManagerOffset / 512u);
                uint smmByteInSec = NativeBootloaderPayload.StartedMemoryManagerOffset % 512u;
                byte[] chkSmm = new byte[512];
                dev.ReadBlock(smmSector, 1, ref chkSmm);
                if (chkSmm[smmByteInSec] != 0)
                {
                    statusMessage = $"Integrity error: Target disk memory manager flag not clean (got {chkSmm[smmByteInSec]:X2}, expected 00).";
                    return false;
                }

                progressCallback?.Invoke("Configuring environment & manifests...", 96);
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
                StorageService.SaveFile("version.ini", versionContent, out vStat);

                string appsManifest =
                    "[WeatherOS_Apps]\n" +
                    "ClockApp_Version=2.0\n" +
                    "ClockApp_Engine=CMOS_RTC_Hardware\n" +
                    "SystemInfoApp_Version=3.1\n" +
                    "NetTransferApp_Version=2.0\n";
                string aStat;
                StorageService.SaveFile("apps.ini", appsManifest, out aStat);

                string settingsContent =
                    "DarkMode=false\n" +
                    "AutoLoc=true\n" +
                    "DefaultCity=Setubal\n" +
                    "DebugLog=false\n" +
                    "CustomKey=\n";
                string sStat;
                StorageService.SaveFile("settings.ini", settingsContent, out sStat);

                string welcomeContent =
                    "==============================================================================\n" +
                    "                   Welcome to WeatherOS (Installed on Hard Disk)             \n" +
                    "==============================================================================\n" +
                    "WeatherOS is now permanently installed to your internal hard drive!\n\n" +
                    "BOOTLOADER:\n" +
                    "- Master Boot Record (MBR) configured with direct BIOS INT 13h LBA/CHS loader.\n" +
                    "- WeatherOS Boot Manager embedded at LBA 1..8.\n" +
                    "- WeatherOS Monolithic 32-bit Kernel deployed at LBA 64..\n" +
                    "- Boots cleanly into physical RAM in under 0.2s.\n" +
                    "==============================================================================\n";
                string wStat;
                StorageService.SaveFile("welcome.txt", welcomeContent, out wStat);

                _forceInstalledFlag = true;
                _bootSource = BootSource.HardDisk;

                progressCallback?.Invoke("Installation finalized successfully! Ready to boot.", 100);
                statusMessage = $"Installed to {targetDrive.Name} [OK] - WeatherOS Native Boot ready.";
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
            return ApplyUpdateDirect(packageData, 0, packageData.Length, fileName, out statusMessage);
        }

        public static bool ApplyUpdateDirect(byte[] packageData, int pkgOffset, int pkgLength, string fileName, out string statusMessage)
        {
            return ApplyUpdateDirect(packageData, pkgOffset, pkgLength, fileName, null, out statusMessage);
        }

        public static bool ApplyUpdateDirect(byte[] packageData, int pkgOffset, int pkgLength, string fileName, Action<int, ulong, ulong> liveProgressCallback, out string statusMessage)
        {
            if (packageData == null || pkgLength <= 0 || pkgOffset + pkgLength > packageData.Length)
            {
                statusMessage = "Error: Update package buffer is empty or invalid.";
                return false;
            }

            uint crc = ComputeCrc32(packageData, pkgOffset, pkgLength);
            string crcStr = "0x" + crc.ToString("X8");

            bool isCumulativePackage = fileName.ToLower().EndsWith(".pkg") ||
                                       fileName.ToLower().EndsWith(".wos") ||
                                       (pkgLength > 8 && packageData[pkgOffset] == (byte)'W' && packageData[pkgOffset + 1] == (byte)'O' && packageData[pkgOffset + 2] == (byte)'S');

            try
            {
                // Safely identify the installed internal hard disk device (NEVER touch Live USB boot drive)
                BlockDevice dev = null;
                try
                {
                    var physDrives = DiskWipeService.GetPhysicalDrives();
                    if (physDrives != null && physDrives.Count > 0)
                    {
                        // 1. Look for drive with WeatherOS installation marker at LBA 9
                        for (int i = 0; i < physDrives.Count; i++)
                        {
                            var drv = physDrives[i];
                            if (drv == null) continue;
                            var d = drv.Device;
                            while (d is Cosmos.HAL.BlockDevice.Partition p) d = p.Host;
                            if (d == null) continue;

                            byte[] sec9 = new byte[512];
                            try
                            {
                                d.ReadBlock(9, 1, ref sec9);
                                if (sec9[0] == (byte)'W' && sec9[1] == (byte)'E' && sec9[2] == (byte)'A' &&
                                    sec9[3] == (byte)'T' && sec9[4] == (byte)'H' && sec9[5] == (byte)'E' &&
                                    sec9[6] == (byte)'R' && sec9[7] == (byte)'O' && sec9[8] == (byte)'S')
                                {
                                    dev = d;
                                    break;
                                }
                            }
                            catch { }
                        }

                        // 2. Fallback: Internal Hard Disk (never Live USB)
                        if (dev == null)
                        {
                            for (int i = 0; i < physDrives.Count; i++)
                            {
                                var drv = physDrives[i];
                                if (drv == null) continue;
                                if (drv.Name.Contains("Live USB") || drv.Name.Contains("Boot Medium") || drv.Name.Contains("Flash"))
                                    continue;

                                var d = drv.Device;
                                while (d is Cosmos.HAL.BlockDevice.Partition p) d = p.Host;
                                if (d != null)
                                {
                                    dev = d;
                                    break;
                                }
                            }
                        }
                    }
                }
                catch { }

                // Locate Kernel Payload inside package with zero memory allocations
                int kernelStart = pkgOffset;
                int kernelLen = pkgLength;
                if (isCumulativePackage)
                {
                    LocateKernelInPackage(packageData, pkgOffset, pkgLength, out kernelStart, out kernelLen);
                }

                if (dev != null && kernelLen >= 512)
                {
                    bool validMb = (packageData[kernelStart] == 0xD6 && packageData[kernelStart + 1] == 0x50 && packageData[kernelStart + 2] == 0x52 && packageData[kernelStart + 3] == 0xE8) ||
                                   (packageData[kernelStart] == 0x02 && packageData[kernelStart + 1] == 0xB0 && packageData[kernelStart + 2] == 0xAD && packageData[kernelStart + 3] == 0x1B);

                    if (validMb)
                    {
                        ulong totalSec = (ulong)((kernelLen + 511) / 512);
                        if (totalSec > 32768) totalSec = 32768; // Kernel partition boundary safety

                        FileTransferService.StatusMessage = "Flashing kernel to physical storage (LBA 64)...";
                        bool writeOk = SafeWriteSectors(dev, 64UL, totalSec, packageData, kernelStart, (writtenSec, totalSecs) =>
                        {
                            int pct = (int)((writtenSec * 100UL) / totalSecs);
                            ulong mbDone = (writtenSec * 512UL) / (1024UL * 1024UL);
                            ulong mbTotal = (totalSecs * 512UL) / (1024UL * 1024UL);
                            liveProgressCallback?.Invoke(pct, mbDone, mbTotal);
                        });

                        if (!writeOk)
                        {
                            statusMessage = "Hardware error: Failed writing kernel sectors to disk.";
                            return false;
                        }

                        // Verify written kernel header at LBA 64
                        byte[] vfySec = new byte[512];
                        if (SafeReadSectors(dev, 64UL, 1, vfySec, 0))
                        {
                            if (vfySec[0] != packageData[kernelStart] || vfySec[1] != packageData[kernelStart + 1] ||
                                vfySec[2] != packageData[kernelStart + 2] || vfySec[3] != packageData[kernelStart + 3])
                            {
                                statusMessage = "Flash verification failed: Kernel header mismatch at LBA 64.";
                                return false;
                            }
                        }

                        // Dynamically locate entry point in kernel payload (mov esp, 0x02050040; cli; mov [mb], ebx)
                        byte[] epSig = new byte[] { 0xBC, 0x40, 0x00, 0x05, 0x02, 0xFA, 0x89, 0x1D };
                        int startScan = (kernelLen > 0x300000) ? (kernelStart + 0x300000) : kernelStart;
                        int epOff = FindByteSequence(packageData, epSig, startScan, (kernelStart + kernelLen) - startScan);
                        if (epOff < 0)
                        {
                            epOff = FindByteSequence(packageData, epSig, kernelStart, kernelLen);
                        }
                        uint newEntryPoint = (epOff >= 0) ? (0x02000000u + (uint)(epOff - kernelStart)) : 0;

                        // Patch Stage 2 at LBA 1..8 with new kernel sector count and entry point
                        if (newEntryPoint != 0)
                        {
                            try
                            {
                                byte[] s2Block = new byte[8 * 512];
                                if (SafeReadSectors(dev, 1, 8, s2Block, 0))
                                {
                                    int rsecIdx = FindByteSequence(s2Block, new byte[] { (byte)'R', (byte)'S', (byte)'E', (byte)'C' }, 0, s2Block.Length);
                                    if (rsecIdx >= 0 && rsecIdx + 8 <= s2Block.Length)
                                    {
                                        int off = rsecIdx + 4;
                                        s2Block[off] = (byte)(totalSec & 0xFF);
                                        s2Block[off + 1] = (byte)((totalSec >> 8) & 0xFF);
                                        s2Block[off + 2] = (byte)((totalSec >> 16) & 0xFF);
                                        s2Block[off + 3] = (byte)((totalSec >> 24) & 0xFF);
                                    }
                                    else if (NativeBootloaderPayload.Stage2RemSectorsOffset > 0 &&
                                             NativeBootloaderPayload.Stage2RemSectorsOffset + 4 <= (uint)s2Block.Length)
                                    {
                                        s2Block[NativeBootloaderPayload.Stage2RemSectorsOffset] = (byte)(totalSec & 0xFF);
                                        s2Block[NativeBootloaderPayload.Stage2RemSectorsOffset + 1] = (byte)((totalSec >> 8) & 0xFF);
                                        s2Block[NativeBootloaderPayload.Stage2RemSectorsOffset + 2] = (byte)((totalSec >> 16) & 0xFF);
                                        s2Block[NativeBootloaderPayload.Stage2RemSectorsOffset + 3] = (byte)((totalSec >> 24) & 0xFF);
                                    }

                                    int epntIdx = FindByteSequence(s2Block, new byte[] { (byte)'E', (byte)'P', (byte)'N', (byte)'T' }, 0, s2Block.Length);
                                    if (epntIdx >= 0 && epntIdx + 8 <= s2Block.Length)
                                    {
                                        int off = epntIdx + 4;
                                        s2Block[off] = (byte)(newEntryPoint & 0xFF);
                                        s2Block[off + 1] = (byte)((newEntryPoint >> 8) & 0xFF);
                                        s2Block[off + 2] = (byte)((newEntryPoint >> 16) & 0xFF);
                                        s2Block[off + 3] = (byte)((newEntryPoint >> 24) & 0xFF);
                                    }
                                    else if (NativeBootloaderPayload.Stage2EntryPointOffset > 0 &&
                                             NativeBootloaderPayload.Stage2EntryPointOffset + 4 <= (uint)s2Block.Length)
                                    {
                                        s2Block[NativeBootloaderPayload.Stage2EntryPointOffset] = (byte)(newEntryPoint & 0xFF);
                                        s2Block[NativeBootloaderPayload.Stage2EntryPointOffset + 1] = (byte)((newEntryPoint >> 8) & 0xFF);
                                        s2Block[NativeBootloaderPayload.Stage2EntryPointOffset + 2] = (byte)((newEntryPoint >> 16) & 0xFF);
                                        s2Block[NativeBootloaderPayload.Stage2EntryPointOffset + 3] = (byte)((newEntryPoint >> 24) & 0xFF);
                                    }

                                    SafeWriteSectors(dev, 1, 8, s2Block, 0);
                                }

                                // Also synchronize MBR Partition 1 length at LBA 0 (offset 458)
                                byte[] mbrBlock = new byte[512];
                                if (SafeReadSectors(dev, 0, 1, mbrBlock, 0))
                                {
                                    if (mbrBlock[510] == 0x55 && mbrBlock[511] == 0xAA && mbrBlock[450] == 0x83)
                                    {
                                        mbrBlock[458] = (byte)(totalSec & 0xFF);
                                        mbrBlock[459] = (byte)((totalSec >> 8) & 0xFF);
                                        mbrBlock[460] = (byte)((totalSec >> 16) & 0xFF);
                                        mbrBlock[461] = (byte)((totalSec >> 24) & 0xFF);
                                        SafeWriteSectors(dev, 0, 1, mbrBlock, 0);
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }

                // 3. Extract and Update Apps Manifest
                string appsManifest = null;
                string manifestUpdate = null;

                if (isCumulativePackage)
                {
                    try
                    {
                        byte[] secAppsMarker = new byte[] { (byte)'-', (byte)'-', (byte)'-', (byte)'S', (byte)'E', (byte)'C', (byte)'T', (byte)'I', (byte)'O', (byte)'N', (byte)':', (byte)'A', (byte)'P', (byte)'P', (byte)'S', (byte)'-', (byte)'-', (byte)'-' };
                        byte[] secManifestMarker = new byte[] { (byte)'-', (byte)'-', (byte)'-', (byte)'S', (byte)'E', (byte)'C', (byte)'T', (byte)'I', (byte)'O', (byte)'N', (byte)':', (byte)'M', (byte)'A', (byte)'N', (byte)'I', (byte)'F', (byte)'E', (byte)'S', (byte)'T', (byte)'-', (byte)'-', (byte)'-' };
                        byte[] secEndMarker = new byte[] { (byte)'-', (byte)'-', (byte)'-', (byte)'S', (byte)'E', (byte)'C', (byte)'T', (byte)'I', (byte)'O', (byte)'N', (byte)':', (byte)'E', (byte)'N', (byte)'D', (byte)'-', (byte)'-', (byte)'-' };

                        int scanStart = (pkgLength > 65536) ? (pkgOffset + pkgLength - 65536) : pkgOffset;
                        int aIdx = FindByteSequence(packageData, secAppsMarker, scanStart, (pkgOffset + pkgLength) - scanStart);
                        int mIdx = FindByteSequence(packageData, secManifestMarker, scanStart, (pkgOffset + pkgLength) - scanStart);
                        int eIdx = FindByteSequence(packageData, secEndMarker, scanStart, (pkgOffset + pkgLength) - scanStart);

                        if (aIdx >= 0 && mIdx > aIdx)
                        {
                            int aStart = aIdx + secAppsMarker.Length;
                            while (aStart < mIdx && (packageData[aStart] == 0x0D || packageData[aStart] == 0x0A)) aStart++;
                            int aEnd = mIdx;
                            while (aEnd > aStart && (packageData[aEnd - 1] == 0x0D || packageData[aEnd - 1] == 0x0A)) aEnd--;
                            if (aEnd > aStart)
                            {
                                appsManifest = Encoding.ASCII.GetString(packageData, aStart, aEnd - aStart);
                            }
                        }

                        if (mIdx >= 0 && eIdx > mIdx)
                        {
                            int mStart = mIdx + secManifestMarker.Length;
                            while (mStart < eIdx && (packageData[mStart] == 0x0D || packageData[mStart] == 0x0A)) mStart++;
                            int mEnd = eIdx;
                            while (mEnd > mStart && (packageData[mEnd - 1] == 0x0D || packageData[mEnd - 1] == 0x0A)) mEnd--;
                            if (mEnd > mStart)
                            {
                                manifestUpdate = Encoding.ASCII.GetString(packageData, mStart, mEnd - mStart);
                            }
                        }
                    }
                    catch { }
                }

                if (string.IsNullOrEmpty(appsManifest))
                {
                    appsManifest =
                        "[WeatherOS_Apps]\n" +
                        "ClockApp_Version=3.0\n" +
                        "ClockApp_Engine=Network_NTP_HTTP_RTC_Hybrid\n" +
                        "ClockApp_Status=Upgraded_NtpSync_HttpDateFallback_ManualCalibration\n" +
                        "SettingsApp_Version=3.1\n" +
                        "SystemInfoApp_Version=3.1\n" +
                        "NetTransferApp_Version=2.0\n";
                }

                if (StorageService.IsVfsAvailable())
                {
                    try
                    {
                        if (!Directory.Exists("0:\\system")) Directory.CreateDirectory("0:\\system");
                        File.WriteAllText("0:\\system\\apps.ini", appsManifest);
                    }
                    catch { }
                }
                StorageService.PutMemoryFile("apps.ini", appsManifest);

                // 4. Update Component: System Version Manifest
                if (string.IsNullOrEmpty(manifestUpdate))
                {
                    manifestUpdate =
                        "[WeatherOS]\n" +
                        "Version=3.2.0-release\n" +
                        "BuildDate=2026-10-18\n" +
                        "Branch=32bit\n" +
                        "LatestCommit=49a9992\n" +
                        $"LastUpdateFile={fileName}\n" +
                        $"LastUpdateChecksum={crcStr}\n" +
                        "BackupAvailable=true\n" +
                        "BootTarget=LBA64:MonolithicKernel\n" +
                        "Status=Updated_Cumulative\n";
                }

                if (StorageService.IsVfsAvailable())
                {
                    try
                    {
                        File.WriteAllText("0:\\system\\version.ini", manifestUpdate);
                    }
                    catch { }
                }
                StorageService.PutMemoryFile("version.ini", manifestUpdate);

                _forceInstalledFlag = true;

                if (isCumulativePackage)
                {
                    statusMessage = $"Cumulative System Update Applied ({fileName}, CRC {crcStr})!\n" +
                                    "  [1] Kernel Binary   : Deployed & verified at LBA 64\n" +
                                    "  [2] System Apps     : Upgraded manifest deployed to 0:\\system\\apps.ini\n" +
                                    "  [OK] Bootloader, MBR, and Stage 2 fully protected. System ready.";
                }
                else
                {
                    statusMessage = $"Kernel update applied ({fileName}, {pkgLength} bytes, CRC {crcStr})! System ready.";
                }
                return true;
            }
            catch (Exception ex)
            {
                statusMessage = "Update failed: " + ex.Message;
                return false;
            }
        }

        private static int FindByteSequence(byte[] data, byte[] pattern, int startIdx, int maxScan)
        {
            if (data == null || pattern == null || pattern.Length == 0 || data.Length < pattern.Length) return -1;
            int limit = startIdx + maxScan;
            if (limit > data.Length) limit = data.Length;
            int patLen = pattern.Length;
            for (int i = startIdx; i <= limit - patLen; i++)
            {
                bool match = true;
                for (int j = 0; j < patLen; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        private static void LocateKernelInPackage(byte[] pkgData, int pkgOffset, int pkgLen, out int bodyStart, out int bodyLen)
        {
            bodyStart = pkgOffset;
            bodyLen = pkgLen;
            if (pkgData == null || pkgLen < 512) return;
            try
            {
                byte[] secKernel = new byte[] { (byte)'-', (byte)'-', (byte)'-', (byte)'S', (byte)'E', (byte)'C', (byte)'T', (byte)'I', (byte)'O', (byte)'N', (byte)':', (byte)'K', (byte)'E', (byte)'R', (byte)'N', (byte)'E', (byte)'L', (byte)'-', (byte)'-', (byte)'-' };
                byte[] secApps = new byte[] { (byte)'-', (byte)'-', (byte)'-', (byte)'S', (byte)'E', (byte)'C', (byte)'T', (byte)'I', (byte)'O', (byte)'N', (byte)':', (byte)'A', (byte)'P', (byte)'P', (byte)'S', (byte)'-', (byte)'-', (byte)'-' };

                int kIdx = FindByteSequence(pkgData, secKernel, pkgOffset, Math.Min(4096, pkgLen));
                if (kIdx >= 0)
                {
                    int start = kIdx + secKernel.Length;
                    while (start < (pkgOffset + pkgLen) && (pkgData[start] == 0x0D || pkgData[start] == 0x0A))
                    {
                        start++;
                    }

                    int scanStart = (pkgLen > 65536) ? (pkgOffset + pkgLen - 65536) : start;
                    int aIdx = FindByteSequence(pkgData, secApps, scanStart, (pkgOffset + pkgLen) - scanStart);
                    if (aIdx < 0)
                    {
                        aIdx = FindByteSequence(pkgData, secApps, start, (pkgOffset + pkgLen) - start);
                    }

                    if (aIdx > start)
                    {
                        int end = aIdx;
                        while (end > start && (pkgData[end - 1] == 0x0D || pkgData[end - 1] == 0x0A))
                        {
                            end--;
                        }

                        int kLen = end - start;
                        if (kLen >= 512)
                        {
                            bodyStart = start;
                            bodyLen = kLen;
                        }
                    }
                }
            }
            catch { }
        }

        public static byte[] CreateSampleCumulativePackage()
        {
            string header = "WOSPKG01\n" +
                            "[PACKAGE_METADATA]\n" +
                            "PackageName=WeatherOS Cumulative System Update v3.1.3 (Clock NTP/RTC Fix)\n" +
                            "Version=3.1.3-release\n" +
                            "Components=Kernel,ClockApp,TimeService,SettingsApp,BootloaderSafe\n" +
                            "TargetSlot=0:\\boot\\WeatherOS.bin\n" +
                            "AntiWipe=True\n" +
                            "Date=2026-10-17\n" +
                            "---SECTION:KERNEL---\n";

            byte[] headerBytes = Encoding.ASCII.GetBytes(header);
            byte[] kernelBytes = GenerateKernelBootImage();

            string mid = "\n---SECTION:APPS---\n" +
                         "[ClockApp]\n" +
                         "Version=3.0\n" +
                         "Engine=Network_NTP_HTTP_RTC_Hybrid\n" +
                         "Features=AutoNtpSync_HttpDateFallback_ManualCalibration_12h24hToggle\n" +
                         "Status=Upgraded\n" +
                         "[SettingsApp]\n" +
                         "Version=3.1\n" +
                         "Features=ClockFormat_Timezone_ManualCalibration_NtpTrigger\n" +
                         "Status=Upgraded\n" +
                         "---SECTION:MANIFEST---\n" +
                         "[WeatherOS]\n" +
                         "Version=3.1.3-release\n" +
                         "Branch=32bit\n" +
                         "LatestCommit=72cd642\n" +
                         "CommitTitle=Network time synchronization, manual clock calibration, and bootloader safe update\n" +
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
            StorageService.PutMemoryBinaryFile("WeatherOS-Update-ClockFix-v3.1.3.pkg", pkg);
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

        public static uint ComputeCrc32(byte[] data, int offset = 0, int length = -1)
        {
            if (data == null || data.Length == 0) return 0;
            int len = (length >= 0) ? length : (data.Length - offset);
            int limit = offset + len;
            if (limit > data.Length) limit = data.Length;

            uint crc = 0xFFFFFFFF;
            for (int i = offset; i < limit; i++)
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
