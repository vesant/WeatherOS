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

        public override BlockDeviceType Type => BlockDeviceType.HardDrive;

        public CustomPciAtaBlockDevice(ushort cmdBase, ushort ctrlBase, byte driveSelect, AtaIdentifyInfo info, string channelName)
        {
            CmdBase = cmdBase;
            CtrlBase = ctrlBase;
            DriveSelect = driveSelect;
            IsLba48 = info.IsLba48;
            mBlockCount = info.TotalSectors > 0 ? info.TotalSectors : 204800UL;
            mBlockSize = 512;
            Model = string.IsNullOrWhiteSpace(info.Model) ? "PCI ATA Fixed Storage" : info.Model;
            Serial = info.Serial ?? "";
            ChannelName = channelName ?? "";
        }

        public override void ReadBlock(ulong aBlock, ulong aBlockCount, ref byte[] aData)
        {
            if (aData == null) return;
            for (ulong s = 0; s < aBlockCount; s++)
            {
                byte[] sec = new byte[512];
                if (ReadSectorPio(CmdBase, CtrlBase, DriveSelect, IsLba48, aBlock + s, sec))
                {
                    Array.Copy(sec, 0, aData, (int)(s * 512), 512);
                }
            }
        }

        public override void WriteBlock(ulong aBlock, ulong aBlockCount, ref byte[] aData)
        {
            if (aData == null) return;
            for (ulong s = 0; s < aBlockCount; s++)
            {
                byte[] sec = new byte[512];
                Array.Copy(aData, (int)(s * 512), sec, 0, 512);
                WriteSectorPio(CmdBase, CtrlBase, DriveSelect, IsLba48, aBlock + s, sec);
            }
            FlushPio(CmdBase, DriveSelect, IsLba48);
        }

        public static bool ReadSectorPio(ushort cmdBase, ushort ctrlBase, byte drvSel, bool lba48, ulong lba, byte[] buffer)
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
                if (!WaitNotBusy(pCmd, 300000)) return false;

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

                if (!WaitDrq(pCmd, 300000)) return false;

                for (int w = 0; w < 256; w++)
                {
                    ushort word = pData.Word;
                    buffer[w * 2] = (byte)(word & 0xFF);
                    buffer[w * 2 + 1] = (byte)(word >> 8);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool WriteSectorPio(ushort cmdBase, ushort ctrlBase, byte drvSel, bool lba48, ulong lba, byte[] buffer)
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

                if (!WaitNotBusy(pCmd, 300000)) return false;

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

                if (!WaitDrq(pCmd, 300000)) return false;

                for (int w = 0; w < 256; w++)
                {
                    ushort word = (ushort)(buffer[w * 2] | (buffer[w * 2 + 1] << 8));
                    pData.Word = word;
                }

                // Wait for BSY to clear after writing sector
                return WaitNotBusy(pCmd, 300000);
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
                WaitNotBusy(pCmd, 300000);
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
                if ((st & 0x80) == 0) return true;
            }
            return false;
        }

        private static bool WaitDrq(IOPort pStatus, int timeout)
        {
            while (timeout-- > 0)
            {
                byte st = pStatus.Byte;
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

            try
            {
                // Scan all PCI Mass Storage controllers (Class 0x01)
                foreach (var device in Cosmos.HAL.PCI.Devices)
                {
                    if (device.ClassCode == 0x01) // Mass Storage
                    {
                        var pciNormal = new PCIDeviceNormal(device.bus, device.slot, device.function);
                        pciNormal.EnableDevice();

                        // Enable Bus Master (bit 2) and I/O Space (bit 0)
                        uint cmdStatus = pciNormal.ReadRegister32(0x04);
                        cmdStatus |= 0x0007u;
                        pciNormal.WriteRegister32(0x04, cmdStatus);

                        uint bar0 = pciNormal.ReadRegister32(0x10);
                        uint bar1 = pciNormal.ReadRegister32(0x14);
                        uint bar2 = pciNormal.ReadRegister32(0x18);
                        uint bar3 = pciNormal.ReadRegister32(0x1C);
                        uint bar4 = pciNormal.ReadRegister32(0x20);

                        // 1. Primary Channel
                        ushort priCmd = 0;
                        ushort priCtrl = 0;
                        if ((bar0 & 1) == 1 && (bar0 & 0xFFFC) > 0)
                        {
                            priCmd = (ushort)(bar0 & 0xFFFC);
                            priCtrl = ((bar1 & 1) == 1 && (bar1 & 0xFFFC) > 0) ? (ushort)(bar1 & 0xFFFC) : (ushort)(priCmd + 0x206);
                        }
                        else
                        {
                            priCmd = 0x1F0;
                            priCtrl = 0x3F6;
                        }

                        ProbeChannel(priCmd, priCtrl, $"PCI [{device.bus:X2}:{device.slot:X2}.{device.function:X2}] Pri (0x{priCmd:X4})");

                        // 2. Secondary Channel
                        ushort secCmd = 0;
                        ushort secCtrl = 0;
                        if ((bar2 & 1) == 1 && (bar2 & 0xFFFC) > 0)
                        {
                            secCmd = (ushort)(bar2 & 0xFFFC);
                            secCtrl = ((bar3 & 1) == 1 && (bar3 & 0xFFFC) > 0) ? (ushort)(bar3 & 0xFFFC) : (ushort)(secCmd + 0x206);
                        }
                        else
                        {
                            secCmd = 0x170;
                            secCtrl = 0x376;
                        }

                        ProbeChannel(secCmd, secCtrl, $"PCI [{device.bus:X2}:{device.slot:X2}.{device.function:X2}] Sec (0x{secCmd:X4})");
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
                    if (!sec0Ok) return; // Unresponsive floating channel or non-existent slave

                    // Filter out floating bus noise (all 0xFF or all 0x00)
                    bool allFF = true;
                    bool all00 = true;
                    for (int b = 0; b < 16; b++)
                    {
                        if (sec0[b] != 0xFF) allFF = false;
                        if (sec0[b] != 0x00) all00 = false;
                    }
                    if (allFF || all00) return;

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

                    bool isUsb = (info.TotalSectors > 0 && info.TotalSectors < 10000000UL);
                    for (int i = 0; i < _devices.Count; i++)
                    {
                        var ed = _devices[i];
                        if (ed.CmdBase == cmdBase && ed.DriveSelect == drvSel) return;
                        if (!string.IsNullOrEmpty(info.Serial) && string.Equals(ed.Serial, info.Serial, StringComparison.OrdinalIgnoreCase))
                        {
                            return; // Same drive already probed
                        }
                        if (isUsb && (ed.BlockCount > 0 && ed.BlockCount < 10000000UL) && ed.CmdBase == cmdBase)
                        {
                            return; // Skip duplicate USB mirror on same channel
                        }

                        // Check for identical MBR signature & partition table (bytes 440..510)
                        byte[] edSec0 = new byte[512];
                        try { ed.ReadBlock(0, 1, ref edSec0); } catch { }
                        if (sec0[510] == 0x55 && sec0[511] == 0xAA && edSec0[510] == 0x55 && edSec0[511] == 0xAA)
                        {
                            bool sameMbr = true;
                            for (int b = 440; b < 510; b++)
                            {
                                if (sec0[b] != edSec0[b]) { sameMbr = false; break; }
                            }
                            if (sameMbr) return; // Same physical disk mirror!
                        }
                    }

                    // Avoid phantom devices on floating bus with 0 sectors
                    if (info.TotalSectors == 0) return;

                    var dev = new CustomPciAtaBlockDevice(cmdBase, ctrlBase, drvSel, info, channelTag);
                    _devices.Add(dev);
                }
            }
            catch { }
        }
    }
}
