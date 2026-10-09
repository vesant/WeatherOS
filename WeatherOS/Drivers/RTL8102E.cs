using System;
using Cosmos.Core;
using Cosmos.HAL;

namespace Cosmos.HAL.Network
{
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

        // NetworkDevice Properties
        public override CardType CardType => CardType.Ethernet;
        public override Cosmos.HAL.Network.MACAddress MACAddress => new Cosmos.HAL.Network.MACAddress(macBytes);
        public override string Name => "RTL8102E";
        public override bool Ready => true;
        
        // Boilerplate methods
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
            Console.WriteLine("r8102e: initializing driver...");

            foreach (var device in Cosmos.HAL.PCI.Devices)
            {
                if (device.VendorID == 0x10EC && device.DeviceID == 0x8136)
                {
                    pciDevice = new PCIDeviceNormal(device.bus, device.slot, device.function);
                    
                    if (pciDevice != null)
                    {
                        Console.WriteLine($"r8102e: found device at pci {device.bus}:{device.slot}:{device.function}");
                        break;
                    }
                }
            }

            if (pciDevice == null)
            {
                Console.WriteLine("r8102e: error - device 10ec:8136 not found");
                return false;
            }

            pciDevice.EnableDevice();
            Console.WriteLine("r8102e: pci device enabled (bus master & memory)");

            bool foundMMIO = false;
            
            Console.WriteLine("r8102e: dumping raw bar registers...");
            for (byte i = 0; i < 6; i++)
            {
                uint rawBar = pciDevice.ReadRegister32((byte)(0x10 + (i * 4)));
                Console.WriteLine($"r8102e: raw BAR{i} = 0x{rawBar:X8}");
                
                if (rawBar != 0)
                {
                    bool isIo = (rawBar & 1) == 1;
                    if (!isIo)
                    {
                        // Check if it's 64-bit
                        byte type = (byte)((rawBar >> 1) & 0x03);
                        uint baseAddr = rawBar & 0xFFFFFFF0;
                        
                        if (type == 2) // 64-bit
                        {
                            i++; // skip next bar
                            uint rawBarHigh = pciDevice.ReadRegister32((byte)(0x10 + (i * 4)));
                            Console.WriteLine($"r8102e: raw BAR{i} (high) = 0x{rawBarHigh:X8}");
                            
                            // On 32-bit OS, we can only use it if upper 32 bits are 0
                            if (rawBarHigh != 0) {
                                Console.WriteLine("r8102e: warning - 64-bit bar is above 4GB, cannot use in 32-bit mode!");
                                continue;
                            }
                        }
                        
                        if (baseAddr != 0)
                        {
                            mmioBase = baseAddr;
                            Console.WriteLine($"r8102e: found MMIO at 0x{mmioBase:X8}");
                            foundMMIO = true;
                            break;
                        }
                    }
                }
            }

            if (!foundMMIO)
            {
                Console.WriteLine("r8102e: error - no valid mmio bar found");
                return false;
            }

            mmio = new MemoryBlock(mmioBase, 0x1000);
            return Phase2ResetAndMAC();
        }

        private void Write32(ManagedMemoryBlock block, uint offset, uint value)
        {
            block[offset] = (byte)(value & 0xFF);
            block[offset + 1] = (byte)((value >> 8) & 0xFF);
            block[offset + 2] = (byte)((value >> 16) & 0xFF);
            block[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        private bool Phase2ResetAndMAC()
        {
            Console.WriteLine("r8102e: issuing soft reset...");
            
            // Soft Reset command (0x10) to Command Register (CR, offset 0x37)
            mmio.Bytes[0x37] = 0x10;

            int timeout = 10000;
            while ((mmio.Bytes[0x37] & 0x10) != 0)
            {
                timeout--;
                if (timeout <= 0)
                {
                    Console.WriteLine("r8102e: error - soft reset timeout");
                    return false;
                }
            }
            Console.WriteLine("r8102e: soft reset complete");

            for (uint i = 0; i < 6; i++)
            {
                macBytes[i] = mmio.Bytes[i];
            }

            Console.Write("r8102e: mac address ");
            for (int i = 0; i < 6; i++)
            {
                Console.Write(macBytes[i].ToString("x2"));
                if (i < 5) Console.Write(":");
            }
            Console.WriteLine();

            Console.WriteLine("r8102e: phase 1 and 2 complete");
            return Phase3DescriptorRings();
        }

        private bool Phase3DescriptorRings()
        {
            Console.WriteLine("r8102e: initializing dma descriptor rings...");

            // Allocate 4 descriptors for RX and TX (4 * 16 = 64 bytes), aligned to 256
            rxDescBlock = new ManagedMemoryBlock(64, 256);
            txDescBlock = new ManagedMemoryBlock(64, 256);

            // Allocate RX Buffers (4 buffers of 1536 bytes -> aligned to 8 bytes)
            rxBuffers = new ManagedMemoryBlock[4];
            for (int i = 0; i < 4; i++)
            {
                rxBuffers[i] = new ManagedMemoryBlock(1536, 8);
            }

            // Allocate TX Buffers (4 buffers of 1536 bytes -> aligned to 8 bytes)
            txBuffers = new ManagedMemoryBlock[4];
            for (int i = 0; i < 4; i++)
            {
                txBuffers[i] = new ManagedMemoryBlock(1536, 8);
            }

            Console.WriteLine("r8102e: setting up rx ring in ram...");
            for (int i = 0; i < 4; i++)
            {
                uint offset = (uint)(i * 16);
                
                // Command/Status: 1536 buffer size (bits 0-13) | OWN bit (bit 31)
                // If it's the last descriptor, set EOR bit (bit 30)
                uint cmdStatus = 1536 | 0x80000000;
                if (i == 3) cmdStatus |= 0x40000000;
                
                Write32(rxDescBlock, offset, cmdStatus);
                Write32(rxDescBlock, offset + 4, 0); // Vlan
                Write32(rxDescBlock, offset + 8, (uint)rxBuffers[i].Offset); // Buffer Address Low
                Write32(rxDescBlock, offset + 12, (uint)(rxBuffers[i].Offset >> 32)); // Buffer Address High
            }

            Console.WriteLine("r8102e: setting up tx ring in ram...");
            for (int i = 0; i < 4; i++)
            {
                uint offset = (uint)(i * 16);
                uint txCmd = (i == 3) ? 0x40000000u : 0u;
                Write32(txDescBlock, offset, txCmd);
                Write32(txDescBlock, offset + 4, 0); // Vlan
                Write32(txDescBlock, offset + 8, (uint)txBuffers[i].Offset); // Buffer Address Low
                Write32(txDescBlock, offset + 12, (uint)(txBuffers[i].Offset >> 32)); // Buffer Address High
            }

            Console.WriteLine("r8102e: writing physical addresses to registers...");
            
            // Write TX Descriptor Start Address (0x20 Low, 0x24 High)
            mmio.DWords[0x20] = (uint)txDescBlock.Offset;
            mmio.DWords[0x24] = (uint)(txDescBlock.Offset >> 32);

            // Write RX Descriptor Start Address (0xE4 Low, 0xE8 High)
            mmio.DWords[0xE4] = (uint)rxDescBlock.Offset;
            mmio.DWords[0xE8] = (uint)(rxDescBlock.Offset >> 32);

            Console.WriteLine($"r8102e: rx ring physical addr: 0x{rxDescBlock.Offset:X8}");
            Console.WriteLine($"r8102e: tx ring physical addr: 0x{txDescBlock.Offset:X8}");
            Console.WriteLine("r8102e: phase 3 complete");
            
            return Phase4And5();
        }

        public volatile ushort LastIsr = 0;
        public volatile uint IrqCount = 0;
        private int currentRxDesc = 0;

        private uint Read32(ManagedMemoryBlock block, uint offset)
        {
            return (uint)(block[offset] | (block[offset + 1] << 8) | (block[offset + 2] << 16) | (block[offset + 3] << 24));
        }

        private bool Phase4And5()
        {
            Console.WriteLine("r8102e: enabling RX and TX in CR...");
            // Command Register (0x37): RE=0x08, TE=0x04 -> 0x0C
            mmio.Bytes[0x37] = 0x0C;

            Console.WriteLine("r8102e: verifying PHY Link Status...");
            // PHYStatus register is at offset 0x6C (8-bit)
            // Bit 1 indicates Link Status (1 = UP, 0 = DOWN)
            byte phyStatus = mmio.Bytes[0x6C];
            if ((phyStatus & 0x02) != 0)
            {
                Console.WriteLine("r8102e: PHY Link is UP! Cable connected.");
            }
            else
            {
                Console.WriteLine("r8102e: PHY Link is DOWN. Please check the network cable.");
            }

            Console.WriteLine("r8102e: masking all hardware interrupts in IMR (switching back to Polling mode)...");
            // Interrupt Mask Register (0x3C): 0x0000 ensures no hardware INTA# is generated
            // This prevents Cosmos OS deadlocks caused by single-core PIT timer blocking.
            mmio.Words[0x3C] = 0x0000;

            Console.WriteLine("r8102e: driver fully initialized and ready for HackPoll!");
            return true;
        }

        private int currentTxDesc = 0;

        public override bool QueueBytes(byte[] buffer, int offset, int length)
        {
            uint descOffset = (uint)(currentTxDesc * 16);
            uint cmdStatus = Read32(txDescBlock, descOffset);
            
            // Check if MAC still owns the descriptor
            if ((cmdStatus & 0x80000000) != 0)
                return false; // Queue full!

            // Copy data to txBuffers[currentTxDesc]
            for (int i = 0; i < length; i++)
            {
                txBuffers[currentTxDesc][(uint)i] = buffer[offset + i];
            }

            // Ethernet requires a minimum payload of 60 bytes (excluding CRC).
            // RTL8102E does NOT auto-pad runts!
            uint txLength = (uint)length;
            if (txLength < 60)
            {
                for (uint i = txLength; i < 60; i++)
                {
                    txBuffers[currentTxDesc][i] = 0; // Pad with zeros
                }
                txLength = 60;
            }

            // CommandStatus: Length | OWN (bit 31) | FS (bit 29) | LS (bit 28)
            uint newCmdStatus = txLength | 0x80000000 | 0x20000000 | 0x10000000;
            if (currentTxDesc == 3) newCmdStatus |= 0x40000000; // Preserve EOR bit
            
            Write32(txDescBlock, descOffset, newCmdStatus);
            
            // Trigger TX poll (TxPoll register 0x38 = 0x40)
            mmio.Bytes[0x38] = 0x40;
            
            currentTxDesc = (currentTxDesc + 1) % 4;
            return true;
        }

        /// <summary>
        /// HackPoll intercepts the hardware directly, bypassing Cosmos OS IRQs.
        /// This avoids full system freezes on single-core devices when Cosmos blocks.
        /// </summary>
        public void HackPoll()
        {
            // 1. Process RX Ring
            while (true)
            {
                uint offset = (uint)(currentRxDesc * 16);
                uint cmdStatus = Read32(rxDescBlock, offset);
                
                // If OWN bit (bit 31) is 1, MAC still owns it, ring is empty for us
                if ((cmdStatus & 0x80000000) != 0)
                    break;
                    
                // Bit 31 is 0! MAC transferred a packet to RAM!
                // Realtek includes the 4-byte Ethernet CRC in the length. Cosmos doesn't want it.
                uint rawLength = cmdStatus & 0x3FFF;
                uint length = rawLength > 4 ? rawLength - 4 : rawLength; 
                
                // Read into managed array for Cosmos NetworkStack
                byte[] packet = new byte[length];
                for (uint i = 0; i < length; i++)
                {
                    packet[i] = rxBuffers[currentRxDesc][i];
                }

                // Send to NetworkStack (or our custom Interceptor)
                if (DataReceived != null)
                {
                    DataReceived(packet);
                }
                
                // Give descriptor back to MAC
                uint newCmdStatus = 1536 | 0x80000000;
                if (currentRxDesc == 3) newCmdStatus |= 0x40000000; // Preserve EOR bit on last desc
                
                Write32(rxDescBlock, offset, newCmdStatus);
                
                currentRxDesc = (currentRxDesc + 1) % 4;
            }

            // 2. Read and Clear Interrupt Status Register (0x3E)
            ushort status = mmio.Words[0x3E];
            if (status != 0)
            {
                LastIsr = status;
                IrqCount++;
                
                // Acknowledge by writing the exact same bits back to ISR
                mmio.Words[0x3E] = status;
            }
        }
    }

public class CustomDHCP
    {
        private RTL8102E _driver;
        private bool _offerReceived = false;
        private byte[] _offeredIp = null;
        private byte[] _serverIp = null;

        public CustomDHCP(RTL8102E driver)
        {
            _driver = driver;
        }

        public byte[] DoAutoConfig()
        {
            Console.WriteLine("[DHCP] Starting Raw DHCP Discover...");
            
            // Hijack DataReceived
            var oldHandler = _driver.DataReceived;
            _driver.DataReceived = CustomDataReceived;

            // Craft raw DHCP Discover Packet
            byte[] discoverPacket = BuildDhcpDiscover();

            // Send initial packet
            _driver.QueueBytes(discoverPacket, 0, discoverPacket.Length);
            Console.WriteLine("[DHCP] Discover sent. Polling for Offer...");

            // Poll using HackPoll until we get an offer or timeout
            long timeout = 100000000L; // Much larger timeout for Atom CPU
            long iter = 0;
            int retries = 0;
            
            while (!_offerReceived && iter < timeout)
            {
                _driver.HackPoll();
                iter++;
                
                // Re-transmit every 20 million iterations in case PHY was still negotiating or packet dropped
                if (iter % 20000000 == 0)
                {
                    retries++;
                    Console.WriteLine($"[DHCP] Re-sending Discover (Retry {retries})...");
                    _driver.QueueBytes(discoverPacket, 0, discoverPacket.Length);
                }
            }

            // Restore normal handler
            _driver.DataReceived = oldHandler;

            if (_offerReceived && _offeredIp != null)
            {
                Console.WriteLine($"[DHCP] SUCCESS! Received IP: {_offeredIp[0]}.{_offeredIp[1]}.{_offeredIp[2]}.{_offeredIp[3]}");
                return _offeredIp;
            }
            
            Console.WriteLine("[DHCP] TIMEOUT! No DHCP Offer received.");
            return null;
        }

        private void CustomDataReceived(byte[] packet)
        {
            if (packet.Length < 14) return;
            
            ushort etherType = (ushort)((packet[12] << 8) | packet[13]);
            if (etherType == 0x0806)
            {
                Console.WriteLine($"[RX] ARP Packet length {packet.Length}");
                return; // ignore ARP for now
            }
            
            if (etherType != 0x0800) return; // Not IPv4
            
            if (packet.Length < 34) return;
            byte protocol = packet[23];
            
            if (protocol != 17) 
            {
                // Not UDP
                return;
            }

            ushort srcPort = (ushort)((packet[34] << 8) | packet[35]);
            ushort dstPort = (ushort)((packet[36] << 8) | packet[37]);
            
            Console.WriteLine($"[RX] UDP Packet {srcPort}->{dstPort} (Len: {packet.Length})");

            if (dstPort != 68) return;

            // Check DHCP Op (2 = BootReply)
            if (packet[42] != 2) return;

            // Check XID (0x12345678)
            if (packet[46] != 0x12 || packet[47] != 0x34 || packet[48] != 0x56 || packet[49] != 0x78) return;

            // Extract Your (Client) IP Address (YIAddr) at offset 58
            _offeredIp = new byte[] { packet[58], packet[59], packet[60], packet[61] };
            _serverIp = new byte[] { packet[62], packet[63], packet[64], packet[65] }; // Next Server IP
            
            _offerReceived = true;
        }

        private byte[] BuildDhcpDiscover()
        {
            byte[] packet = new byte[342];
            byte[] mac = _driver.MACAddress.bytes;

            // 1. Ethernet Header (14 bytes)
            for (int i = 0; i < 6; i++) packet[i] = 0xFF; // Dst MAC: Broadcast
            for (int i = 0; i < 6; i++) packet[6 + i] = mac[i]; // Src MAC
            packet[12] = 0x08; packet[13] = 0x00; // EtherType: IPv4

            // 2. IPv4 Header (20 bytes)
            packet[14] = 0x45; // Version/IHL
            packet[15] = 0x00; // TOS
            packet[16] = 0x01; packet[17] = 0x48; // Total Length: 328
            packet[18] = 0x00; packet[19] = 0x00; // ID
            packet[20] = 0x00; packet[21] = 0x00; // Flags/Frag
            packet[22] = 0x40; // TTL: 64
            packet[23] = 17;   // Protocol: UDP
            packet[24] = 0x00; packet[25] = 0x00; // Checksum (0 for now)
            // Src IP: 0.0.0.0 (already 0)
            packet[30] = 0xFF; packet[31] = 0xFF; packet[32] = 0xFF; packet[33] = 0xFF; // Dst IP: 255.255.255.255
            
            CalculateIPv4Checksum(packet, 14);

            // 3. UDP Header (8 bytes)
            packet[34] = 0x00; packet[35] = 68; // Src Port: 68
            packet[36] = 0x00; packet[37] = 67; // Dst Port: 67
            packet[38] = 0x01; packet[39] = 0x34; // UDP Length: 308
            packet[40] = 0x00; packet[41] = 0x00; // UDP Checksum (0 = disabled)

            // 4. DHCP Payload (300 bytes)
            packet[42] = 1; // Op: BootRequest
            packet[43] = 1; // HType: Ethernet
            packet[44] = 6; // HLen: MAC length
            packet[45] = 0; // Hops
            
            // XID: 0x12345678
            packet[46] = 0x12; packet[47] = 0x34; packet[48] = 0x56; packet[49] = 0x78;
            
            packet[50] = 0x00; packet[51] = 0x00; // Secs
            packet[52] = 0x80; packet[53] = 0x00; // Flags: Broadcast

            // CHAddr (Client Hardware Address) at offset 70
            for (int i = 0; i < 6; i++) packet[70 + i] = mac[i];

            // Magic Cookie at offset 278
            packet[278] = 0x63; packet[279] = 0x82; packet[280] = 0x53; packet[281] = 0x63;

            // Option 53: DHCP Message Type (Discover = 1)
            packet[282] = 53;
            packet[283] = 1;
            packet[284] = 1; // Discover

            // Option 61: Client Identifier
            packet[285] = 61; // Option 61
            packet[286] = 7;  // Length
            packet[287] = 1;  // Hardware type (Ethernet)
            for (int i = 0; i < 6; i++) packet[288 + i] = mac[i];

            // Option 55: Parameter Request List
            packet[294] = 55; // Option 55
            packet[295] = 3;  // Length
            packet[296] = 1;  // Subnet Mask
            packet[297] = 3;  // Router (Gateway)
            packet[298] = 6;  // DNS

            // End Option
            packet[299] = 255;

            return packet;
        }

        private void CalculateIPv4Checksum(byte[] packet, int offset)
        {
            uint sum = 0;
            for (int i = 0; i < 20; i += 2)
            {
                sum += (uint)((packet[offset + i] << 8) | packet[offset + i + 1]);
            }
            while ((sum >> 16) != 0)
            {
                sum = (sum & 0xFFFF) + (sum >> 16);
            }
            sum = ~sum;
            packet[offset + 10] = (byte)((sum >> 8) & 0xFF);
            packet[offset + 11] = (byte)(sum & 0xFF);
        }
    }

}



