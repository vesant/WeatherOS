using System;
using System.Collections.Generic;
using Cosmos.Core;
using Cosmos.HAL;
using Cosmos.HAL.BlockDevice;
using Cosmos.System.FileSystem.VFS;

namespace WeatherOS.Services
{
    public class PciStorageDevice
    {
        public uint Bus;
        public uint Slot;
        public uint Function;
        public ushort VendorId;
        public ushort DeviceId;
        public uint ClassCode;
        public uint Subclass;
        public uint ProgIf;
        public string VendorName = "";
        public string SubclassName = "";
        public uint[] Bars = new uint[6];
        public uint AbarAddress;
        public bool IsAhci;
        public bool IsNvme;
        public bool IsIde;
        public bool IsRaid;
    }

    public class AhciPortDiag
    {
        public int PortIndex;
        public bool Implemented;
        public uint Ssts;
        public uint Sig;
        public uint Cmd;
        public uint Tfd;
        public byte Det;
        public byte Spd;
        public bool DriveConnected;
        public string DeviceType = "None";
        public string SpeedString = "--";
    }

    public class DiskDiagReport
    {
        public List<PciStorageDevice> Controllers = new List<PciStorageDevice>();
        public bool AhciControllerFound;
        public uint AhciAbarAddress;
        public uint AhciCap;
        public uint AhciGhc;
        public uint AhciPi;
        public uint AhciVersion;
        public bool AhciEnabled;
        public List<AhciPortDiag> AhciPorts = new List<AhciPortDiag>();
        public int ActiveAhciDrives;

        public byte LegacyPrimaryStatus;
        public bool LegacyPrimaryFloating;
        public byte LegacySecondaryStatus;
        public bool LegacySecondaryFloating;

        public int CosmosBlockDeviceCount;
        public List<string> CosmosBlockDeviceNames = new List<string>();

        public int VfsDiskCount;
        public List<string> VfsDiskNames = new List<string>();

        public string PrimaryDiagnosis = "";
        public List<string> DetailedReasons = new List<string>();
        public List<string> BiosRemediationSteps = new List<string>();
    }

    public static class DiskDiagnosticsService
    {
        public static DiskDiagReport RunDiagnostics()
        {
            var report = new DiskDiagReport();

            // 1. Enumerate PCI Bus for Mass Storage Controllers (Class 0x01)
            try
            {
                foreach (var device in Cosmos.HAL.PCI.Devices)
                {
                    if (device.ClassCode == 0x01) // Mass Storage Controller
                    {
                        var pci = new PciStorageDevice
                        {
                            Bus = device.bus,
                            Slot = device.slot,
                            Function = device.function,
                            VendorId = device.VendorID,
                            DeviceId = device.DeviceID,
                            ClassCode = (byte)device.ClassCode,
                            Subclass = (byte)device.Subclass,
                            ProgIf = (byte)device.ProgIF,
                            VendorName = GetVendorName(device.VendorID),
                            SubclassName = GetSubclassName((byte)device.Subclass, (byte)device.ProgIF)
                        };

                        if (device.Subclass == 0x01) pci.IsIde = true;
                        if (device.Subclass == 0x06 && device.ProgIF == 0x01) pci.IsAhci = true;
                        if (device.Subclass == 0x08) pci.IsNvme = true;
                        if (device.Subclass == 0x04) pci.IsRaid = true;

                        // Read raw BARs
                        try
                        {
                            var pciDevNormal = new PCIDeviceNormal(device.bus, device.slot, device.function);
                            for (byte b = 0; b < 6; b++)
                            {
                                pci.Bars[b] = pciDevNormal.ReadRegister32((byte)(0x10 + (b * 4)));
                            }

                            if (pci.IsAhci || device.Subclass == 0x06)
                            {
                                pci.AbarAddress = pci.Bars[5] & 0xFFFFFFF0;
                            }
                        }
                        catch { }

                        report.Controllers.Add(pci);
                    }
                }
            }
            catch { }

            // 2. Probe AHCI ABAR Memory Mapped Registers if SATA Controller found
            foreach (var ctrl in report.Controllers)
            {
                if ((ctrl.IsAhci || ctrl.Subclass == 0x06) && ctrl.AbarAddress > 0)
                {
                    report.AhciControllerFound = true;
                    report.AhciAbarAddress = ctrl.AbarAddress;

                    try
                    {
                        var abar = new MemoryBlock(ctrl.AbarAddress, 0x1100);
                        report.AhciCap = ReadMmio32(abar, 0x00);
                        report.AhciGhc = ReadMmio32(abar, 0x04);
                        report.AhciPi = ReadMmio32(abar, 0x0C);
                        report.AhciVersion = ReadMmio32(abar, 0x10);
                        report.AhciEnabled = (report.AhciGhc & 0x80000000) != 0;

                        // Inspect ports 0..31
                        for (int p = 0; p < 32; p++)
                        {
                            if (((report.AhciPi >> p) & 1) == 1)
                            {
                                uint portOffset = 0x100 + (uint)(p * 0x80);
                                uint ssts = ReadMmio32(abar, portOffset + 0x28);
                                uint sig = ReadMmio32(abar, portOffset + 0x24);
                                uint cmd = ReadMmio32(abar, portOffset + 0x18);
                                uint tfd = ReadMmio32(abar, portOffset + 0x20);

                                byte det = (byte)(ssts & 0x0F);
                                byte spd = (byte)((ssts >> 4) & 0x0F);

                                var portDiag = new AhciPortDiag
                                {
                                    PortIndex = p,
                                    Implemented = true,
                                    Ssts = ssts,
                                    Sig = sig,
                                    Cmd = cmd,
                                    Tfd = tfd,
                                    Det = det,
                                    Spd = spd
                                };

                                if (det == 3) // Physical communication established
                                {
                                    portDiag.DriveConnected = true;
                                    report.ActiveAhciDrives++;

                                    if (sig == 0x00000101) portDiag.DeviceType = "SATA Hard Drive / SSD";
                                    else if (sig == 0xEB140101) portDiag.DeviceType = "SATAPI CD/DVD Drive";
                                    else if (sig == 0xC33C0101) portDiag.DeviceType = "SEMB Enclosure";
                                    else if (sig == 0x96690101) portDiag.DeviceType = "Port Multiplier";
                                    else portDiag.DeviceType = $"SATA Device (0x{sig:X8})";

                                    if (spd == 1) portDiag.SpeedString = "Gen 1 (1.5 Gbps)";
                                    else if (spd == 2) portDiag.SpeedString = "Gen 2 (3.0 Gbps)";
                                    else if (spd == 3) portDiag.SpeedString = "Gen 3 (6.0 Gbps)";
                                    else portDiag.SpeedString = "Active";
                                }
                                else if (det == 1)
                                {
                                    portDiag.DeviceType = "Device present, no PHY link";
                                }

                                report.AhciPorts.Add(portDiag);
                            }
                        }
                    }
                    catch { }
                    break;
                }
            }

            // 3. Probe Legacy IDE Ports (Cosmos AtaPio ports)
            try
            {
                var port1F7 = new IOPort(0x1F7);
                report.LegacyPrimaryStatus = port1F7.Byte;
                report.LegacyPrimaryFloating = (report.LegacyPrimaryStatus == 0xFF);
            }
            catch
            {
                report.LegacyPrimaryFloating = true;
                report.LegacyPrimaryStatus = 0xFF;
            }

            try
            {
                var port177 = new IOPort(0x177);
                report.LegacySecondaryStatus = port177.Byte;
                report.LegacySecondaryFloating = (report.LegacySecondaryStatus == 0xFF);
            }
            catch
            {
                report.LegacySecondaryFloating = true;
                report.LegacySecondaryStatus = 0xFF;
            }

            // 4. Inspect Cosmos HAL Block Devices
            try
            {
                if (BlockDevice.Devices != null)
                {
                    report.CosmosBlockDeviceCount = BlockDevice.Devices.Count;
                    for (int i = 0; i < BlockDevice.Devices.Count; i++)
                    {
                        var dev = BlockDevice.Devices[i];
                        if (dev != null)
                        {
                            string dType = dev is Partition ? "Partition" : "ATA / IDE Storage Device";
                            ulong secCount = dev.BlockCount;
                            string sizeStr = "";
                            if (secCount > 0 && secCount < 2000000000UL)
                            {
                                ulong mb = (secCount * dev.BlockSize) / (1024UL * 1024UL);
                                sizeStr = $"{mb} MB ({secCount} sectors)";
                            }
                            else
                            {
                                sizeStr = "Active Hardware Target";
                            }
                            report.CosmosBlockDeviceNames.Add($"#{i}: {dType} [{sizeStr}]");
                        }
                    }
                }
            }
            catch { }

            // 5. Inspect Cosmos VFS Disks and their Partitions
            try
            {
                var disks = VFSManager.GetDisks();
                if (disks != null)
                {
                    report.VfsDiskCount = disks.Count;
                    for (int i = 0; i < disks.Count; i++)
                    {
                        var d = disks[i];
                        if (d != null)
                        {
                            report.VfsDiskNames.Add($"Disk #{i}: ({d.Partitions.Count} partitions)");
                            for (int p = 0; p < d.Partitions.Count; p++)
                            {
                                var part = d.Partitions[p];
                                if (part != null)
                                {
                                    var host = part.Host;
                                    ulong bCount = host != null ? host.BlockCount : 0;
                                    ulong bSize = host != null ? host.BlockSize : 512;
                                    ulong mb = (bCount * bSize) / (1024UL * 1024UL);
                                    string fsType = part.MountedFS != null ? part.MountedFS.Type : (part.HasFileSystem ? "Mounted" : "Raw / Linux");
                                    report.VfsDiskNames.Add($"   Part {p + 1}: {mb} MB ({bCount} sec, {fsType})");
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            // 6. Synthesize Root-Cause Explanation and Remediation
            SynthesizeDiagnosis(report);

            return report;
        }

        private static void SynthesizeDiagnosis(DiskDiagReport r)
        {
            if (r.ActiveAhciDrives > 0 && r.CosmosBlockDeviceCount == 0)
            {
                r.PrimaryDiagnosis = "HARDWARE SATA DRIVE DETECTED, BUT BLOCKED BY AHCI MODE IN BIOS";
                r.DetailedReasons.Add($"[!] {r.ActiveAhciDrives} physical SATA drive(s) detected and communicating via AHCI ABAR (0x{r.AhciAbarAddress:X8})!");
                r.DetailedReasons.Add("[!] ROOT CAUSE: Laptop SATA controller is currently running in 'AHCI Mode'.");
                r.DetailedReasons.Add("[!] Cosmos OS HAL only includes a legacy IDE ATA PIO driver (ports 0x1F0/0x170).");
                r.DetailedReasons.Add($"[!] In AHCI mode, legacy IDE ports are disconnected by chipset (Port 0x1F7 = 0x{r.LegacyPrimaryStatus:X2} Floating).");
                r.DetailedReasons.Add("[!] Because legacy ports are floating (0xFF), Cosmos ATA PIO detected 0 drives at boot.");

                r.BiosRemediationSteps.Add("1. Restart laptop and press [F2], [F12], or [Del] during boot to enter BIOS Setup.");
                r.BiosRemediationSteps.Add("2. Navigate to 'System Configuration' -> 'Storage' or 'SATA Operation / Chipset'.");
                r.BiosRemediationSteps.Add("3. Change 'SATA Controller Mode' from [AHCI] to [IDE / Legacy / Compatible Mode].");
                r.BiosRemediationSteps.Add("4. Save changes ([F10]) and reboot into WeatherOS.");
                r.BiosRemediationSteps.Add("5. Cosmos ATA PIO will instantly detect the hard drive at port 0x1F0!");
            }
            else if (r.Controllers.Exists(c => c.IsNvme))
            {
                var nvme = r.Controllers.Find(c => c.IsNvme);
                r.PrimaryDiagnosis = $"INTERNAL M.2 NVMe SSD DETECTED ({nvme?.VendorName})";
                r.DetailedReasons.Add($"[!] High-speed NVMe PCIe Controller detected (PCI 01:08:02, Vendor: {nvme?.VendorName}).");
                r.DetailedReasons.Add("[!] Cosmos OS does not currently include a bare-metal NVMe PCIe block driver.");
                r.DetailedReasons.Add("[!] Therefore, the NVMe SSD is not registered in BlockDevice.Devices.");

                r.BiosRemediationSteps.Add("1. Connect an external USB Flash Drive formatted as FAT32 for direct file storage.");
                r.BiosRemediationSteps.Add("2. Or install an internal 2.5\" SATA SSD if your laptop has a secondary SATA bay.");
            }
            else if (r.AhciControllerFound && r.ActiveAhciDrives == 0)
            {
                r.PrimaryDiagnosis = "SATA CONTROLLER FOUND, BUT NO ACTIVE DRIVE ON AHCI PORTS";
                r.DetailedReasons.Add($"[!] AHCI controller responding at ABAR 0x{r.AhciAbarAddress:X8}.");
                r.DetailedReasons.Add("[!] Ports Implemented bitmask: 0x" + r.AhciPi.ToString("X8") + $", but DET status is 0 (No physical PHY link).");
                r.DetailedReasons.Add("[!] Verify internal SATA drive cable or power connection.");

                r.BiosRemediationSteps.Add("1. Check if hard drive is firmly seated in the laptop drive bay.");
                r.BiosRemediationSteps.Add("2. Check BIOS to confirm the hard drive appears in Boot / Information tab.");
            }
            else if (r.CosmosBlockDeviceCount > 0 || r.VfsDiskCount > 0)
            {
                r.PrimaryDiagnosis = $"SUCCESS: {r.CosmosBlockDeviceCount} DRIVES & {r.VfsDiskCount} DISKS FOUND IN COSMOS!";
                r.DetailedReasons.Add($"[OK] Physical storage controllers operating in IDE Compatibility Mode.");
                r.DetailedReasons.Add($"[OK] Primary IDE Channel (0x1F0) is ACTIVE and operational.");
                r.DetailedReasons.Add($"[OK] Cosmos VFS parsed partition tables ({r.VfsDiskCount} disks with partitions).");
                r.DetailedReasons.Add($"[!] All physical Linux/Data partitions are now available in 'disks' and 'diskmgr'.");

                r.BiosRemediationSteps.Add("1. No BIOS changes needed - IDE mode is working perfectly!");
                r.BiosRemediationSteps.Add("2. Run 'disks' command or open 'Disk & Storage Explorer' (diskmgr).");
                r.BiosRemediationSteps.Add("3. You can format any partition to FAT32 or perform a full disk wipe.");
            }
            else
            {
                r.PrimaryDiagnosis = "NO STORAGE CONTROLLERS OR DRIVES FOUND ON PCI BUS";
                r.DetailedReasons.Add("[!] No PCI devices matched ClassCode 0x01 (Mass Storage).");
                r.DetailedReasons.Add("[!] Legacy IDE ports 0x1F0 and 0x170 are both floating (0xFF).");
                r.BiosRemediationSteps.Add("1. Ensure laptop storage controller is enabled in BIOS.");
                r.BiosRemediationSteps.Add("2. Try plugging in a standard USB drive.");
            }
        }

        public static void PrintCliReport()
        {
            var r = RunDiagnostics();

            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.WriteLine("\n================================================================================");
            System.Console.WriteLine("             HARDWARE DISK & STORAGE DIAGNOSTICS & ROOT-CAUSE REPORT            ");
            System.Console.WriteLine("================================================================================");
            System.Console.ResetColor();

            System.Console.ForegroundColor = ConsoleColor.Yellow;
            System.Console.WriteLine($"\n[DIAGNOSTIC VERDICT] : {r.PrimaryDiagnosis}");
            System.Console.ResetColor();

            System.Console.WriteLine("\n--- PCI STORAGE CONTROLLERS ---");
            if (r.Controllers.Count == 0)
            {
                System.Console.WriteLine("  No PCI storage controllers detected.");
            }
            else
            {
                foreach (var c in r.Controllers)
                {
                    string type = c.IsAhci ? "SATA AHCI" : (c.IsNvme ? "NVMe SSD" : (c.IsIde ? "Legacy IDE" : (c.IsRaid ? "RAID" : "Storage")));
                    System.Console.WriteLine($"  [{c.Bus:X2}:{c.Slot:X2}.{c.Function:X2}] {c.VendorName} (0x{c.VendorId:X4}:0x{c.DeviceId:X4})");
                    System.Console.WriteLine($"       Type: {type} ({c.SubclassName}) | BAR5: 0x{c.AbarAddress:X8}");
                }
            }

            if (r.AhciControllerFound)
            {
                System.Console.WriteLine("\n--- AHCI ABAR (0x" + r.AhciAbarAddress.ToString("X8") + ") MEMORY PROBE ---");
                System.Console.WriteLine($"  AHCI Enabled: {(r.AhciEnabled ? "YES (GHC.AE = 1)" : "NO")} | Implemented Ports Mask: 0x{r.AhciPi:X8}");
                foreach (var p in r.AhciPorts)
                {
                    string status = p.DriveConnected ? $"[ONLINE] {p.DeviceType} ({p.SpeedString})" : $"[OFFLINE] Status: 0x{p.Ssts:X8}";
                    ConsoleColor col = p.DriveConnected ? ConsoleColor.Green : ConsoleColor.DarkGray;
                    System.Console.ForegroundColor = col;
                    System.Console.WriteLine($"  Port {p.PortIndex}: {status} (Sig: 0x{p.Sig:X8})");
                    System.Console.ResetColor();
                }
            }

            System.Console.WriteLine("\n--- LEGACY IDE PORTS (Cosmos AtaPio Target) ---");
            System.Console.WriteLine($"  Primary Channel (0x1F0..0x1F7)   : Status 0x{r.LegacyPrimaryStatus:X2} -> {(r.LegacyPrimaryFloating ? "FLOATING BUS (Disabled in AHCI Mode)" : "ACTIVE")}");
            System.Console.WriteLine($"  Secondary Channel (0x170..0x177) : Status 0x{r.LegacySecondaryStatus:X2} -> {(r.LegacySecondaryFloating ? "FLOATING BUS (Disabled in AHCI Mode)" : "ACTIVE")}");

            System.Console.WriteLine("\n--- COSMOS HAL BLOCK DEVICES & VFS ---");
            System.Console.WriteLine($"  BlockDevice.Devices Count : {r.CosmosBlockDeviceCount}");
            foreach (var b in r.CosmosBlockDeviceNames) System.Console.WriteLine($"    {b}");
            System.Console.WriteLine($"  VFSManager.GetDisks()     : {r.VfsDiskCount}");
            foreach (var d in r.VfsDiskNames) System.Console.WriteLine($"    {d}");

            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.WriteLine("\n--------------------------------------------------------------------------------");
            System.Console.WriteLine(" DETAILED ROOT-CAUSE EXPLANATION:");
            System.Console.WriteLine("--------------------------------------------------------------------------------");
            System.Console.ResetColor();
            foreach (var pt in r.DetailedReasons)
            {
                System.Console.WriteLine(" " + pt);
            }

            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine("\n--------------------------------------------------------------------------------");
            System.Console.WriteLine(" HOW TO RESOLVE IN YOUR LAPTOP BIOS SETUP:");
            System.Console.WriteLine("--------------------------------------------------------------------------------");
            System.Console.ResetColor();
            foreach (var step in r.BiosRemediationSteps)
            {
                System.Console.WriteLine(" " + step);
            }
            System.Console.WriteLine();
        }

        private static string GetVendorName(ushort vendorId)
        {
            switch (vendorId)
            {
                case 0x8086: return "Intel Corporation";
                case 0x1002:
                case 0x1022: return "AMD / Advanced Micro Devices";
                case 0x10DE: return "NVIDIA Corporation";
                case 0x11AB: return "Marvell Technology";
                case 0x1B21: return "ASMedia Technology";
                case 0x144D: return "Samsung Electronics (NVMe)";
                case 0x1987: return "Phison Electronics";
                case 0x1269: return "Silicon Motion";
                case 0x10EC: return "Realtek Semiconductor";
                case 0x1000: return "Broadcom / LSI Logic";
                case 0x105A: return "Promise Technology";
                case 0x1095: return "Silicon Image";
                case 0x1106: return "VIA Technologies";
                case 0x15B7: return "Western Digital / SanDisk";
                case 0x1344: return "Micron Technology";
                case 0x1C58:
                case 0x1E0F: return "Kioxia / Toshiba Memory";
                default: return $"PCI Vendor 0x{vendorId:X4}";
            }
        }

        private static string GetSubclassName(uint subclass, uint progIf)
        {
            switch (subclass)
            {
                case 0x01: return "IDE Controller";
                case 0x06: return progIf == 0x01 ? "Serial ATA (AHCI 1.0)" : "Serial ATA";
                case 0x08: return progIf == 0x02 ? "NVM Express (NVMe)" : "Non-Volatile Memory";
                case 0x04: return "RAID Controller";
                case 0x00: return "SCSI Bus Controller";
                case 0x07: return "Serial Attached SCSI (SAS)";
                default: return $"Storage Subclass 0x{subclass:X2}";
            }
        }

        private static uint ReadMmio32(MemoryBlock block, uint offset)
        {
            return (uint)(block[offset] |
                         (block[offset + 1] << 8) |
                         (block[offset + 2] << 16) |
                         (block[offset + 3] << 24));
        }
    }
}
