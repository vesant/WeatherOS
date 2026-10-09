using System;
using Cosmos.Core;
using Cosmos.HAL;

namespace Cosmos.HAL.Network
{
    /// <summary>
    /// Realtek RTL8102E / RTL8169 Series PCI Fast Ethernet Driver
    /// Written by ghost
    /// </summary>
    public class RTL8102E : NetworkDevice
    {
        private PCIDeviceNormal pciDevice;
        private uint mmioBase;
        private MemoryBlock mmio;
        private byte[] macBytes = new byte[6];

        private ManagedMemoryBlock rxDescBlock;
        private ManagedMemoryBlock txDescBlock;
        private ManagedMemoryBlock[] rxBuffers = new ManagedMemoryBlock[4];
        private ManagedMemoryBlock[] txBuffers = new ManagedMemoryBlock[4];

        // NetworkDevice Properties required by Cosmos OS
        public override CardType CardType => CardType.Ethernet;
        public override MACAddress MACAddress => new MACAddress(macBytes);
        public override string Name => "RTL8102E";
        public override bool Ready => true;
        
        // Cosmos Boilerplate methods
        public override byte[] ReceivePacket() { return null; }
        public override int BytesAvailable() { return 0; }
        public override bool Enable() { return true; }
        public override bool IsReceiveBufferFull() { return false; }
        public override bool IsSendBufferFull() { return false; }
        public override bool ReceiveBytes(byte[] buffer, int offset, int max) { return false; }

        public RTL8102E()
        {
        }

        public bool Initialize()
        {
            // Scan the PCI bus for the Realtek 10ec:8136 device
            foreach (var device in Cosmos.HAL.PCI.Devices)
            {
                if (device.VendorID == 0x10EC && device.DeviceID == 0x8136)
                {
                    pciDevice = new PCIDeviceNormal(device.bus, device.slot, device.function);
                    break;
                }
            }

            if (pciDevice == null)
            {
                Console.WriteLine("[rtl8102e] No compatible device found on PCI bus.");
                return false;
            }

            // Enable PCI Bus Mastering and Memory Space
            pciDevice.EnableDevice();
            
            // Locate the Memory Mapped I/O (MMIO) BAR
            bool foundMMIO = false;
            for (byte i = 0; i < 6; i++)
            {
                uint rawBar = pciDevice.ReadRegister32((byte)(0x10 + (i * 4)));
                if (rawBar != 0)
                {
                    bool isIo = (rawBar & 1) == 1;
                    if (!isIo)
                    {
                        byte type = (byte)((rawBar >> 1) & 0x03);
                        uint baseAddr = rawBar & 0xFFFFFFF0;
                        
                        // Handle 64-bit BARs (Type 2)
                        if (type == 2)
                        {
                            i++; 
                            uint rawBarHigh = pciDevice.ReadRegister32((byte)(0x10 + (i * 4)));
                            if (rawBarHigh != 0) continue; // High address is unsupported in 32-bit Cosmos
                        }
                        
                        if (baseAddr != 0)
                        {
                            mmioBase = baseAddr;
                            foundMMIO = true;
                            break;
                        }
                    }
                }
            }

            if (!foundMMIO)
            {
                Console.WriteLine("[rtl8102e] Initialization failed: No valid MMIO BAR found.");
                return false;
            }

            mmio = new MemoryBlock(mmioBase, 0x1000);
            return ResetHardwareAndExtractMAC();
        }

        private void Write32(ManagedMemoryBlock block, uint offset, uint value)
        {
            block[offset] = (byte)(value & 0xFF);
            block[offset + 1] = (byte)((value >> 8) & 0xFF);
            block[offset + 2] = (byte)((value >> 16) & 0xFF);
            block[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        private uint Read32(ManagedMemoryBlock block, uint offset)
        {
            return (uint)(block[offset] | (block[offset + 1] << 8) | (block[offset + 2] << 16) | (block[offset + 3] << 24));
        }

        private bool ResetHardwareAndExtractMAC()
        {
            // Soft Reset command (0x10) to Command Register (CR, offset 0x37)
            mmio.Bytes[0x37] = 0x10;

            int timeout = 10000;
            while ((mmio.Bytes[0x37] & 0x10) != 0)
            {
                timeout--;
                if (timeout <= 0)
                {
                    Console.WriteLine("[rtl8102e] Hardware soft reset timed out.");
                    return false;
                }
            }

            // Read the burned-in MAC address (registers 0x00 to 0x05)
            for (uint i = 0; i < 6; i++)
            {
                macBytes[i] = mmio.Bytes[i];
            }

            Console.Write($"[rtl8102e] eth0: RTL8102E at 0x{mmioBase:X8}, ");
            for (int i = 0; i < 6; i++)
            {
                Console.Write(macBytes[i].ToString("x2"));
                if (i < 5) Console.Write(":");
            }
            Console.WriteLine(", PIT Hack mode");

            return SetupDescriptorRings();
        }

        private bool SetupDescriptorRings()
        {
            // Allocate memory for the DMA Descriptor Rings (4 elements each, 16 bytes per element)
            // Aligned to 256 bytes boundary as required by hardware.
            rxDescBlock = new ManagedMemoryBlock(64, 256);
            txDescBlock = new ManagedMemoryBlock(64, 256);

            // Allocate 4 memory blocks for packet receiving (RX)
            rxBuffers = new ManagedMemoryBlock[4];
            for (int i = 0; i < 4; i++)
            {
                rxBuffers[i] = new ManagedMemoryBlock(1536, 8);
            }

            // Allocate 4 memory blocks for packet transmitting (TX)
            txBuffers = new ManagedMemoryBlock[4];
            for (int i = 0; i < 4; i++)
            {
                txBuffers[i] = new ManagedMemoryBlock(1536, 8);
            }

            // Initialize RX descriptors
            for (int i = 0; i < 4; i++)
            {
                uint offset = (uint)(i * 16);
                
                // Command/Status: 1536 buffer size (bits 0-13) | OWN bit (bit 31)
                // If it's the last descriptor in the ring, set EOR bit (bit 30)
                uint cmdStatus = 1536 | 0x80000000;
                if (i == 3) cmdStatus |= 0x40000000;
                
                Write32(rxDescBlock, offset, cmdStatus);
                Write32(rxDescBlock, offset + 4, 0); // Vlan
                Write32(rxDescBlock, offset + 8, (uint)rxBuffers[i].Offset); // Buffer Address Low
                Write32(rxDescBlock, offset + 12, (uint)(rxBuffers[i].Offset >> 32)); // Buffer Address High
            }

            // Initialize TX descriptors
            for (int i = 0; i < 4; i++)
            {
                uint offset = (uint)(i * 16);
                Write32(txDescBlock, offset, 0);
                Write32(txDescBlock, offset + 4, 0); // Vlan
                Write32(txDescBlock, offset + 8, (uint)txBuffers[i].Offset); // Buffer Address Low
                Write32(txDescBlock, offset + 12, (uint)(txBuffers[i].Offset >> 32)); // Buffer Address High
            }

            // Give the hardware the Physical Memory addresses for the rings
            mmio.DWords[0x20] = (uint)txDescBlock.Offset; // TxDescStartAddr Low
            mmio.DWords[0x24] = (uint)(txDescBlock.Offset >> 32); // TxDescStartAddr High
            mmio.DWords[0xE4] = (uint)rxDescBlock.Offset; // RxDescStartAddr Low
            mmio.DWords[0xE8] = (uint)(rxDescBlock.Offset >> 32); // RxDescStartAddr High

            return FinishInitialization();
        }

        private bool FinishInitialization()
        {
            // Enable RX and TX paths in the MAC Command Register (0x37)
            // RE (Receiver Enable) = 0x08, TE (Transmitter Enable) = 0x04 -> 0x0C
            mmio.Bytes[0x37] = 0x0C;

            // CRITICAL: Configure Receive Configuration Register (RCR, 0x44)
            // If this is 0, the MAC drops ALL packets!
            // 0x0F = Accept Broadcast (0x08) | Accept Multicast (0x04) | Accept Physical Match (0x02) | Accept All (0x01)
            // 0xE70F = Standard Realtek Linux config (allows WRAP, etc)
            mmio.DWords[0x44] = 0x0000E70F;

            // Force Clear PCI 'Interrupt Disable' bit (Bit 10) in PCI Command Register (Offset 0x04)
            // Some BIOSes leave this bit set to 1, completely muting the PCI INTx# line.
            uint pciCmd = pciDevice.ReadRegister32(0x04);
            pciCmd &= ~(uint)(1 << 10);
            pciDevice.WriteRegister32(0x04, pciCmd);

            // Timer Hack: We no longer use PIT Timer because Cosmos masks interrupts.
            // We manually invoke HackPoll() from our RawTcpHttp loops!

            // Interrupt Mask Register (0x3C): Mask all since we are polling!
            mmio.Words[0x3C] = 0x0000;

            return true;
        }

        private int _hackPollCounter = 0;
        public void HackPoll()
        {
            _hackPollCounter++;
            if (_hackPollCounter >= 50000)
            {
                Console.Write(".");
                _hackPollCounter = 0;
            }
            Cosmos.Core.INTs.IRQContext ctx = default;
            HandleInterrupt(ref ctx);
        }

        private int currentTxDesc = 0;
        private int currentRxDesc = 0;

        /// <summary>
        /// Invoked by Cosmos OS when it wants to send a packet over the network.
        /// Copies the buffer to our DMA pool and triggers the hardware to transmit it.
        /// </summary>
        public override bool QueueBytes(byte[] buffer, int offset, int length)
        {
            uint descOffset = (uint)(currentTxDesc * 16);
            uint cmdStatus = Read32(txDescBlock, descOffset);
            
            // Check if MAC is still processing our previous packet
            if ((cmdStatus & 0x80000000) != 0)
                return false; // Hardware Queue is full! Wait for the next loop.

            // Copy payload to the DMA buffer
            for (int i = 0; i < length; i++)
            {
                txBuffers[currentTxDesc][(uint)i] = buffer[offset + i];
            }

            // Ethernet padding rule: Packets must be at least 60 bytes long (excluding CRC).
            // Small packets like ARP replies (42 bytes) will be discarded as runts by switches if not padded.
            uint txLength = (uint)length;
            if (txLength < 60)
            {
                for (uint i = txLength; i < 60; i++)
                {
                    txBuffers[currentTxDesc][i] = 0; // Pad with zeroes
                }
                txLength = 60;
            }

            // CommandStatus construction:
            // Length (bits 0-15) | OWN (bit 31) | FS (First Segment, bit 29) | LS (Last Segment, bit 28)
            uint newCmdStatus = txLength | 0x80000000 | 0x20000000 | 0x10000000;
            if (currentTxDesc == 3) newCmdStatus |= 0x40000000; // Preserve EOR (End of Ring) bit
            
            Write32(txDescBlock, descOffset, newCmdStatus);
            
            // Trigger TX poll (TxPoll register 0x38 = 0x40)
            mmio.Bytes[0x38] = 0x40;
            
            currentTxDesc = (currentTxDesc + 1) % 4;
            return true;
        }

        /// <summary>
        /// Hardware Interrupt Handler.
        /// Invoked automatically by the CPU whenever the RTL8102E receives a packet.
        /// </summary>
        public void HandleInterrupt(ref Cosmos.Core.INTs.IRQContext context)
        {
            // 1. Read Interrupt Status Register (0x3E)
            ushort status = mmio.Words[0x3E];
            
            // 2. Process the Receive (RX) Ring
            while (true)
            {
                uint offset = (uint)(currentRxDesc * 16);
                uint cmdStatus = Read32(rxDescBlock, offset);
                
                // If the OWN bit (bit 31) is 1, the MAC still owns it (no packet received here)
                if ((cmdStatus & 0x80000000) != 0)
                    break;
                    
                // The MAC clears bit 31 to 0 when it drops a new packet into RAM
                uint rawLength = cmdStatus & 0x3FFF;
                
                // The hardware includes the 4-byte Ethernet CRC in the length.
                // Cosmos OS expects the payload without the CRC.
                uint length = rawLength > 4 ? rawLength - 4 : rawLength; 
                
                // Read from the unmanaged block into a managed C# array for the NetworkStack
                byte[] packet = new byte[length];
                for (uint i = 0; i < length; i++)
                {
                    packet[i] = rxBuffers[currentRxDesc][i];
                }

                // Push to the Cosmos Network Stack
                if (DataReceived != null)
                {
                    DataReceived(packet);
                }
                
                // Reset the descriptor and give ownership back to the MAC
                uint newCmdStatus = 1536 | 0x80000000;
                if (currentRxDesc == 3) newCmdStatus |= 0x40000000; // Preserve EOR bit
                
                Write32(rxDescBlock, offset, newCmdStatus);
                
                currentRxDesc = (currentRxDesc + 1) % 4;
            }

            // 3. Acknowledge interrupts (Clear ISR by writing the status back to it)
            mmio.Words[0x3E] = status;
        }
    }
}
