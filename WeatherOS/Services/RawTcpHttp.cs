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
        private static byte[] _rxBuffer = new byte[8192];
        private static int _rxLength = 0;

        // O tipo do delegate no UserKit 2022
        private static Cosmos.HAL.DataReceivedHandler _oldHandler;

        public static string FetchGet(Address destIp, Address gatewayIp, string requestString)
        {
            _synAckReceived = false;
            _finReceived = false;
            _rxLength = 0;
            
            var nic = NetworkDevice.Devices[0];
            
            // 1. Obter MACs e IPs
            _macSource = nic.MACAddress.bytes;
            _ipSource = Cosmos.System.Network.Config.NetworkConfiguration.CurrentAddress.ToByteArray();
            _ipDest = destIp.ToByteArray();

            // Usamos o MAC padrao do VirtualBox NAT Gateway (já que ARP interno é inacessível)
            _macDest = new byte[] { 0x52, 0x54, 0x00, 0x12, 0x35, 0x02 };

            // 2. Raptar a Placa de Rede
            _oldHandler = nic.DataReceived;
            nic.DataReceived = CustomDataReceived;

            try
            {
                // 3. Enviar SYN com Retransmissao (Resiliencia Bare-Metal)
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    SendTcpPacket(nic, 0x02, new byte[0]); // SYN
                    
                    int timeout = 0;
                    while (!_synAckReceived && timeout < 30000000)
                    {
                        timeout++;
                    }
                    if (_synAckReceived) break;
                }

                if (!_synAckReceived) return null;

                // 5. Enviar ACK + HTTP Payload
                byte[] payload = System.Text.Encoding.ASCII.GetBytes(requestString);
                SendTcpPacket(nic, 0x18, payload); // ACK + PSH

                // 6. Esperar FIN ou timeout (dados)
                int timeout2 = 0;
                while (!_finReceived && timeout2 < 100000000)
                {
                    timeout2++;
                }

                // 7. Descodificar a string FORA da interrupcao (Seguro para Memory Manager)
                byte[] validData = new byte[_rxLength];
                Array.Copy(_rxBuffer, 0, validData, 0, _rxLength);
                string responseStr = System.Text.Encoding.ASCII.GetString(validData);

                // Extrair apenas o body (ignorar HTTP headers)
                int bodyIdx = responseStr.IndexOf("\r\n\r\n");
                if (bodyIdx != -1)
                {
                    return responseStr.Substring(bodyIdx + 4);
                }
                return responseStr;
            }
            finally
            {
                // 8. Devolver a placa de rede ao Kernel
                nic.DataReceived = _oldHandler;
                _sourcePort++; // Incrementar porta local para proximos pedidos
            }
        }

        private static void SendTcpPacket(NetworkDevice nic, byte tcpFlags, byte[] payload)
        {
            int tcpLen = 20 + payload.Length;
            int ipLen = 20 + tcpLen;
            int frameLen = 14 + ipLen;
            byte[] frame = new byte[frameLen];

            // --- ETHERNET HEADER (14 bytes) ---
            Array.Copy(_macDest, 0, frame, 0, 6);
            Array.Copy(_macSource, 0, frame, 6, 6);
            frame[12] = 0x08; frame[13] = 0x00; // IPv4

            // --- IPV4 HEADER (20 bytes) ---
            frame[14] = 0x45; // Version 4, IHL 5
            frame[15] = 0x00; // TOS
            frame[16] = (byte)(ipLen >> 8); frame[17] = (byte)(ipLen & 0xFF); // Total Length
            frame[18] = 0x12; frame[19] = 0x34; // ID
            frame[20] = 0x40; frame[21] = 0x00; // Flags (Don't fragment)
            frame[22] = 0x40; // TTL (64)
            frame[23] = 0x06; // Protocol (TCP)
            Array.Copy(_ipSource, 0, frame, 26, 4);
            Array.Copy(_ipDest, 0, frame, 30, 4);

            // IP Checksum
            int ipChecksum = CalculateChecksum(frame, 14, 20);
            frame[24] = (byte)(ipChecksum >> 8); frame[25] = (byte)(ipChecksum & 0xFF);

            // --- TCP HEADER (20 bytes) ---
            int tcpOffset = 34;
            frame[tcpOffset] = (byte)(_sourcePort >> 8); frame[tcpOffset + 1] = (byte)(_sourcePort & 0xFF);
            frame[tcpOffset + 2] = 0x00; frame[tcpOffset + 3] = 0x50; // Dest Port 80
            
            frame[tcpOffset + 4] = (byte)(_sequence >> 24); frame[tcpOffset + 5] = (byte)(_sequence >> 16);
            frame[tcpOffset + 6] = (byte)(_sequence >> 8); frame[tcpOffset + 7] = (byte)(_sequence & 0xFF);
            
            frame[tcpOffset + 8] = (byte)(_acknowledgment >> 24); frame[tcpOffset + 9] = (byte)(_acknowledgment >> 16);
            frame[tcpOffset + 10] = (byte)(_acknowledgment >> 8); frame[tcpOffset + 11] = (byte)(_acknowledgment & 0xFF);

            frame[tcpOffset + 12] = 0x50; // Data offset (5 * 4 = 20)
            frame[tcpOffset + 13] = tcpFlags; // Flags
            
            frame[tcpOffset + 14] = 0xFA; frame[tcpOffset + 15] = 0xF0; // Window (64240)

            if (payload.Length > 0)
            {
                Array.Copy(payload, 0, frame, 54, payload.Length);
            }

            // TCP Pseudo-Header Checksum
            byte[] pseudo = new byte[12 + tcpLen];
            Array.Copy(_ipSource, 0, pseudo, 0, 4);
            Array.Copy(_ipDest, 0, pseudo, 4, 4);
            pseudo[8] = 0;
            pseudo[9] = 0x06;
            pseudo[10] = (byte)(tcpLen >> 8); pseudo[11] = (byte)(tcpLen & 0xFF);
            Array.Copy(frame, 34, pseudo, 12, tcpLen);

            int tcpChecksum = CalculateChecksum(pseudo, 0, pseudo.Length);
            frame[tcpOffset + 16] = (byte)(tcpChecksum >> 8); frame[tcpOffset + 17] = (byte)(tcpChecksum & 0xFF);

            nic.QueueBytes(frame);
        }

        private static int CalculateChecksum(byte[] buffer, int offset, int length)
        {
            long sum = 0;
            int i = 0;
            while (length > 1)
            {
                sum += (uint)((buffer[offset + i] << 8) | buffer[offset + i + 1]);
                i += 2;
                length -= 2;
            }
            if (length > 0)
            {
                sum += (uint)(buffer[offset + i] << 8);
            }
            while ((sum >> 16) != 0)
            {
                sum = (sum & 0xFFFF) + (sum >> 16);
            }
            return (ushort)(~sum);
        }

        private static void CustomDataReceived(byte[] packet)
        {
            if (packet == null || packet.Length < 54) 
            {
                _oldHandler?.Invoke(packet);
                return;
            }
            
            // Check Ethernet Type (0x0800 IPv4)
            if (packet[12] != 0x08 || packet[13] != 0x00)
            {
                _oldHandler?.Invoke(packet);
                return;
            }

            // Check Protocol (0x06 TCP)
            if (packet[23] != 0x06)
            {
                _oldHandler?.Invoke(packet);
                return;
            }

            // Check Dest IP
            if (packet[30] != _ipSource[0] || packet[31] != _ipSource[1] || packet[32] != _ipSource[2] || packet[33] != _ipSource[3])
            {
                _oldHandler?.Invoke(packet);
                return;
            }

            int ipHeaderLen = (packet[14] & 0x0F) * 4;
            int tcpOffset = 14 + ipHeaderLen;

            // Check Dest Port
            int destPort = (packet[tcpOffset + 2] << 8) | packet[tcpOffset + 3];
            if (destPort != _sourcePort)
            {
                _oldHandler?.Invoke(packet);
                return;
            }

            byte flags = packet[tcpOffset + 13];
            uint remoteSeq = (uint)((packet[tcpOffset + 4] << 24) | (packet[tcpOffset + 5] << 16) | (packet[tcpOffset + 6] << 8) | packet[tcpOffset + 7]);
            
            int tcpHeaderLen = (packet[tcpOffset + 12] >> 4) * 4;
            int payloadOffset = tcpOffset + tcpHeaderLen;
            int payloadLen = packet.Length - payloadOffset;

            // SYN-ACK (0x12)
            if ((flags & 0x12) == 0x12 && !_synAckReceived)
            {
                _acknowledgment = remoteSeq + 1;
                _sequence += 1;
                _synAckReceived = true;
            }
            // PSH-ACK ou ACK com dados
            else if (payloadLen > 0)
            {
                _acknowledgment = remoteSeq + (uint)payloadLen;
                
                // COPIA DIRETA PARA BUFFER ESTATICO - ZERO ALOCACOES!
                if (_rxLength + payloadLen < 8192)
                {
                    Array.Copy(packet, payloadOffset, _rxBuffer, _rxLength, payloadLen);
                    _rxLength += payloadLen;
                }
                
                // Enviar ACK
                var nic = NetworkDevice.Devices[0];
                SendTcpPacket(nic, 0x10, new byte[0]); // ACK
            }
            
            // FIN (0x01)
            if ((flags & 0x01) == 0x01)
            {
                _acknowledgment = remoteSeq + 1;
                var nic = NetworkDevice.Devices[0];
                SendTcpPacket(nic, 0x10, new byte[0]); // ACK ao FIN
                _finReceived = true;
            }
        }
    }
}
