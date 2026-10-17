using System;
using System.Collections.Generic;
using System.Text;
using Cosmos.Core;
using Cosmos.HAL;
using Cosmos.HAL.BlockDevice;

namespace WeatherOS.Services
{
    public class CustomPciAtaBlockDevice : BlockDevice
    {
        public ushort CmdBase { get; private set; }
        public ushort CtrlBase { get; private set; }
        public byte DriveSelect { get; private set; }
        public bool IsLba48 { get; private set; }
        public string Model { get; private set; } = "";
        public string Serial { get; private set; } = "";
        public string ChannelName { get; private set; } = "";
        public byte[] Sector0Cache { get; set; } = null;

        public override BlockDeviceType Type => BlockDeviceType.HardDrive;

        public CustomPciAtaBlockDevice(ushort cmdBase, ushort ctrlBase, byte driveSelect, AtaIdentifyInfo info, string channelName)
        {
            CmdBase = cmdBase;
            CtrlBase = ctrlBase;
            DriveSelect = driveSelect;
            IsLba48 = info.IsLba48;
            mBlockCount = info.TotalSectors > 0 ? info.TotalSectors : 204800UL;
            mBlockSize = 512;
            string cleanModel = SanitizeAtaModel(info.Model);
            if (string.IsNullOrEmpty(cleanModel))
            {
                Model = "Internal Fixed Storage";
            }
            else
            {
                Model = cleanModel;
            }
            Serial = info.Serial ?? "";
            ChannelName = channelName ?? "";
        }

        public static string SanitizeAtaModel(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string s = raw.Trim('\0', ' ', '\t', '\r', '\n');
            if (s.Length == 0) return "";
            int distinct = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c <= 32 || c > 126) continue;
                bool seen = false;
                for (int j = 0; j < i; j++)
                {
                    if (s[j] == c) { seen = true; break; }
                }
                if (!seen) distinct++;
            }
            if (distinct < 3 && s.Length > 4) return "";
            return s;
        }

        public override void ReadBlock(ulong aBlock, ulong aBlockCount, ref byte[] aData)
        {
            if (aData == null) return;
            for (ulong s = 0; s < aBlockCount; s++)
            {
                int off = (int)(s * 512);
                if (!ReadSectorPio(CmdBase, CtrlBase, DriveSelect, IsLba48, aBlock + s, aData, off))
                {
                    ReadSectorPio(CmdBase, CtrlBase, DriveSelect, IsLba48, aBlock + s, aData, off);
                }
            }
        }

        public override void WriteBlock(ulong aBlock, ulong aBlockCount, ref byte[] aData)
        {
            if (aData == null) return;
            for (ulong s = 0; s < aBlockCount; s++)
            {
                int off = (int)(s * 512);
                bool ok = WriteSectorPio(CmdBase, CtrlBase, DriveSelect, IsLba48, aBlock + s, aData, off);
                if (!ok)
                {
                    // Retry once on failure
                    WriteSectorPio(CmdBase, CtrlBase, DriveSelect, IsLba48, aBlock + s, aData, off);
                }
            }
            FlushPio(CmdBase, DriveSelect, IsLba48);
        }

        public static bool ReadSectorPio(ushort cmdBase, ushort ctrlBase, byte drvSel, bool lba48, ulong lba, byte[] buffer, int offset = 0)
        {
            try
            {
                var pData = new IOPort(cmdBase);
                var pSecCount = new IOPort((ushort)(cmdBase + 2));
                var pLbaLow = new IOPort((ushort)(cmdBase + 3));
                var pLbaMid = new IOPort((ushort)(cmdBase + 4));
                var pLbaHigh = new IOPort((ushort)(cmdBase + 5));
                var pDrive = new IOPort((ushort)(cmdBase + 6));
                var pCmd = new IOPort((ushort)(cmdBase + 7));

                // Wait for drive not busy
                if (!WaitNotBusy(pCmd, 200000)) return false;

                if (lba48 && lba >= 0x0FFFFFFFU)
                {
                    pDrive.Byte = (byte)(0x40 | (drvSel == 0xB0 ? 0x10 : 0x00));
                    WaitIo(pCmd);

                    // High bytes
                    pSecCount.Byte = 0;
                    pLbaLow.Byte = (byte)(lba >> 24);
                    pLbaMid.Byte = (byte)(lba >> 32);
                    pLbaHigh.Byte = (byte)(lba >> 40);

                    // Low bytes
                    pSecCount.Byte = 1;
                    pLbaLow.Byte = (byte)lba;
                    pLbaMid.Byte = (byte)(lba >> 8);
                    pLbaHigh.Byte = (byte)(lba >> 16);

                    pCmd.Byte = 0x24; // READ SECTORS EXT
                }
                else
                {
                    pDrive.Byte = (byte)(0xE0 | (drvSel == 0xB0 ? 0x10 : 0x00) | (int)((lba >> 24) & 0x0F));
                    WaitIo(pCmd);

                    pSecCount.Byte = 1;
                    pLbaLow.Byte = (byte)lba;
                    pLbaMid.Byte = (byte)(lba >> 8);
                    pLbaHigh.Byte = (byte)(lba >> 16);

                    pCmd.Byte = 0x20; // READ SECTORS
                }

                if (!WaitDrq(pCmd, 200000)) return false;

                for (int w = 0; w < 256; w++)
                {
                    ushort word = pData.Word;
                    buffer[offset + (w * 2)] = (byte)(word & 0xFF);
                    buffer[offset + (w * 2) + 1] = (byte)(word >> 8);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool WriteSectorPio(ushort cmdBase, ushort ctrlBase, byte drvSel, bool lba48, ulong lba, byte[] buffer, int offset = 0)
        {
            try
            {
                var pData = new IOPort(cmdBase);
                var pSecCount = new IOPort((ushort)(cmdBase + 2));
                var pLbaLow = new IOPort((ushort)(cmdBase + 3));
                var pLbaMid = new IOPort((ushort)(cmdBase + 4));
                var pLbaHigh = new IOPort((ushort)(cmdBase + 5));
                var pDrive = new IOPort((ushort)(cmdBase + 6));
                var pCmd = new IOPort((ushort)(cmdBase + 7));

                if (!WaitNotBusy(pCmd, 200000)) return false;

                if (lba48 && lba >= 0x0FFFFFFFU)
                {
                    pDrive.Byte = (byte)(0x40 | (drvSel == 0xB0 ? 0x10 : 0x00));
                    WaitIo(pCmd);

                    pSecCount.Byte = 0;
                    pLbaLow.Byte = (byte)(lba >> 24);
                    pLbaMid.Byte = (byte)(lba >> 32);
                    pLbaHigh.Byte = (byte)(lba >> 40);

                    pSecCount.Byte = 1;
                    pLbaLow.Byte = (byte)lba;
                    pLbaMid.Byte = (byte)(lba >> 8);
                    pLbaHigh.Byte = (byte)(lba >> 16);

                    pCmd.Byte = 0x34; // WRITE SECTORS EXT
                }
                else
                {
                    pDrive.Byte = (byte)(0xE0 | (drvSel == 0xB0 ? 0x10 : 0x00) | (int)((lba >> 24) & 0x0F));
                    WaitIo(pCmd);

                    pSecCount.Byte = 1;
                    pLbaLow.Byte = (byte)lba;
                    pLbaMid.Byte = (byte)(lba >> 8);
                    pLbaHigh.Byte = (byte)(lba >> 16);

                    pCmd.Byte = 0x30; // WRITE SECTORS
                }

                if (!WaitDrq(pCmd, 200000)) return false;

                for (int w = 0; w < 256; w++)
                {
                    ushort word = (ushort)(buffer[offset + (w * 2)] | (buffer[offset + (w * 2) + 1] << 8));
                    pData.Word = word;
                }

                // Wait for BSY to clear after writing sector
                return WaitNotBusy(pCmd, 200000);
            }
            catch
            {
                return false;
            }
        }

        public static void FlushPio(ushort cmdBase, byte drvSel, bool lba48)
        {
            try
            {
                var pDrive = new IOPort((ushort)(cmdBase + 6));
                var pCmd = new IOPort((ushort)(cmdBase + 7));
                pDrive.Byte = (byte)(0xE0 | (drvSel == 0xB0 ? 0x10 : 0x00));
                WaitIo(pCmd);
                pCmd.Byte = (lba48 && drvSel == 0xA0) ? (byte)0xEA : (byte)0xE7; // FLUSH CACHE EXT / FLUSH CACHE
                WaitNotBusy(pCmd, 200000);
            }
            catch { }
        }

        private static void WaitIo(IOPort pStatus)
        {
            byte b = pStatus.Byte;
            b = pStatus.Byte;
            b = pStatus.Byte;
            b = pStatus.Byte;
        }

        private static bool WaitNotBusy(IOPort pStatus, int timeout)
        {
            while (timeout-- > 0)
            {
                byte st = pStatus.Byte;
                if (st == 0xFF) return false;
                if ((st & 0x80) == 0) return true;
            }
            return false;
        }

        private static bool WaitDrq(IOPort pStatus, int timeout)
        {
            while (timeout-- > 0)
            {
                byte st = pStatus.Byte;
                if (st == 0xFF) return false;
                if ((st & 0x80) == 0)
                {
                    if ((st & 0x01) != 0) return false; // ERR bit set
                    if ((st & 0x08) != 0) return true;  // DRQ ready
                }
            }
            return false;
        }
    }

    public static class CustomPciAtaDriverService
    {
        private static bool _initialized = false;
        private static List<CustomPciAtaBlockDevice> _devices = new List<CustomPciAtaBlockDevice>();

        public static List<CustomPciAtaBlockDevice> GetDevices()
        {
            if (!_initialized)
            {
                Initialize();
            }
            return _devices;
        }

        public static void Initialize()
        {
            _initialized = true;
            _devices.Clear();

            // 1. Scan and enable ALL PCI Mass Storage controllers (Class 0x01) first
            // This ensures I/O space decoding and Bus Master are enabled before probing any I/O ports
            try
            {
                if (Cosmos.HAL.PCI.Devices != null)
                {
                    foreach (var device in Cosmos.HAL.PCI.Devices)
                    {
                        if (device.ClassCode == 0x01) // Mass Storage
                        {
                            var pciNormal = new PCIDeviceNormal(device.bus, device.slot, device.function);
                            pciNormal.EnableDevice();

                            // Enable Bus Master (bit 2), Memory (bit 1), and I/O Space (bit 0)
                            uint cmdStatus = pciNormal.ReadRegister32(0x04);
                            cmdStatus |= 0x0007u;
                            pciNormal.WriteRegister32(0x04, cmdStatus);

                            // On Intel ICH7/ICHx SATA controllers, enable Port 0 & Port 1 in PCS register
                            if (device.VendorID == 0x8086)
                            {
                                try
                                {
                                    uint mapPcs = pciNormal.ReadRegister32(0x90);
                                    mapPcs |= 0x00030000u; // Enable SATA ports 0 and 1
                                    pciNormal.WriteRegister32(0x90, mapPcs);
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            catch { }

            // 2. Now probe standard legacy IDE ports (Primary 0x1F0, Secondary 0x170)
            try
            {
                ProbeChannel(0x1F0, 0x3F6, "Standard IDE Primary (0x1F0)");
                ProbeChannel(0x170, 0x376, "Standard IDE Secondary (0x170)");
            }
            catch { }

            // 3. Scan PCI controllers for any native PCI BAR I/O ports
            try
            {
                if (Cosmos.HAL.PCI.Devices != null)
                {
                    foreach (var device in Cosmos.HAL.PCI.Devices)
                    {
                        if (device.ClassCode == 0x01) // Mass Storage
                        {
                            var pciNormal = new PCIDeviceNormal(device.bus, device.slot, device.function);
                            uint bar0 = pciNormal.ReadRegister32(0x10);
                            uint bar1 = pciNormal.ReadRegister32(0x14);
                            uint bar2 = pciNormal.ReadRegister32(0x18);
                            uint bar3 = pciNormal.ReadRegister32(0x1C);

                            // Primary Channel
                            if ((bar0 & 1) == 1 && (bar0 & 0xFFFC) > 0)
                            {
                                ushort priCmd = (ushort)(bar0 & 0xFFFC);
                                ushort priCtrl = ((bar1 & 1) == 1 && (bar1 & 0xFFFC) > 0) ? (ushort)((bar1 & 0xFFFC) + 2) : (ushort)(priCmd + 0x206);
                                ProbeChannel(priCmd, priCtrl, $"PCI [{device.bus:X2}:{device.slot:X2}.{device.function:X2}] BAR0 (0x{priCmd:X4})");
                            }

                            // Secondary Channel
                            if ((bar2 & 1) == 1 && (bar2 & 0xFFFC) > 0)
                            {
                                ushort secCmd = (ushort)(bar2 & 0xFFFC);
                                ushort secCtrl = ((bar3 & 1) == 1 && (bar3 & 0xFFFC) > 0) ? (ushort)((bar3 & 0xFFFC) + 2) : (ushort)(secCmd + 0x206);
                                ProbeChannel(secCmd, secCtrl, $"PCI [{device.bus:X2}:{device.slot:X2}.{device.function:X2}] BAR2 (0x{secCmd:X4})");
                            }
                        }
                    }
                }
            }
            catch { }

            // Register discovered custom devices in Cosmos BlockDevice.Devices so Cosmos VFS and Apps can use them
            try
            {
                for (int d = 0; d < _devices.Count; d++)
                {
                    var cd = _devices[d];
                    bool alreadyExists = false;
                    for (int ex = 0; ex < BlockDevice.Devices.Count; ex++)
                    {
                        if (BlockDevice.Devices[ex] == cd) { alreadyExists = true; break; }
                    }
                    if (!alreadyExists)
                    {
                        BlockDevice.Devices.Add(cd);
                    }
                }
            }
            catch { }
        }

        private static void ProbeChannel(ushort cmdBase, ushort ctrlBase, string channelTag)
        {
            if (cmdBase == 0) return;

            // Software reset channel to ensure hardware is out of standby and in a known state
            try
            {
                var pCtrl = new IOPort(ctrlBase);
                pCtrl.Byte = 0x04; // SRST
                for (int i = 0; i < 50; i++) { byte dummy = pCtrl.Byte; }
                pCtrl.Byte = 0x00; // clear SRST
                for (int i = 0; i < 500; i++) { byte dummy = pCtrl.Byte; }
            }
            catch { }

            // Probe Master (0xA0)
            ProbeSingleTarget(cmdBase, ctrlBase, 0xA0, $"{channelTag} Master");

            // Probe Slave (0xB0)
            ProbeSingleTarget(cmdBase, ctrlBase, 0xB0, $"{channelTag} Slave");
        }

        private static void ProbeSingleTarget(ushort cmdBase, ushort ctrlBase, byte drvSel, string channelTag)
        {
            try
            {
                var info = AtaDeviceDiagnostics.ProbeAta(cmdBase, drvSel);
                if (info.Exists && !info.IsAtapi)
                {
                    // Verify that Sector 0 can actually be read from this hardware channel
                    byte[] sec0 = new byte[512];
                    bool sec0Ok = CustomPciAtaBlockDevice.ReadSectorPio(cmdBase, ctrlBase, drvSel, false, 0, sec0);
                    if (!sec0Ok) return; // Unresponsive channel or non-existent slave

                    // Filter out floating bus noise (all 0xFF only; do NOT filter all 0x00 as empty sectors are valid!)
                    bool allFF = true;
                    for (int b = 0; b < 16; b++)
                    {
                        if (sec0[b] != 0xFF) { allFF = false; break; }
                    }
                    if (allFF) return;

                    // Inspect MBR partition table to get true drive capacity if GParted partitioned the disk
                    if (sec0[510] == 0x55 && sec0[511] == 0xAA)
                    {
                        ulong maxPartEnd = 0;
                        for (int p = 0; p < 4; p++)
                        {
                            int off = 446 + (p * 16);
                            uint sLba = (uint)(sec0[off + 8] | (sec0[off + 9] << 8) | (sec0[off + 10] << 16) | (sec0[off + 11] << 24));
                            uint cSec = (uint)(sec0[off + 12] | (sec0[off + 13] << 8) | (sec0[off + 14] << 16) | (sec0[off + 15] << 24));
                            if (sLba > 0 && cSec > 0 && ((ulong)sLba + (ulong)cSec) > maxPartEnd)
                            {
                                maxPartEnd = (ulong)sLba + (ulong)cSec;
                            }
                        }
                        if (maxPartEnd > info.TotalSectors)
                        {
                            info.TotalSectors = maxPartEnd;
                        }
                    }

                    info.Model = CustomPciAtaBlockDevice.SanitizeAtaModel(info.Model);

                    // Check for existing device duplicates / ghost mirrors
                    for (int i = 0; i < _devices.Count; i++)
                    {
                        var ed = _devices[i];
                        if (ed.CmdBase == cmdBase && ed.DriveSelect == drvSel) return;

                        // Ghost mirror on the same ATA channel: if Master and Slave report same sector count, Slave is a ghost
                        if (ed.CmdBase == cmdBase && ed.BlockCount > 0 && info.TotalSectors > 0)
                        {
                            long diff = (long)ed.BlockCount - (long)info.TotalSectors;
                            if (diff < 0) diff = -diff;
                            if (diff < 2048) return; // Same physical disk ghost mirror on same channel!
                        }

                        // Serial match
                        if (!string.IsNullOrEmpty(info.Serial) && string.Equals(ed.Serial, info.Serial, StringComparison.OrdinalIgnoreCase))
                        {
                            return; // Same drive already probed
                        }

                        // Cached Sector 0 comparison
                        if (ed.Sector0Cache != null)
                        {
                            bool identicalSec0 = true;
                            for (int b = 0; b < 32; b++)
                            {
                                if (sec0[b] != ed.Sector0Cache[b]) { identicalSec0 = false; break; }
                            }
                            if (identicalSec0)
                            {
                                long diff = (long)ed.BlockCount - (long)info.TotalSectors;
                                if (diff < 0) diff = -diff;
                                if (diff < 4096) return; // Same physical disk mirror!
                            }
                        }
                    }

                    // Avoid phantom devices on floating bus with 0 sectors
                    if (info.TotalSectors == 0) return;

                    var dev = new CustomPciAtaBlockDevice(cmdBase, ctrlBase, drvSel, info, channelTag);
                    dev.Sector0Cache = sec0;
                    _devices.Add(dev);
                }
            }
            catch { }
        }
    }
}
