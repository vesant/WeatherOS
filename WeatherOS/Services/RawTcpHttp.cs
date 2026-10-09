using System;
using Cosmos.HAL;
using Cosmos.System.Network.IPv4;

namespace WeatherOS.Services
{
    public static class RawTcpHttp
    {
        private static byte[] _macSource = new byte[6];
        private static byte[] _macDest = new byte[6];
        private static byte[] _ipSource = new byte[4];
        private static byte[] _ipDest = new byte[4];
        private static ushort _sourcePort = 54321;
        private static uint _sequence = 1000;
        private static uint _acknowledgment = 0;

        private static bool _synAckReceived = false;
        private static bool _finReceived = false;
        
        // Static buffers to avoid memory allocation inside IRQ!
        private static byte[] _rxBuffer = new byte[65536];
        private static int _rxLength = 0;

        // O tipo do delegate no UserKit 2022
        private static Cosmos.HAL.DataReceivedHandler _oldHandler;

        public static uint TotalRxBytes = 0;
                public static uint TotalTxBytes = 0; private static ushort _ipId = 1;
        public static bool _dhcpOfferReceived = false;
        public static bool _arpReplyReceived = false;
        public static byte[] _gatewayIp = new byte[4];

        public static void DoAutoConfig(Cosmos.HAL.NetworkDevice nic)
        {
            _macSource = nic.MACAddress.bytes;
            _ipSource = new byte[] { 0, 0, 0, 0 };
            _macDest = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
            _oldHandler = nic.DataReceived;
            nic.DataReceived = CustomDataReceived;

            // 1. DHCP Discover
            System.Console.WriteLine("[net] raw_tcp: broadcasting DHCP Discover...");
            _dhcpOfferReceived = false;
            byte[] dhcp = new byte[342];
            for(int i=0; i<6; i++) dhcp[i] = 0xFF;
            Array.Copy(_macSource, 0, dhcp, 6, 6);
            dhcp[12] = 0x08; dhcp[13] = 0x00; // IPv4
            dhcp[14] = 0x45; dhcp[15] = 0x00;
            dhcp[16] = 0x01; dhcp[17] = 0x48; // Total Length (328)
            dhcp[18] = 0x00; dhcp[19] = 0x00; // ID
            dhcp[20] = 0x00; dhcp[21] = 0x00; // Flags/Frag
            dhcp[22] = 0x40; // TTL
            dhcp[23] = 17; // UDP
            dhcp[24] = 0x00; dhcp[25] = 0x00; // Checksum
            // Src IP 0.0.0.0
            dhcp[30] = 0xFF; dhcp[31] = 0xFF; dhcp[32] = 0xFF; dhcp[33] = 0xFF; // Dest 255.255.255.255
            
            int ipSum = CalculateChecksum(dhcp, 14, 20);
            dhcp[24] = (byte)(ipSum >> 8); dhcp[25] = (byte)(ipSum & 0xFF);

            // UDP
            dhcp[34] = 0x00; dhcp[35] = 68; // Src Port
            dhcp[36] = 0x00; dhcp[37] = 67; // Dest Port
            dhcp[38] = 0x01; dhcp[39] = 0x34; // UDP Length (308)
            dhcp[40] = 0x00; dhcp[41] = 0x00; // Checksum (0 is allowed for UDP IPv4)

            // DHCP
            dhcp[42] = 1; // BootRequest
            dhcp[43] = 1; // Ethernet
            dhcp[44] = 6; // MAC Len
            dhcp[45] = 0; // Hops
            dhcp[46] = 0x12; dhcp[47] = 0x34; dhcp[48] = 0x56; dhcp[49] = 0x78; // XID
            dhcp[52] = 0x80; dhcp[53] = 0x00; // Broadcast flag
            Array.Copy(_macSource, 0, dhcp, 70, 6); // Client MAC
            dhcp[278] = 0x63; dhcp[279] = 0x82; dhcp[280] = 0x53; dhcp[281] = 0x63; // Magic
            dhcp[282] = 53; dhcp[283] = 1; dhcp[284] = 1; // Discover
            dhcp[285] = 255; // End

            nic.QueueBytes(dhcp);

            long timeout = 0;
            while (!_dhcpOfferReceived && timeout < 500000000L) { if (timeout % 5000000 == 0) System.Console.Write(".");
                        if (timeout % 5000000 == 0) System.Console.Write(".");
                        WeatherOS.Kernel.CustomNIC?.HackPoll();
                        timeout++; }
            
            if (!_dhcpOfferReceived) {
                System.Console.WriteLine("[net] raw_tcp: DHCP failed! Falling back to 192.168.1.100.");
                _ipSource = new byte[] { 192, 168, 1, 100 };
                _gatewayIp = new byte[] { 192, 168, 1, 1 };
            } else {
                System.Console.WriteLine($"[net] raw_tcp: DHCP OK! IP: {_ipSource[0]}.{_ipSource[1]}.{_ipSource[2]}.{_ipSource[3]}");
            }

            // 2. ARP Request for Gateway
            System.Console.WriteLine($"[net] raw_tcp: ARP Request for Gateway {_gatewayIp[0]}.{_gatewayIp[1]}.{_gatewayIp[2]}.{_gatewayIp[3]}...");
            _arpReplyReceived = false;
            byte[] arp = new byte[42];
            for(int i=0; i<6; i++) arp[i] = 0xFF;
            Array.Copy(_macSource, 0, arp, 6, 6);
            arp[12] = 0x08; arp[13] = 0x06;
            arp[14] = 0x00; arp[15] = 0x01;
            arp[16] = 0x08; arp[17] = 0x00;
            arp[18] = 0x06; arp[19] = 0x04;
            arp[20] = 0x00; arp[21] = 0x01;
            Array.Copy(_macSource, 0, arp, 22, 6);
            Array.Copy(_ipSource, 0, arp, 28, 4);
            for(int i=0; i<6; i++) arp[32+i] = 0x00;
            Array.Copy(_gatewayIp, 0, arp, 38, 4);

            nic.QueueBytes(arp);

            timeout = 0;
            while (!_arpReplyReceived && timeout < 500000000L) { if (timeout % 5000000 == 0) System.Console.Write(".");
                        if (timeout % 5000000 == 0) System.Console.Write(".");
                        WeatherOS.Kernel.CustomNIC?.HackPoll();
                        timeout++; }

            if (!_arpReplyReceived) {
                System.Console.WriteLine("[net] raw_tcp: ARP failed! Router might ignore us.");
            } else {
                System.Console.WriteLine("$[net] raw_tcp: ARP OK! Router MAC: {_macDest[0]:X2}:{_macDest[1]:X2}:{_macDest[2]:X2}:{_macDest[3]:X2}:{_macDest[4]:X2}:{_macDest[5]:X2}");
            }
            nic.DataReceived = _oldHandler;
        }


        public static string FetchGet(Address destIp, Address gatewayIp, string requestString) {
            _synAckReceived = false;
            _finReceived = false;
            _rxLength = 0;
            
            var nic = NetworkDevice.Devices[0];
            
            // IPs and MACs already configured by DoAutoConfig()
            _ipDest = destIp.ToByteArray();

            _oldHandler = nic.DataReceived;
            nic.DataReceived = CustomDataReceived;
            SendArpWakeup(nic);

            try
            {
                // 3. Enviar SYN com Retransmissao
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    SendTcpPacket(nic, 0x02, new byte[0]); // SYN
                    
                    long timeout = 0;
                    while (!_synAckReceived && timeout < 200000000L)
                    {
                        if (timeout % 5000000 == 0) System.Console.Write(".");
                        WeatherOS.Kernel.CustomNIC?.HackPoll();
                        timeout++;
                    }
                    if (_synAckReceived) break;
                }

                if (!_synAckReceived) { nic.DataReceived = _oldHandler; return null; }

                // 5. Enviar ACK + HTTP Payload
                byte[] payload = System.Text.Encoding.ASCII.GetBytes(requestString);
                SendTcpPacket(nic, 0x18, payload); // ACK + PSH
                _sequence += (uint)payload.Length;

                // 6. Esperar FIN ou timeout (dados)
                long timeout2 = 0;
                while (!_finReceived && timeout2 < 400000000L)
                {
                    if (timeout2 % 5000000 == 0) System.Console.Write(".");
                        WeatherOS.Kernel.CustomNIC?.HackPoll();
                        timeout2++;
                }

                // 7. Descodificar a string
                byte[] validData = new byte[_rxLength];
                Array.Copy(_rxBuffer, 0, validData, 0, _rxLength);
                string responseStr = System.Text.Encoding.ASCII.GetString(validData);

                // Extrair apenas o body
                int bodyIdx = CustomIndexOf(responseStr, "\r\n\r\n");
                if (bodyIdx != -1)
                {
                    nic.DataReceived = _oldHandler; return responseStr.Substring(bodyIdx + 4);
                }
                nic.DataReceived = _oldHandler; return responseStr;
            }
            finally
            {
                nic.DataReceived = _oldHandler;
                _sourcePort++;
            }
        }

        private static int CustomIndexOf(string source, string search, int startIndex = 0)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(search)) return -1;
            if (startIndex < 0 || startIndex >= source.Length) return -1;
            int searchLen = search.Length;
            int maxIdx = source.Length - searchLen;
            for (int i = startIndex; i <= maxIdx; i++)
            {
                bool match = true;
                for (int j = 0; j < searchLen; j++)
                {
                    if (source[i + j] != search[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }

                private static void SendArpWakeup(Cosmos.HAL.NetworkDevice nic)
        {
            byte[] arp = new byte[42];
            for(int i=0; i<6; i++) arp[i] = 0xFF;
            Array.Copy(_macSource, 0, arp, 6, 6);
            arp[12] = 0x08; arp[13] = 0x06;
            arp[14] = 0x00; arp[15] = 0x01;
            arp[16] = 0x08; arp[17] = 0x00;
            arp[18] = 0x06; arp[19] = 0x04;
            arp[20] = 0x00; arp[21] = 0x01;
            Array.Copy(_macSource, 0, arp, 22, 6);
            Array.Copy(_ipSource, 0, arp, 28, 4);
            for(int i=0; i<6; i++) arp[32+i] = 0x00;
            arp[38] = 192; arp[39] = 168; arp[40] = 31; arp[41] = 1;
            nic.QueueBytes(arp);
        }

        private static void SendTcpPacket(Cosmos.HAL.NetworkDevice nic, byte tcpFlags, byte[] payload)
        {
            bool isSyn = (tcpFlags & 0x02) != 0;
            int tcpOptionsLen = isSyn ? 12 : 0;
            int tcpLen = 20 + tcpOptionsLen + payload.Length;
            int ipLen = 20 + tcpLen;
            int frameLen = 14 + ipLen;
            byte[] frame = new byte[frameLen];

            // ETHERNET
            Array.Copy(_macDest, 0, frame, 0, 6);
            Array.Copy(_macSource, 0, frame, 6, 6);
            frame[12] = 0x08; frame[13] = 0x00;

            // IPV4
            frame[14] = 0x45; frame[15] = 0x00;
            frame[16] = (byte)(ipLen >> 8); frame[17] = (byte)(ipLen & 0xFF);
            frame[18] = 0x12; frame[19] = 0x34;
            frame[20] = 0x40; frame[21] = 0x00;
            frame[22] = 0x40; frame[23] = 0x06;
            Array.Copy(_ipSource, 0, frame, 26, 4);
            Array.Copy(_ipDest, 0, frame, 30, 4);

            int ipChecksum = CalculateChecksum(frame, 14, 20);
            frame[24] = (byte)(ipChecksum >> 8); frame[25] = (byte)(ipChecksum & 0xFF);

            // TCP
            int tcpOffset = 34;
            frame[tcpOffset] = (byte)(_sourcePort >> 8); frame[tcpOffset + 1] = (byte)(_sourcePort & 0xFF);
            frame[tcpOffset + 2] = 0x00; frame[tcpOffset + 3] = 0x50;
            
            frame[tcpOffset + 4] = (byte)(_sequence >> 24); frame[tcpOffset + 5] = (byte)(_sequence >> 16);
            frame[tcpOffset + 6] = (byte)(_sequence >> 8); frame[tcpOffset + 7] = (byte)(_sequence & 0xFF);
            
            uint ackToSend = isSyn ? 0 : _acknowledgment; frame[tcpOffset + 8] = (byte)(ackToSend >> 24); frame[tcpOffset + 9] = (byte)(ackToSend >> 16);
            frame[tcpOffset + 10] = (byte)(ackToSend >> 8); frame[tcpOffset + 11] = (byte)(ackToSend & 0xFF);

            int dataOffset = 5 + (tcpOptionsLen / 4);
            frame[tcpOffset + 12] = (byte)(dataOffset << 4);
            frame[tcpOffset + 13] = tcpFlags;
            frame[tcpOffset + 14] = 0xFA; frame[tcpOffset + 15] = 0xF0;

            int currentOffset = tcpOffset + 20;

            if (isSyn)
            {
                // MSS
                frame[currentOffset++] = 0x02; 
                frame[currentOffset++] = 0x04; 
                frame[currentOffset++] = 0x05; 
                frame[currentOffset++] = 0xB4;
                // NOP
                frame[currentOffset++] = 0x01;
                // Window Scale (8)
                frame[currentOffset++] = 0x03;
                frame[currentOffset++] = 0x03;
                frame[currentOffset++] = 0x08;
                // NOP, NOP
                frame[currentOffset++] = 0x01;
                frame[currentOffset++] = 0x01;
                // SACK Permitted
                frame[currentOffset++] = 0x04;
                frame[currentOffset++] = 0x02;
            }

            if (payload.Length > 0)
            {
                Array.Copy(payload, 0, frame, currentOffset, payload.Length);
            }

            byte[] pseudo = new byte[12 + tcpLen];
            Array.Copy(_ipSource, 0, pseudo, 0, 4);
            Array.Copy(_ipDest, 0, pseudo, 4, 4);
            pseudo[8] = 0; pseudo[9] = 0x06;
            pseudo[10] = (byte)(tcpLen >> 8); pseudo[11] = (byte)(tcpLen & 0xFF);
            Array.Copy(frame, 34, pseudo, 12, tcpLen);

            int tcpChecksum = CalculateChecksum(pseudo, 0, pseudo.Length);
            frame[tcpOffset + 16] = (byte)(tcpChecksum >> 8); frame[tcpOffset + 17] = (byte)(tcpChecksum & 0xFF);

            TotalTxBytes += (uint)frame.Length;
            if (!nic.QueueBytes(frame))
            {
                System.Console.WriteLine("\n[net] ERROR: Tx Ring Full!");
                WeatherOS.Kernel.CustomNIC?.HackPoll();
            }
        }

        private static int CalculateChecksum(byte[] buffer, int offset, int length)
        {
            long sum = 0;
            int i = 0;
            while (length > 1) { sum += (uint)((buffer[offset + i] << 8) | buffer[offset + i + 1]); i += 2; length -= 2; }
            if (length > 0) sum += (uint)(buffer[offset + i] << 8);
            while ((sum >> 16) != 0) sum = (sum & 0xFFFF) + (sum >> 16);
            return (ushort)(~sum);
        }

                private static void CustomDataReceived(byte[] packet)
        {
            if (_oldHandler == CustomDataReceived) _oldHandler = null; // STOP RECURSION FATAL BUG
            if (packet != null) TotalRxBytes += (uint)packet.Length;
            if (packet == null || packet.Length < 42) { _oldHandler?.Invoke(packet); return; }

            // Is ARP?
            if (packet[12] == 0x08 && packet[13] == 0x06)
            {
                if (packet[20] == 0x00 && packet[21] == 0x02)
                {
                    if (packet[28] == _gatewayIp[0] && packet[29] == _gatewayIp[1] &&
                        packet[30] == _gatewayIp[2] && packet[31] == _gatewayIp[3])
                    {
                        Array.Copy(packet, 22, _macDest, 0, 6);
                        _arpReplyReceived = true;
                    }
                }
                return;
            }

            if (packet[12] != 0x08 || packet[13] != 0x00) { _oldHandler?.Invoke(packet); return; } // Not IPv4

            // Is UDP?
            if (packet[23] == 17)
            {
                int ipHdrLen = (packet[14] & 0x0F) * 4;
                int udpOff = 14 + ipHdrLen;
                if (packet.Length >= udpOff + 8 && packet[udpOff + 2] == 0x00 && packet[udpOff + 3] == 0x44)
                {
                    int dhcpOff = udpOff + 8;
                    if (packet.Length >= dhcpOff + 240 && packet[dhcpOff + 236] == 0x63 && packet[dhcpOff + 237] == 0x82)
                    {
                        Array.Copy(packet, dhcpOff + 16, _ipSource, 0, 4);
                        int optIdx = dhcpOff + 240;
                        while (optIdx < packet.Length && packet[optIdx] != 255)
                        {
                            byte opt = packet[optIdx];
                            if (opt == 0) { optIdx++; continue; }
                            byte len = packet[optIdx + 1];
                            if (opt == 3 && len >= 4) Array.Copy(packet, optIdx + 2, _gatewayIp, 0, 4);
                            optIdx += 2 + len;
                        }
                        if (_gatewayIp[0] == 0) { Array.Copy(_ipSource, 0, _gatewayIp, 0, 4); _gatewayIp[3] = 1; }
                        _dhcpOfferReceived = true;
                    }
                }
                return;
            }

            if (packet[23] != 0x06) { _oldHandler?.Invoke(packet); return; } // Not TCP
            if (packet.Length < 54) { _oldHandler?.Invoke(packet); return; }
            if (packet[30] != _ipSource[0] || packet[31] != _ipSource[1] || packet[32] != _ipSource[2] || packet[33] != _ipSource[3]) { _oldHandler?.Invoke(packet); return; }

            int ipHeaderLen = (packet[14] & 0x0F) * 4;
            int ipTotalLen = (packet[16] << 8) | packet[17];

            int tcpOffset = 14 + ipHeaderLen;

            int destPort = (packet[tcpOffset + 2] << 8) | packet[tcpOffset + 3];
            if (destPort != _sourcePort) { _oldHandler?.Invoke(packet); return; }

            byte flags = packet[tcpOffset + 13];
            uint remoteSeq = (uint)((packet[tcpOffset + 4] << 24) | (packet[tcpOffset + 5] << 16) | (packet[tcpOffset + 6] << 8) | packet[tcpOffset + 7]);
            
            int tcpHeaderLen = (packet[tcpOffset + 12] >> 4) * 4;
            int payloadOffset = tcpOffset + tcpHeaderLen;
            int payloadLen = ipTotalLen - ipHeaderLen - tcpHeaderLen;

            // SYN-ACK (0x12)
            if ((flags & 0x12) == 0x12 && !_synAckReceived)
            {
                _acknowledgment = remoteSeq + 1;
                _sequence += 1;
                _synAckReceived = true;
            }
            // ACK or PSH-ACK
            else if ((flags & 0x10) == 0x10 && _synAckReceived)
            {
                if (payloadLen > 0)
                {
                    _acknowledgment = remoteSeq + (uint)payloadLen;
                    
                    if (_rxLength + payloadLen < 65536) // Increased buffer for large JSON
                    {
                        Array.Copy(packet, payloadOffset, _rxBuffer, _rxLength, payloadLen);
                        _rxLength += payloadLen;
                    }
                    
                    var nic = NetworkDevice.Devices[0];
                    SendTcpPacket(nic, 0x10, new byte[0]); // ACK
                }
            }
            
            // FIN
            if ((flags & 0x01) == 0x01 || (flags & 0x11) == 0x11)
            {
                _acknowledgment = remoteSeq + 1;
                var nic = NetworkDevice.Devices[0];
                SendTcpPacket(nic, 0x10, new byte[0]); // ACK ao FIN
                _finReceived = true;
            }
        }
    }
}














