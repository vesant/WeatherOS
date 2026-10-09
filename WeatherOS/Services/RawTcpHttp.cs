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

        public static string FetchGet(Address destIp, Address gatewayIp, string requestString) {
            _synAckReceived = false;
            _finReceived = false;
            _rxLength = 0;
            
            var nic = NetworkDevice.Devices[0];
            
            // 1. Obter MACs e IPs
            _macSource = nic.MACAddress.bytes;
            _ipSource = Cosmos.System.Network.Config.NetworkConfiguration.CurrentAddress.ToByteArray();
            _ipDest = destIp.ToByteArray();

            // Endereço MAC Real do Router Físico do User (64-64-4a-ba-df-a4)
            _macDest = new byte[] { 0x64, 0x64, 0x4A, 0xBA, 0xDF, 0xA4 };

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
                    while (!_synAckReceived && timeout < 20000000L)
                    {
                        WeatherOS.Kernel.CustomNIC?.HackPoll();
                        timeout++;
                    }
                    if (_synAckReceived) break;
                }

                if (!_synAckReceived) return null;

                // 5. Enviar ACK + HTTP Payload
                byte[] payload = System.Text.Encoding.ASCII.GetBytes(requestString);
                SendTcpPacket(nic, 0x18, payload); // ACK + PSH
                _sequence += (uint)payload.Length;

                // 6. Esperar FIN ou timeout (dados)
                long timeout2 = 0;
                while (!_finReceived && timeout2 < 40000000L)
                {
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
                    return responseStr.Substring(bodyIdx + 4);
                }
                return responseStr;
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
            if (packet != null) TotalRxBytes += (uint)packet.Length;
            
            if (packet == null || packet.Length < 54) { _oldHandler?.Invoke(packet); return; }
            if (packet[12] != 0x08 || packet[13] != 0x00) { _oldHandler?.Invoke(packet); return; } // Not IPv4
            if (packet[23] != 0x06) { _oldHandler?.Invoke(packet); return; } // Not TCP
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


