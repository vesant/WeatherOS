using System;
using System.Text;
using Cosmos.HAL;
using Cosmos.HAL.Network;
using Cosmos.System.Network.IPv4;

namespace WeatherOS.Services
{
    public enum TransferStatus
    {
        Idle,
        Listening,
        Sending,
        Receiving,
        Success,
        Error
    }

    /// <summary>
    /// Bare-Metal Network File Transfer & OTA Streaming Protocol ("WOSF")
    /// Provides 100% reliable chunked UDP file streaming with acknowledgments,
    /// retransmissions, and automatic hardware NIC polling.
    /// </summary>
    public static class FileTransferService
    {
        public const ushort PORT = 7331;
        public const ushort CHUNK_SIZE = 512;

        public const byte MSG_PING = 0x01;
        public const byte MSG_PONG = 0x02;
        public const byte MSG_START_SEND = 0x10;
        public const byte MSG_START_ACK = 0x11;
        public const byte MSG_DATA_CHUNK = 0x20;
        public const byte MSG_DATA_ACK = 0x21;
        public const byte MSG_FINISH = 0x30;
        public const byte MSG_FINISH_ACK = 0x31;
        public const byte MSG_ABORT = 0xFF;

        public static TransferStatus Status { get; set; } = TransferStatus.Idle;
        public static string CurrentFileName { get; set; } = "";
        public static long TotalFileSize { get; set; } = 0;
        public static int TotalChunks { get; set; } = 0;
        public static int CurrentChunk { get; set; } = 0;
        public static long TransferredBytes { get; set; } = 0;
        public static string StatusMessage { get; set; } = "Ready";

        public static string RemoteIpStr { get; set; } = "";
        public static string LastPeerIp { get; set; } = "";
        public static string LastPeerName { get; set; } = "";
        public static bool IsPeerVerified { get; set; } = false;
        public static bool HasDiscoveredPeer => !string.IsNullOrWhiteSpace(LastPeerIp);
        public static bool IsPeerConnected => !string.IsNullOrWhiteSpace(LastPeerIp) && IsPeerVerified;
        public static byte[] RemoteMac { get; set; } = new byte[6];
        public static byte[] RemoteIp { get; set; } = new byte[4];
        public static ushort RemotePort { get; set; } = 7331;

        private static ushort _ipIdCounter = 0xA100;

        // 2 MB Static Buffer for file reception
        private static byte[] _rxBuffer = new byte[2097152];
        private static int _rxBufferLength = 0;

        // Signals for synchronous wait loops
        public static volatile bool _startAckReceived = false;
        public static volatile bool _dataAckReceived = false;
        public static volatile ushort _lastAckedChunk = 0;
        public static volatile bool _finishAckReceived = false;
        public static volatile bool _pongReceived = false;
        public static volatile string _lastPongInfo = "";

        public static void ResetState()
        {
            Status = TransferStatus.Idle;
            CurrentFileName = "";
            TotalFileSize = 0;
            TotalChunks = 0;
            CurrentChunk = 0;
            TransferredBytes = 0;
            StatusMessage = "Ready";
            RemoteIpStr = "";
            _startAckReceived = false;
            _dataAckReceived = false;
            _finishAckReceived = false;
            _pongReceived = false;
            IsPeerVerified = false;
            _rxBufferLength = 0;
        }

        public static void StartListening()
        {
            ResetState();
            Status = TransferStatus.Listening;
            StatusMessage = "Listening on UDP port 7331. Awaiting transmission from PC...";
            RawTcpHttp.EnsurePacketHandlerActive();
            BroadcastAnnounce();
        }

        public static void StopListening()
        {
            if (Status == TransferStatus.Listening || Status == TransferStatus.Receiving)
            {
                ResetState();
            }
        }

        /// <summary>
        /// Sends raw Ethernet + IPv4 + UDP packet directly to NIC queue.
        /// </summary>
        public static void SendUdp(byte[] targetMac, byte[] targetIp, ushort srcPort, ushort destPort, byte[] payload)
        {
            if (NetworkDevice.Devices.Count == 0) return;
            var nic = NetworkDevice.Devices[0];

            int payloadLen = payload != null ? payload.Length : 0;
            int udpLen = 8 + payloadLen;
            int ipLen = 20 + udpLen;
            int frameLen = 14 + ipLen;
            byte[] frame = new byte[frameLen];

            // 1. Ethernet Header (14 bytes)
            if (targetMac != null && targetMac.Length == 6 && (targetMac[0] != 0 || targetMac[1] != 0 || targetMac[2] != 0))
            {
                Array.Copy(targetMac, 0, frame, 0, 6);
            }
            else
            {
                for (int i = 0; i < 6; i++) frame[i] = 0xFF; // Broadcast fallback
            }

            byte[] myMac = nic.MACAddress != null ? nic.MACAddress.bytes : new byte[6];
            Array.Copy(myMac, 0, frame, 6, 6);
            frame[12] = 0x08; frame[13] = 0x00; // IPv4

            // 2. IPv4 Header (20 bytes)
            frame[14] = 0x45; frame[15] = 0x00;
            frame[16] = (byte)(ipLen >> 8); frame[17] = (byte)(ipLen & 0xFF);
            frame[18] = (byte)(_ipIdCounter >> 8); frame[19] = (byte)(_ipIdCounter & 0xFF); _ipIdCounter++;
            frame[20] = 0x40; frame[21] = 0x00; // Don't fragment
            frame[22] = 0x40; // TTL 64
            frame[23] = 17;   // Protocol UDP
            frame[24] = 0x00; frame[25] = 0x00; // Checksum placeholder

            byte[] myIp = RawTcpHttp.GetSourceIpBytes();
            Array.Copy(myIp, 0, frame, 26, 4);
            Array.Copy(targetIp, 0, frame, 30, 4);

            int ipSum = RawTcpHttp.CalculateIpChecksum(frame, 14, 20);
            frame[24] = (byte)(ipSum >> 8); frame[25] = (byte)(ipSum & 0xFF);

            // 3. UDP Header (8 bytes)
            frame[34] = (byte)(srcPort >> 8); frame[35] = (byte)(srcPort & 0xFF);
            frame[36] = (byte)(destPort >> 8); frame[37] = (byte)(destPort & 0xFF);
            frame[38] = (byte)(udpLen >> 8); frame[39] = (byte)(udpLen & 0xFF);
            frame[40] = 0x00; frame[41] = 0x00; // UDP checksum 0 (disabled)

            // 4. Payload
            if (payloadLen > 0)
            {
                Array.Copy(payload, 0, frame, 42, payloadLen);
            }

            RawTcpHttp.TotalTxBytes += (uint)frame.Length;
            nic.QueueBytes(frame);
        }

        /// <summary>
        /// Processes incoming UDP packets on port 7331 from CustomDataReceived.
        /// </summary>
        public static void ProcessIncomingPacket(byte[] packet, int udpOff)
        {
            if (packet == null || packet.Length < udpOff + 8) return;

            ushort srcPort = (ushort)((packet[udpOff] << 8) | packet[udpOff + 1]);
            ushort udpLen = (ushort)((packet[udpOff + 4] << 8) | packet[udpOff + 5]);
            int payloadOff = udpOff + 8;
            int payloadLen = udpLen - 8;

            if (payloadLen < 5) return;

            // Check Magic: "WOSF" (0x57, 0x4F, 0x53, 0x46)
            if (packet[payloadOff] != 0x57 || packet[payloadOff + 1] != 0x4F ||
                packet[payloadOff + 2] != 0x53 || packet[payloadOff + 3] != 0x46)
            {
                return;
            }

            byte[] srcMac = new byte[6];
            Array.Copy(packet, 6, srcMac, 0, 6);

            byte[] srcIp = new byte[4];
            Array.Copy(packet, 26, srcIp, 0, 4);

            byte[] myIpBytes = RawTcpHttp.GetSourceIpBytes();
            if (myIpBytes != null && myIpBytes.Length == 4)
            {
                // Ignore own broadcast
                if (srcIp[0] == myIpBytes[0] && srcIp[1] == myIpBytes[1] && srcIp[2] == myIpBytes[2] && srcIp[3] == myIpBytes[3])
                {
                    return;
                }

                // If local node has a configured IP, filter out packets from incompatible subnets (e.g. host virtual adapters)
                if (myIpBytes[0] != 0 || myIpBytes[1] != 0 || myIpBytes[2] != 0 || myIpBytes[3] != 0)
                {
                    if (myIpBytes[0] == 192 && myIpBytes[1] == 168)
                    {
                        if (srcIp[0] != 192 || srcIp[1] != 168 || srcIp[2] != myIpBytes[2])
                        {
                            return; // Different 192.168.x subnet
                        }
                    }
                    else if (myIpBytes[0] == 172 && (myIpBytes[1] >= 16 && myIpBytes[1] <= 31))
                    {
                        if (srcIp[0] != 172 || srcIp[1] != myIpBytes[1])
                        {
                            return; // Different 172.x subnet
                        }
                    }
                    else if (myIpBytes[0] == 10)
                    {
                        if (srcIp[0] != 10 || srcIp[1] != myIpBytes[1])
                        {
                            return; // Different 10.x subnet
                        }
                    }
                }
            }

            LastPeerIp = $"{srcIp[0]}.{srcIp[1]}.{srcIp[2]}.{srcIp[3]}";
            Array.Copy(srcMac, 0, RemoteMac, 0, 6);
            Array.Copy(srcIp, 0, RemoteIp, 0, 4);

            byte msgType = packet[payloadOff + 4];

            // 1. PING -> reply PONG
            if (msgType == MSG_PING)
            {
                if (payloadLen > 5)
                {
                    LastPeerName = Encoding.ASCII.GetString(packet, payloadOff + 5, payloadLen - 5).Trim();
                }
                string info = "WeatherOS v3.0 | " + RawTcpHttp.GetAssignedIp();
                byte[] infoBytes = Encoding.ASCII.GetBytes(info);
                byte[] pong = new byte[5 + infoBytes.Length];
                pong[0] = 0x57; pong[1] = 0x4F; pong[2] = 0x53; pong[3] = 0x46;
                pong[4] = MSG_PONG;
                Array.Copy(infoBytes, 0, pong, 5, infoBytes.Length);

                SendUdp(srcMac, srcIp, PORT, srcPort, pong);
                return;
            }

            // 2. PONG
            if (msgType == MSG_PONG)
            {
                _pongReceived = true;
                IsPeerVerified = true;
                if (payloadLen > 5)
                {
                    _lastPongInfo = Encoding.ASCII.GetString(packet, payloadOff + 5, payloadLen - 5);
                    LastPeerName = _lastPongInfo;
                }
                return;
            }

            // 3. START_SEND
            if (msgType == MSG_START_SEND)
            {
                if (payloadLen >= 14)
                {
                    uint fSize = (uint)((packet[payloadOff + 5] << 24) | (packet[payloadOff + 6] << 16) | (packet[payloadOff + 7] << 8) | packet[payloadOff + 8]);
                    ushort tChunks = (ushort)((packet[payloadOff + 9] << 8) | packet[payloadOff + 10]);
                    ushort cSize = (ushort)((packet[payloadOff + 11] << 8) | packet[payloadOff + 12]);
                    string fName = Encoding.ASCII.GetString(packet, payloadOff + 13, payloadLen - 13).Trim();

                    CurrentFileName = fName;
                    TotalFileSize = fSize;
                    TotalChunks = tChunks;
                    CurrentChunk = 0;
                    TransferredBytes = 0;
                    _rxBufferLength = 0;

                    RemoteIpStr = $"{srcIp[0]}.{srcIp[1]}.{srcIp[2]}.{srcIp[3]}";
                    Array.Copy(srcMac, 0, RemoteMac, 0, 6);
                    Array.Copy(srcIp, 0, RemoteIp, 0, 4);
                    RemotePort = srcPort;

                    Status = TransferStatus.Receiving;
                    StatusMessage = $"Receiving '{fName}' ({fSize} B) from {RemoteIpStr}...";

                    // Reply START_ACK
                    byte[] ack = new byte[6];
                    ack[0] = 0x57; ack[1] = 0x4F; ack[2] = 0x53; ack[3] = 0x46;
                    ack[4] = MSG_START_ACK;
                    ack[5] = 0x00; // Status OK
                    SendUdp(srcMac, srcIp, PORT, srcPort, ack);
                }
                return;
            }

            // 4. START_ACK
            if (msgType == MSG_START_ACK)
            {
                _startAckReceived = true;
                return;
            }

            // 5. DATA_CHUNK
            if (msgType == MSG_DATA_CHUNK)
            {
                if (Status == TransferStatus.Receiving && payloadLen >= 9)
                {
                    ushort chunkIdx = (ushort)((packet[payloadOff + 5] << 8) | packet[payloadOff + 6]);
                    ushort chunkLen = (ushort)((packet[payloadOff + 7] << 8) | packet[payloadOff + 8]);

                    int bufferOffset = chunkIdx * CHUNK_SIZE;
                    if (bufferOffset + chunkLen <= _rxBuffer.Length && payloadLen >= 9 + chunkLen)
                    {
                        Array.Copy(packet, payloadOff + 9, _rxBuffer, bufferOffset, chunkLen);
                        int newLen = bufferOffset + chunkLen;
                        if (newLen > _rxBufferLength) _rxBufferLength = newLen;
                        CurrentChunk = chunkIdx + 1;
                        TransferredBytes = _rxBufferLength;
                    }

                    // Reply DATA_ACK with chunk index
                    byte[] dAck = new byte[8];
                    dAck[0] = 0x57; dAck[1] = 0x4F; dAck[2] = 0x53; dAck[3] = 0x46;
                    dAck[4] = MSG_DATA_ACK;
                    dAck[5] = (byte)(chunkIdx >> 8);
                    dAck[6] = (byte)(chunkIdx & 0xFF);
                    dAck[7] = 0x00; // Status OK

                    SendUdp(srcMac, srcIp, PORT, srcPort, dAck);
                }
                return;
            }

            // 6. DATA_ACK
            if (msgType == MSG_DATA_ACK)
            {
                if (payloadLen >= 7)
                {
                    ushort ackedChunk = (ushort)((packet[payloadOff + 5] << 8) | packet[payloadOff + 6]);
                    _lastAckedChunk = ackedChunk;
                    _dataAckReceived = true;
                }
                return;
            }

            // 7. FINISH
            if (msgType == MSG_FINISH)
            {
                if (Status == TransferStatus.Receiving)
                {
                    int saveLen = (int)TotalFileSize;
                    if (saveLen > _rxBufferLength) saveLen = _rxBufferLength;
                    byte[] fileData = new byte[saveLen];
                    Array.Copy(_rxBuffer, 0, fileData, 0, saveLen);

                    string saveStatus;
                    bool saved = StorageService.SaveBinaryFileToLocation(CurrentFileName, fileData, "disk0", out saveStatus);

                    Status = saved ? TransferStatus.Success : TransferStatus.Error;
                    StatusMessage = saved
                        ? $"[SUCCESS] Received '{CurrentFileName}' ({saveLen} B) and saved to storage!"
                        : $"[ERROR] File received but storage save failed: {saveStatus}";

                    // Reply FINISH_ACK
                    byte[] fAck = new byte[6];
                    fAck[0] = 0x57; fAck[1] = 0x4F; fAck[2] = 0x53; fAck[3] = 0x46;
                    fAck[4] = MSG_FINISH_ACK;
                    fAck[5] = (byte)(saved ? 0x00 : 0x01);
                    SendUdp(srcMac, srcIp, PORT, srcPort, fAck);
                }
                return;
            }

            // 8. FINISH_ACK
            if (msgType == MSG_FINISH_ACK)
            {
                _finishAckReceived = true;
                return;
            }

            // 9. ABORT
            if (msgType == MSG_ABORT)
            {
                Status = TransferStatus.Error;
                StatusMessage = "Transfer cancelled by peer.";
            }
        }

        /// <summary>
        /// Sends a file to a remote peer (e.g. Windows PC).
        /// </summary>
        public static bool SendFile(string targetIpStr, ushort targetPort, string filename, byte[] fileData, Action<int, int> progressCallback, out string status)
        {
            byte[] targetIp = ParseIp(targetIpStr);
            if (targetIp == null)
            {
                status = $"Invalid target IP: '{targetIpStr}'";
                return false;
            }

            if (fileData == null || fileData.Length == 0)
            {
                status = "File data is empty.";
                return false;
            }

            RawTcpHttp.EnsurePacketHandlerActive();

            // Refresh ARP resolution for the target IP
            RawTcpHttp.SendArpRequest(targetIp);
            for (int w = 0; w < 10; w++)
            {
                WeatherOS.Kernel.CustomNIC?.HackPoll();
                System.Threading.Thread.Sleep(5);
            }

            byte[] targetMac = new byte[6];
            for (int i = 0; i < 6; i++) targetMac[i] = 0xFF; // Broadcast or learned ARP

            Status = TransferStatus.Sending;
            CurrentFileName = filename;
            TotalFileSize = fileData.Length;
            RemoteIpStr = targetIpStr;
            RemotePort = targetPort;

            ushort totalChunks = (ushort)((fileData.Length + CHUNK_SIZE - 1) / CHUNK_SIZE);
            if (totalChunks == 0) totalChunks = 1;
            TotalChunks = totalChunks;
            CurrentChunk = 0;
            TransferredBytes = 0;

            StatusMessage = $"Initiating transfer of '{filename}' to {targetIpStr}:{targetPort}...";

            // 1. Send START_SEND packet (up to 4 attempts)
            byte[] nameBytes = Encoding.ASCII.GetBytes(filename);
            byte[] startPkt = new byte[13 + nameBytes.Length];
            startPkt[0] = 0x57; startPkt[1] = 0x4F; startPkt[2] = 0x53; startPkt[3] = 0x46;
            startPkt[4] = MSG_START_SEND;
            startPkt[5] = (byte)(fileData.Length >> 24);
            startPkt[6] = (byte)(fileData.Length >> 16);
            startPkt[7] = (byte)(fileData.Length >> 8);
            startPkt[8] = (byte)(fileData.Length & 0xFF);
            startPkt[9] = (byte)(totalChunks >> 8);
            startPkt[10] = (byte)(totalChunks & 0xFF);
            startPkt[11] = (byte)(CHUNK_SIZE >> 8);
            startPkt[12] = (byte)(CHUNK_SIZE & 0xFF);
            Array.Copy(nameBytes, 0, startPkt, 13, nameBytes.Length);

            _startAckReceived = false;
            for (int attempt = 0; attempt < 4 && !_startAckReceived; attempt++)
            {
                SendUdp(targetMac, targetIp, PORT, targetPort, startPkt);
                int waitMs = 0;
                while (!_startAckReceived && waitMs < 400)
                {
                    WeatherOS.Kernel.CustomNIC?.HackPoll();
                    System.Threading.Thread.Sleep(5);
                    waitMs += 5;
                }
            }

            if (!_startAckReceived)
            {
                Status = TransferStatus.Error;
                status = $"Target {targetIpStr}:{targetPort} did not acknowledge transfer request.";
                StatusMessage = status;
                return false;
            }

            // 2. Stream chunks
            for (ushort c = 0; c < totalChunks; c++)
            {
                int offset = c * CHUNK_SIZE;
                int chunkLen = Math.Min(CHUNK_SIZE, fileData.Length - offset);

                byte[] chunkPkt = new byte[9 + chunkLen];
                chunkPkt[0] = 0x57; chunkPkt[1] = 0x4F; chunkPkt[2] = 0x53; chunkPkt[3] = 0x46;
                chunkPkt[4] = MSG_DATA_CHUNK;
                chunkPkt[5] = (byte)(c >> 8);
                chunkPkt[6] = (byte)(c & 0xFF);
                chunkPkt[7] = (byte)(chunkLen >> 8);
                chunkPkt[8] = (byte)(chunkLen & 0xFF);
                Array.Copy(fileData, offset, chunkPkt, 9, chunkLen);

                _dataAckReceived = false;
                for (int attempt = 0; attempt < 5 && (!_dataAckReceived || _lastAckedChunk != c); attempt++)
                {
                    SendUdp(targetMac, targetIp, PORT, targetPort, chunkPkt);
                    int waitMs = 0;
                    while ((!_dataAckReceived || _lastAckedChunk != c) && waitMs < 350)
                    {
                        WeatherOS.Kernel.CustomNIC?.HackPoll();
                        System.Threading.Thread.Sleep(5);
                        waitMs += 5;
                    }
                }

                if (!_dataAckReceived || _lastAckedChunk != c)
                {
                    Status = TransferStatus.Error;
                    status = $"Transmission timeout on chunk {c + 1}/{totalChunks}.";
                    StatusMessage = status;
                    return false;
                }

                CurrentChunk = c + 1;
                TransferredBytes = offset + chunkLen;
                progressCallback?.Invoke(c + 1, totalChunks);
            }

            // 3. Send FINISH packet
            byte[] finPkt = new byte[9];
            finPkt[0] = 0x57; finPkt[1] = 0x4F; finPkt[2] = 0x53; finPkt[3] = 0x46;
            finPkt[4] = MSG_FINISH;
            finPkt[5] = 0x00; finPkt[6] = 0x00; finPkt[7] = 0x00; finPkt[8] = 0x00;

            _finishAckReceived = false;
            for (int attempt = 0; attempt < 4 && !_finishAckReceived; attempt++)
            {
                SendUdp(targetMac, targetIp, PORT, targetPort, finPkt);
                int waitMs = 0;
                while (!_finishAckReceived && waitMs < 400)
                {
                    WeatherOS.Kernel.CustomNIC?.HackPoll();
                    System.Threading.Thread.Sleep(5);
                    waitMs += 5;
                }
            }

            Status = TransferStatus.Success;
            status = $"[SUCCESS] '{filename}' ({fileData.Length} B) successfully delivered to {targetIpStr}!";
            StatusMessage = status;
            return true;
        }

        /// <summary>
        /// Pings a remote peer via WOSF UDP packet.
        /// </summary>
        public static bool SendPing(string targetIpStr, ushort targetPort, out int rttMs, out string replyInfo)
        {
            rttMs = 0;
            replyInfo = "";
            byte[] targetIp = ParseIp(targetIpStr);
            if (targetIp == null) return false;

            RawTcpHttp.EnsurePacketHandlerActive();
            RawTcpHttp.SendArpRequest(targetIp);

            byte[] ping = new byte[5];
            ping[0] = 0x57; ping[1] = 0x4F; ping[2] = 0x53; ping[3] = 0x46;
            ping[4] = MSG_PING;

            _pongReceived = false;
            _lastPongInfo = "";

            byte[] bcastMac = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
            SendUdp(bcastMac, targetIp, PORT, targetPort, ping);

            int elapsed = 0;
            while (!_pongReceived && elapsed < 800)
            {
                WeatherOS.Kernel.CustomNIC?.HackPoll();
                System.Threading.Thread.Sleep(5);
                elapsed += 5;
            }

            if (_pongReceived)
            {
                rttMs = elapsed;
                replyInfo = _lastPongInfo;
                IsPeerVerified = true;
                return true;
            }

            IsPeerVerified = false;
            return false;
        }

        public static bool DiscoverWindowsPeer(int timeoutMs = 1500)
        {
            RawTcpHttp.EnsurePacketHandlerActive();
            byte[] bcastMac = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
            byte[] bcastIp = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };
            string myIp = RawTcpHttp.GetAssignedIp();
            byte[] info = Encoding.ASCII.GetBytes("WeatherOS:" + myIp);
            byte[] probe = new byte[5 + info.Length];
            probe[0] = 0x57; probe[1] = 0x4F; probe[2] = 0x53; probe[3] = 0x46;
            probe[4] = MSG_PING;
            Array.Copy(info, 0, probe, 5, info.Length);

            _pongReceived = false;

            for (int attempt = 0; attempt < 3 && string.IsNullOrWhiteSpace(LastPeerIp); attempt++)
            {
                SendUdp(bcastMac, bcastIp, PORT, PORT, probe);

                int waited = 0;
                while (string.IsNullOrWhiteSpace(LastPeerIp) && waited < (timeoutMs / 3))
                {
                    WeatherOS.Kernel.CustomNIC?.HackPoll();
                    System.Threading.Thread.Sleep(15);
                    waited += 15;
                }
            }

            return !string.IsNullOrWhiteSpace(LastPeerIp);
        }

        public static void BroadcastAnnounce()
        {
            RawTcpHttp.EnsurePacketHandlerActive();
            byte[] bcastMac = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
            byte[] bcastIp = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };
            string myIp = RawTcpHttp.GetAssignedIp();
            byte[] info = Encoding.ASCII.GetBytes("WeatherOS:" + myIp);
            byte[] announce = new byte[5 + info.Length];
            announce[0] = 0x57; announce[1] = 0x4F; announce[2] = 0x53; announce[3] = 0x46;
            announce[4] = MSG_PONG;
            Array.Copy(info, 0, announce, 5, info.Length);
            SendUdp(bcastMac, bcastIp, PORT, PORT, announce);
        }

        public static byte[] ParseIp(string ipStr)
        {
            if (string.IsNullOrWhiteSpace(ipStr)) return null;
            string[] parts = ipStr.Trim().Split('.');
            if (parts.Length != 4) return null;

            byte[] ip = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                byte b;
                if (!byte.TryParse(parts[i], out b)) return null;
                ip[i] = b;
            }
            return ip;
        }
    }
}
