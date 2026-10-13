using System;
using System.Collections.Generic;
using System.Text;
using Cosmos.Core;
using Cosmos.HAL;
using Cosmos.HAL.BlockDevice;

namespace WeatherOS.Services
{
    public class AhciBlockDevice : BlockDevice
    {
        public int PortNumber { get; private set; }
        public string Model { get; private set; } = "";

        public override BlockDeviceType Type => BlockDeviceType.HardDrive;

        public AhciBlockDevice(int port, ulong sectorCount, string model)
        {
            PortNumber = port;
            mBlockCount = sectorCount > 0 ? sectorCount : 204800;
            mBlockSize = 512;
            Model = string.IsNullOrWhiteSpace(model) ? "SATA Storage Device" : model;
        }

        public override void ReadBlock(ulong aBlock, ulong aBlockCount, ref byte[] aData)
        {
            if (aData == null) return;
            for (ulong s = 0; s < aBlockCount; s++)
            {
                byte[] sec = new byte[512];
                if (AhciDriverService.ReadSingleSector(PortNumber, aBlock + s, sec))
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
                AhciDriverService.WriteSingleSector(PortNumber, aBlock + s, sec);
            }
        }
    }

    internal class AhciPortBuffers
    {
        public ManagedMemoryBlock CmdList;
        public ManagedMemoryBlock Fis;
        public ManagedMemoryBlock CmdTable;
        public ManagedMemoryBlock DataBuf;
    }

    public static class AhciDriverService
    {
        private static bool _initialized = false;
        private static MemoryBlock _abar = null;
        private static uint _abarAddress = 0;
        private static AhciPortBuffers[] _ports = new AhciPortBuffers[32];
        private static List<AhciBlockDevice> _devices = new List<AhciBlockDevice>();

        public static List<AhciBlockDevice> GetDrives()
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
                // 1. Locate PCI Mass Storage Controller (Class 0x01, Subclass 0x06: SATA AHCI, or Subclass 0x04: RAID AHCI)
                foreach (var device in Cosmos.HAL.PCI.Devices)
                {
                    if (device.ClassCode == 0x01 && (device.Subclass == 0x06 || device.ProgIF == 0x01 || device.Subclass == 0x04))
                    {
                        var pciNormal = new PCIDeviceNormal(device.bus, device.slot, device.function);
                        pciNormal.EnableDevice();

                        // Enable Bus Mastering (bit 2), Memory Space (bit 1), I/O Space (bit 0)
                        uint cmdStatus = pciNormal.ReadRegister32(0x04);
                        cmdStatus |= 0x0007u;
                        pciNormal.WriteRegister32(0x04, cmdStatus);

                        uint abarAddr = pciNormal.ReadRegister32(0x24) & 0xFFFFFFF0u; // BAR5
                        if (abarAddr == 0)
                        {
                            abarAddr = pciNormal.ReadRegister32(0x10) & 0xFFFFFFF0u; // BAR0 fallback
                        }

                        if (abarAddr > 0)
                        {
                            _abarAddress = abarAddr;
                            _abar = new MemoryBlock(abarAddr, 0x10000);
                            break;
                        }
                    }
                }

                if (_abar == null || _abarAddress == 0)
                {
                    return;
                }

                // 2. Enable AHCI Mode (GHC.AE = 1 at offset 0x04)
                uint ghc = ReadMmio32(0x04);
                if ((ghc & 0x80000000u) == 0)
                {
                    ghc |= 0x80000000u;
                    WriteMmio32(0x04, ghc);
                }

                // 3. Scan Implemented Ports (PI bitmask at offset 0x0C)
                uint pi = ReadMmio32(0x0C);
                for (int p = 0; p < 32; p++)
                {
                    if (((pi >> p) & 1) == 1)
                    {
                        uint portBase = 0x100u + (uint)(p * 0x80);
                        uint ssts = ReadMmio32(portBase + 0x28);
                        byte det = (byte)(ssts & 0x0F);

                        if (det == 3) // Physical communication established
                        {
                            uint sig = ReadMmio32(portBase + 0x24);
                            if (sig == 0x00000101u || sig == 0) // SATA Hard Drive / SSD
                            {
                                var ahciDev = ConfigurePort(p, portBase);
                                if (ahciDev != null)
                                {
                                    _devices.Add(ahciDev);
                                    bool existsInHal = false;
                                    for (int ex = 0; ex < BlockDevice.Devices.Count; ex++)
                                    {
                                        if (BlockDevice.Devices[ex] == ahciDev) { existsInHal = true; break; }
                                    }
                                    if (!existsInHal)
                                    {
                                        BlockDevice.Devices.Add(ahciDev);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static AhciBlockDevice ConfigurePort(int port, uint portBase)
        {
            try
            {
                // 1. Stop Port Command Engine
                uint cmd = ReadMmio32(portBase + 0x18);
                if ((cmd & 0x01u) != 0)
                {
                    cmd &= ~0x01u; // Clear ST (bit 0)
                    WriteMmio32(portBase + 0x18, cmd);
                    int wait = 0;
                    while ((ReadMmio32(portBase + 0x18) & 0x8000u) != 0 && wait++ < 1000) { }
                }

                if ((cmd & 0x10u) != 0)
                {
                    cmd &= ~0x10u; // Clear FRE (bit 4)
                    WriteMmio32(portBase + 0x18, cmd);
                    int wait = 0;
                    while ((ReadMmio32(portBase + 0x18) & 0x4000u) != 0 && wait++ < 1000) { }
                }

                // 2. Allocate DMA Buffers via Cosmos ManagedMemoryBlock (Aligned & GC-Safe)
                var buf = new AhciPortBuffers();
                buf.CmdList = new ManagedMemoryBlock(1024, 1024);   // 1KB aligned to 1024
                buf.Fis = new ManagedMemoryBlock(256, 256);         // 256B aligned to 256
                buf.CmdTable = new ManagedMemoryBlock(256, 128);    // 256B aligned to 128
                buf.DataBuf = new ManagedMemoryBlock(512, 16);      // 512B aligned to 16
                _ports[port] = buf;

                // Clear memory
                for (uint i = 0; i < 1024; i++) buf.CmdList[i] = 0;
                for (uint i = 0; i < 256; i++) buf.Fis[i] = 0;
                for (uint i = 0; i < 256; i++) buf.CmdTable[i] = 0;
                for (uint i = 0; i < 512; i++) buf.DataBuf[i] = 0;

                uint clbAddr = (uint)buf.CmdList.Offset;
                uint fbAddr = (uint)buf.Fis.Offset;

                // 3. Set Base Addresses
                WriteMmio32(portBase + 0x00, clbAddr);   // CLB
                WriteMmio32(portBase + 0x04, 0);         // CLBU
                WriteMmio32(portBase + 0x08, fbAddr);    // FB (FIS Base)
                WriteMmio32(portBase + 0x0C, 0);         // FBU

                // Clear Errors and Interrupts
                WriteMmio32(portBase + 0x30, 0xFFFFFFFFu); // SERR
                WriteMmio32(portBase + 0x10, 0xFFFFFFFFu); // IS

                // Wait for BSY and DRQ in TFD to clear
                int spin = 0;
                while ((ReadMmio32(portBase + 0x20) & 0x88u) != 0 && spin++ < 2000) { }

                // 4. Start Port
                cmd = ReadMmio32(portBase + 0x18);
                cmd |= 0x0010u; // FRE (bit 4)
                WriteMmio32(portBase + 0x18, cmd);

                cmd |= 0x0001u; // ST (bit 0)
                WriteMmio32(portBase + 0x18, cmd);

                // 5. Query IDENTIFY DEVICE (Command 0xEC) to get model & sector count
                ulong sectorCount = 204800; // Default fallback
                string modelName = "SATA Hard Drive";

                byte[] identifyData = new byte[512];
                if (ExecuteAtaCommand(port, portBase, 0xEC, 0, 1, false, identifyData))
                {
                    // Model name: Words 27..46 (bytes 54..93)
                    char[] nameChars = new char[40];
                    for (int w = 0; w < 20; w++)
                    {
                        nameChars[w * 2] = (char)identifyData[55 + w * 2];
                        nameChars[w * 2 + 1] = (char)identifyData[54 + w * 2];
                    }
                    string m = new string(nameChars).Trim();
                    if (!string.IsNullOrEmpty(m)) modelName = m;

                    // Sector count: Words 100..103 (bytes 200..207) for 48-bit LBA
                    ulong sec48 = (ulong)identifyData[200] |
                                  ((ulong)identifyData[201] << 8) |
                                  ((ulong)identifyData[202] << 16) |
                                  ((ulong)identifyData[203] << 24) |
                                  ((ulong)identifyData[204] << 32) |
                                  ((ulong)identifyData[205] << 40) |
                                  ((ulong)identifyData[206] << 48) |
                                  ((ulong)identifyData[207] << 56);

                    if (sec48 > 0 && sec48 < 0x0000FFFFFFFFFFFFUL)
                    {
                        sectorCount = sec48;
                    }
                    else
                    {
                        // 28-bit LBA: Words 60..61 (bytes 120..123)
                        ulong sec28 = (ulong)(identifyData[120] |
                                             (identifyData[121] << 8) |
                                             (identifyData[122] << 16) |
                                             (identifyData[123] << 24));
                        if (sec28 > 0) sectorCount = sec28;
                    }
                }

                return new AhciBlockDevice(port, sectorCount, modelName);
            }
            catch
            {
                return null;
            }
        }

        public static bool ReadSingleSector(int port, ulong lba, byte[] buffer)
        {
            if (_abar == null || buffer == null || buffer.Length < 512) return false;
            if (port < 0 || port >= 32 || _ports[port] == null) return false;
            uint portBase = 0x100u + (uint)(port * 0x80);
            return ExecuteAtaCommand(port, portBase, 0x25, lba, 1, false, buffer); // 0x25 = READ DMA EXT
        }

        public static bool WriteSingleSector(int port, ulong lba, byte[] buffer)
        {
            if (_abar == null || buffer == null || buffer.Length < 512) return false;
            if (port < 0 || port >= 32 || _ports[port] == null) return false;
            uint portBase = 0x100u + (uint)(port * 0x80);
            return ExecuteAtaCommand(port, portBase, 0x35, lba, 1, true, buffer); // 0x35 = WRITE DMA EXT
        }

        private static bool ExecuteAtaCommand(int port, uint portBase, byte commandCode, ulong lba, ushort sectorCount, bool isWrite, byte[] buffer)
        {
            try
            {
                var buf = _ports[port];
                if (buf == null) return false;

                uint cmdTableAddr = (uint)buf.CmdTable.Offset;
                uint dataBufAddr = (uint)buf.DataBuf.Offset;

                // 1. Setup Command Header 0 (in buf.CmdList)
                buf.CmdList[0] = (byte)(5 | (isWrite ? 0x40 : 0x00)); // CFL=5 (20 bytes FIS), W bit 6
                buf.CmdList[1] = 0;
                buf.CmdList[2] = 1; // PRDTL = 1 entry (bits 16..23 of DW0)
                buf.CmdList[3] = 0;
                buf.CmdList[4] = 0; // PRD Byte Count (DW1)
                buf.CmdList[5] = 0;
                buf.CmdList[6] = 0;
                buf.CmdList[7] = 0;

                buf.CmdList[8] = (byte)(cmdTableAddr & 0xFF);         // CTBA Low (DW2)
                buf.CmdList[9] = (byte)((cmdTableAddr >> 8) & 0xFF);
                buf.CmdList[10] = (byte)((cmdTableAddr >> 16) & 0xFF);
                buf.CmdList[11] = (byte)((cmdTableAddr >> 24) & 0xFF);
                for (uint r = 12; r < 32; r++) buf.CmdList[r] = 0;

                // 2. Setup Command FIS (Register H2D, Type 0x27 at buf.CmdTable offset 0)
                for (uint f = 0; f < 64; f++) buf.CmdTable[f] = 0;

                buf.CmdTable[0] = 0x27; // FIS Type: Register H2D
                buf.CmdTable[1] = 0x80; // 0x80: Command flag
                buf.CmdTable[2] = commandCode;
                buf.CmdTable[3] = 0; // Features Low

                buf.CmdTable[4] = (byte)(lba & 0xFF);         // LBA0
                buf.CmdTable[5] = (byte)((lba >> 8) & 0xFF);  // LBA1
                buf.CmdTable[6] = (byte)((lba >> 16) & 0xFF); // LBA2
                buf.CmdTable[7] = 0x40;                       // Device: 1 << 6 (LBA mode)

                buf.CmdTable[8] = (byte)((lba >> 24) & 0xFF); // LBA3
                buf.CmdTable[9] = (byte)((lba >> 32) & 0xFF); // LBA4
                buf.CmdTable[10] = (byte)((lba >> 40) & 0xFF); // LBA5
                buf.CmdTable[11] = 0;                         // Features High

                buf.CmdTable[12] = (byte)(sectorCount & 0xFF);        // Count Low
                buf.CmdTable[13] = (byte)((sectorCount >> 8) & 0xFF); // Count High
                buf.CmdTable[14] = 0;
                buf.CmdTable[15] = 0;

                // 3. Setup PRDT Entry 0 (at buf.CmdTable offset 0x80)
                uint byteCount = (uint)(sectorCount * 512);
                buf.CmdTable[0x80] = (byte)(dataBufAddr & 0xFF);
                buf.CmdTable[0x81] = (byte)((dataBufAddr >> 8) & 0xFF);
                buf.CmdTable[0x82] = (byte)((dataBufAddr >> 16) & 0xFF);
                buf.CmdTable[0x83] = (byte)((dataBufAddr >> 24) & 0xFF);
                for (uint p = 0x84; p < 0x8C; p++) buf.CmdTable[p] = 0;

                uint dbc = (byteCount - 1) | 0x80000000u; // Interrupt on completion
                buf.CmdTable[0x8C] = (byte)(dbc & 0xFF);
                buf.CmdTable[0x8D] = (byte)((dbc >> 8) & 0xFF);
                buf.CmdTable[0x8E] = (byte)((dbc >> 16) & 0xFF);
                buf.CmdTable[0x8F] = (byte)((dbc >> 24) & 0xFF);

                // 4. If Writing: Copy buffer into Data Buffer
                if (isWrite)
                {
                    for (uint i = 0; i < 512; i++)
                    {
                        buf.DataBuf[i] = buffer[i];
                    }
                }

                // 5. Clear Interrupt and Error status
                WriteMmio32(portBase + 0x30, 0xFFFFFFFFu); // SERR
                WriteMmio32(portBase + 0x10, 0xFFFFFFFFu); // IS

                // Wait for port ready (not busy)
                int wait = 0;
                while ((ReadMmio32(portBase + 0x20) & 0x88u) != 0 && wait++ < 5000) { }

                // 6. Issue Command to Slot 0
                WriteMmio32(portBase + 0x38, 1); // CI = 1

                // 7. Poll for completion
                int timeout = 0;
                while ((ReadMmio32(portBase + 0x38) & 1) != 0)
                {
                    timeout++;
                    if (timeout > 500000)
                    {
                        return false; // Timeout
                    }
                }

                // 8. Check Task File Error bit (bit 0 of TFD)
                uint tfd = ReadMmio32(portBase + 0x20);
                if ((tfd & 0x01) != 0)
                {
                    return false;
                }

                // 9. If Reading: Copy data back to caller buffer
                if (!isWrite)
                {
                    for (uint i = 0; i < 512; i++)
                    {
                        buffer[i] = buf.DataBuf[i];
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static uint ReadMmio32(uint offset)
        {
            if (_abar == null) return 0;
            return _abar.DWords[offset];
        }

        private static void WriteMmio32(uint offset, uint value)
        {
            if (_abar == null) return;
            _abar.DWords[offset] = value;
        }
    }
}
