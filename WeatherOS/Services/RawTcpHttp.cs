using System;
using Cosmos.HAL;
using Cosmos.System.Network.IPv4;

namespace WeatherOS.Services
{
    public static class RawTcpHttp
    {
        public static Cosmos.HAL.NetworkDevice GetActiveNic()
        {
            if (WeatherOS.Kernel.ActiveNetDevice != null) return WeatherOS.Kernel.ActiveNetDevice;
            if (WeatherOS.Kernel.CustomWifi != null && WeatherOS.Kernel.CustomWifi.IsConnected) return WeatherOS.Kernel.CustomWifi;
            if (WeatherOS.Kernel.CustomNIC != null && WeatherOS.Kernel.CustomNIC.Ready) return WeatherOS.Kernel.CustomNIC;
            if (Cosmos.HAL.NetworkDevice.Devices != null && Cosmos.HAL.NetworkDevice.Devices.Count > 0)
                return Cosmos.HAL.NetworkDevice.Devices[0];
            return null;
        }

        public static void PollNetwork()
        {
            WeatherOS.Kernel.CustomNIC?.HackPoll();
            WeatherOS.Kernel.CustomWifi?.PollRx();
        }

        private static byte[] _macSource = new byte[6];
        private static byte[] _macDest = new byte[6];
        private static byte[] _ipSource = new byte[4];
        private static byte[] _ipDest = new byte[4];
        private static ushort _sourcePort = 54321;
        public static ushort TargetPort = 80;
        private static uint _sequence = 0x4F9B2C11;
        private static uint _acknowledgment = 0;

        private static bool _synAckReceived = false;
        private static bool _finReceived = false;
        
        // Static buffer to avoid memory allocation inside IRQ! (256 KB to handle full 7-day payloads)
        private static byte[] _rxBuffer = new byte[262144]; // 256 KB
        private static int _rxLength = 0;
        private static int _jsonBraceCount = 0;
        private static bool _jsonStarted = false;

        private static Cosmos.HAL.DataReceivedHandler _oldHandler;

        private static uint _localRxBytes = 0;
        private static uint _localTxBytes = 0;

        public static uint TotalRxBytes
        {
            get { return Cosmos.HAL.Network.RTL8102E.HardwareRxBytes > _localRxBytes ? Cosmos.HAL.Network.RTL8102E.HardwareRxBytes : _localRxBytes; }
            set { _localRxBytes = value; }
        }

        public static uint TotalTxBytes
        {
            get { return Cosmos.HAL.Network.RTL8102E.HardwareTxBytes > _localTxBytes ? Cosmos.HAL.Network.RTL8102E.HardwareTxBytes : _localTxBytes; }
            set { _localTxBytes = value; }
        }

        private static uint _localTxPackets = 0;
        private static uint _localRxPackets = 0;

        public static uint TotalTxPackets
        {
            get { return Cosmos.HAL.Network.RTL8102E.HardwareTxPackets > _localTxPackets ? Cosmos.HAL.Network.RTL8102E.HardwareTxPackets : _localTxPackets; }
            set { _localTxPackets = value; }
        }

        public static uint TotalRxPackets
        {
            get { return Cosmos.HAL.Network.RTL8102E.HardwareRxPackets > _localRxPackets ? Cosmos.HAL.Network.RTL8102E.HardwareRxPackets : _localRxPackets; }
            set { _localRxPackets = value; }
        }
        private static ushort _ipId = 0x8A3C;
        public static bool _dhcpOfferReceived = false;
        public static bool _dhcpAckReceived = false;
        public static bool _arpReplyReceived = false;
        public static byte[] _pendingArpIp = null;
        public static byte[] _pendingArpMac = new byte[6];
        public static byte[] _gatewayIp = new byte[4];
        public static byte[] _dhcpServerIp = new byte[4];
        
        private static ushort _pingIdentifier = 0x1234;
        private static bool _pingReplyReceived = false;
        private static bool _ntpReplyReceived = false;
        private static uint _lastNtpEpochSeconds = 0;

        public static string GetAssignedIp()
        {
            if (_ipSource[0] == 0 && _ipSource[1] == 0) return "0.0.0.0";
            return $"{_ipSource[0]}.{_ipSource[1]}.{_ipSource[2]}.{_ipSource[3]}";
        }

        public static string GetGatewayIp()
        {
            if (_gatewayIp[0] == 0 && _gatewayIp[1] == 0) return "0.0.0.0";
            return $"{_gatewayIp[0]}.{_gatewayIp[1]}.{_gatewayIp[2]}.{_gatewayIp[3]}";
        }

        public static Cosmos.System.Network.IPv4.Address GetGatewayAddress()
        {
            if (_gatewayIp[0] == 0 && _gatewayIp[1] == 0) return new Cosmos.System.Network.IPv4.Address(192, 168, 1, 1);
            return new Cosmos.System.Network.IPv4.Address(_gatewayIp[0], _gatewayIp[1], _gatewayIp[2], _gatewayIp[3]);
        }

        public static string GetMaskedIp()
        {
            if (_ipSource[0] == 0 && _ipSource[1] == 0) return "Offline";
            return $"{_ipSource[0]}.{_ipSource[1]}.***.*** (Secured)";
        }

        public static byte[] GetSourceIpBytes()
        {
            byte[] copy = new byte[4];
            Array.Copy(_ipSource, 0, copy, 0, 4);
            return copy;
        }

        public static int CalculateIpChecksum(byte[] buffer, int offset, int length)
        {
            return CalculateChecksum(buffer, offset, length);
        }

        public static void EnsurePacketHandlerActive()
        {
            try
            {
                if (Cosmos.HAL.NetworkDevice.Devices != null && Cosmos.HAL.NetworkDevice.Devices.Count > 0)
                {
                    var nic = GetActiveNic();
                    if (nic.DataReceived != CustomDataReceived)
                    {
                        nic.DataReceived = CustomDataReceived;
                    }
                }
            }
            catch { }
        }

        public static void HardwareDelayMs(int ms)
        {
            if (ms <= 0) return;
            try
            {
                var port = new Cosmos.Core.IOPort(0x80);
                int count = ms * 1000;
                for (int i = 0; i < count; i++)
                {
                    port.Byte = 0;
                }
            }
            catch
            {
                for (int i = 0; i < ms * 10000; i++) { }
            }
        }

        public static void DoAutoConfig(Cosmos.HAL.NetworkDevice nic)
        {
            _macSource = nic.MACAddress.bytes;
            _ipSource = new byte[] { 0, 0, 0, 0 };
            _gatewayIp = new byte[] { 0, 0, 0, 0 };
            _dhcpServerIp = new byte[] { 0, 0, 0, 0 };
            _macDest = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
            
            // Set CustomDataReceived as the active packet handler
            EnsurePacketHandlerActive();

            bool isGui = WeatherOS.Graphics.TuiDesktop.IsGuiActive;

            // Check hardware PHY link status (cable plugged in)
            bool linkUp = true;
            if (nic is Cosmos.HAL.Network.RTL8102E rtlNic)
            {
                linkUp = rtlNic.IsPhyLinkUp();
            }

            if (!linkUp)
            {
                _ipSource = new byte[] { 0, 0, 0, 0 };
                _gatewayIp = new byte[] { 0, 0, 0, 0 };
                if (!isGui) System.Console.WriteLine("[net] raw_tcp: Ethernet cable disconnected (PHY link DOWN). Skipping network boot wait.");
                EnsurePacketHandlerActive();
                return;
            }

            // 1. DHCP Discover with local LAN retransmissions (up to ~1.2s so home routers & switches respond reliably)
            if (!isGui) System.Console.Write("[net] raw_tcp: DHCP Discover");
            _dhcpOfferReceived = false;
            _dhcpAckReceived = false;
            byte[] dhcp = BuildDhcpPacket(1, null, null); // Discover
            nic.QueueBytes(dhcp);

            for (int i = 0; i < 40 && !_dhcpOfferReceived; i++)
            {
                PollNetwork();
                HardwareDelayMs(25);
                if (i == 10 || i == 24)
                {
                    nic.QueueBytes(dhcp); // Retransmit Discover at 250ms and 600ms
                }
                if (!isGui && (i % 8 == 0)) System.Console.Write(".");
            }
            if (!isGui) System.Console.WriteLine();
            
            if (_dhcpOfferReceived)
            {
                // 1b. DHCP Request (up to ~600ms)
                byte[] dhcpReq = BuildDhcpPacket(3, _ipSource, _dhcpServerIp);
                nic.QueueBytes(dhcpReq);
                for (int i = 0; i < 24 && !_dhcpAckReceived; i++)
                {
                    PollNetwork();
                    HardwareDelayMs(25);
                    if (i == 8) nic.QueueBytes(dhcpReq);
                }

                if (!isGui)
                {
                    if (WeatherOS.Services.WeatherService.IsDebugModeActive)
                        System.Console.WriteLine($"[net] raw_tcp: DHCP OK! IP: {_ipSource[0]}.{_ipSource[1]}.{_ipSource[2]}.{_ipSource[3]} (Gateway: {_gatewayIp[0]}.{_gatewayIp[1]}.{_gatewayIp[2]}.{_gatewayIp[3]})");
                    else
                        System.Console.WriteLine($"[net] raw_tcp: DHCP OK! IP: {_ipSource[0]}.{_ipSource[1]}.***.*** [Secured]");
                }
            }
            else
            {
                // 2. DHCP silent: Instant fallback to static IP for direct PC-to-laptop cable link
                _ipSource = new byte[] { 192, 168, 1, 180 };
                _gatewayIp = new byte[] { 192, 168, 1, 1 };
                if (!isGui) System.Console.WriteLine("[net] raw_tcp: Direct cable link (Static IP: 192.168.1.180). Ready.");
            }

            // Sync with Cosmos NetworkStack if IP was resolved
            if (_ipSource[0] != 0)
            {
                try
                {
                    var myIp = new Cosmos.System.Network.IPv4.Address(_ipSource[0], _ipSource[1], _ipSource[2], _ipSource[3]);
                    var mySubnet = new Cosmos.System.Network.IPv4.Address(255, 255, 255, 0);
                    var myGw = new Cosmos.System.Network.IPv4.Address(_gatewayIp[0], _gatewayIp[1], _gatewayIp[2], _gatewayIp[3]);
                    Cosmos.System.Network.NetworkStack.ConfigIP(nic, new Cosmos.System.Network.Config.IPConfig(myIp, mySubnet, myGw));
                }
                catch { }
            }

            // Keep CustomDataReceived permanently active
            EnsurePacketHandlerActive();
        }

        private static byte[] BuildDhcpPacket(byte msgType, byte[] requestedIp, byte[] serverIp)
        {
            byte[] dhcp = new byte[342];
            for (int i = 0; i < 6; i++) dhcp[i] = 0xFF; // Broadcast
            Array.Copy(_macSource, 0, dhcp, 6, 6);
            dhcp[12] = 0x08; dhcp[13] = 0x00; // IPv4
            dhcp[14] = 0x45; dhcp[15] = 0x00;
            dhcp[16] = 0x01; dhcp[17] = 0x48; // Total Length (328)
            dhcp[18] = 0x00; dhcp[19] = 0x00; // ID
            dhcp[20] = 0x00; dhcp[21] = 0x00; // Flags/Frag
            dhcp[22] = 0x40; // TTL
            dhcp[23] = 17; // UDP
            dhcp[24] = 0x00; dhcp[25] = 0x00; // Checksum
            dhcp[30] = 0xFF; dhcp[31] = 0xFF; dhcp[32] = 0xFF; dhcp[33] = 0xFF; // Dest 255.255.255.255
            
            int ipSum = CalculateChecksum(dhcp, 14, 20);
            dhcp[24] = (byte)(ipSum >> 8); dhcp[25] = (byte)(ipSum & 0xFF);

            // UDP
            dhcp[34] = 0x00; dhcp[35] = 68; // Src Port
            dhcp[36] = 0x00; dhcp[37] = 67; // Dest Port
            dhcp[38] = 0x01; dhcp[39] = 0x34; // UDP Length (308)
            dhcp[40] = 0x00; dhcp[41] = 0x00;

            // DHCP Body
            dhcp[42] = 1; // BootRequest
            dhcp[43] = 1; // Ethernet
            dhcp[44] = 6; // MAC Len
            dhcp[45] = 0; // Hops
            dhcp[46] = 0x12; dhcp[47] = 0x34; dhcp[48] = 0x56; dhcp[49] = 0x78; // XID
            dhcp[52] = 0x80; dhcp[53] = 0x00; // Broadcast flag
            Array.Copy(_macSource, 0, dhcp, 70, 6); // Client MAC
            dhcp[278] = 0x63; dhcp[279] = 0x82; dhcp[280] = 0x53; dhcp[281] = 0x63; // Magic
            
            int opt = 282;
            dhcp[opt++] = 53; dhcp[opt++] = 1; dhcp[opt++] = msgType; // 1 = Discover, 3 = Request

            // Option 61: Client Identifier (Hardware Type 1 = Ethernet, followed by MAC)
            dhcp[opt++] = 61; dhcp[opt++] = 7;
            dhcp[opt++] = 1;
            for (int i = 0; i < 6; i++) dhcp[opt++] = _macSource[i];

            if (msgType == 3 && requestedIp != null)
            {
                dhcp[opt++] = 50; dhcp[opt++] = 4; // Requested IP
                dhcp[opt++] = requestedIp[0]; dhcp[opt++] = requestedIp[1];
                dhcp[opt++] = requestedIp[2]; dhcp[opt++] = requestedIp[3];
                if (serverIp != null && serverIp[0] != 0)
                {
                    dhcp[opt++] = 54; dhcp[opt++] = 4; // Server Identifier
                    dhcp[opt++] = serverIp[0]; dhcp[opt++] = serverIp[1];
                    dhcp[opt++] = serverIp[2]; dhcp[opt++] = serverIp[3];
                }
            }

            // Option 55: Parameter Request List (1=Subnet, 3=Router, 6=DNS, 15=Domain)
            dhcp[opt++] = 55; dhcp[opt++] = 4;
            dhcp[opt++] = 1; dhcp[opt++] = 3; dhcp[opt++] = 6; dhcp[opt++] = 15;

            // Option 12: Host Name ("WeatherOS")
            dhcp[opt++] = 12; dhcp[opt++] = 9;
            dhcp[opt++] = (byte)'W'; dhcp[opt++] = (byte)'e'; dhcp[opt++] = (byte)'a';
            dhcp[opt++] = (byte)'t'; dhcp[opt++] = (byte)'h'; dhcp[opt++] = (byte)'e';
            dhcp[opt++] = (byte)'r'; dhcp[opt++] = (byte)'O'; dhcp[opt++] = (byte)'S';

            dhcp[opt] = 255; // End
            return dhcp;
        }

        public static byte[] GetRouterMac()
        {
            return _macDest;
        }

        public static bool ResolveArp(byte[] targetIp, byte[] outMac, int timeoutMs = 500)
        {
            if (targetIp == null || targetIp.Length != 4 || outMac == null || outMac.Length < 6) return false;

            EnsurePacketHandlerActive();

            // 1. If target is router/gateway and we already learned its MAC
            if (_gatewayIp != null && targetIp[0] == _gatewayIp[0] && targetIp[1] == _gatewayIp[1] &&
                targetIp[2] == _gatewayIp[2] && targetIp[3] == _gatewayIp[3])
            {
                if (_macDest != null && (_macDest[0] != 0 || _macDest[1] != 0 || _macDest[2] != 0) &&
                    (_macDest[0] != 0xFF || _macDest[1] != 0xFF))
                {
                    Array.Copy(_macDest, 0, outMac, 0, 6);
                    return true;
                }
            }

            // 2. Perform active ARP Request (with retransmission if first broadcast frame was missed)
            SendArpRequest(targetIp);

            int elapsed = 0;
            while (!_arpReplyReceived && elapsed < timeoutMs)
            {
                PollNetwork();
                HardwareDelayMs(5);
                elapsed += 5;
                if (elapsed == 150 || elapsed == 350)
                {
                    SendArpRequest(targetIp);
                }
            }

            if (_arpReplyReceived)
            {
                Array.Copy(_pendingArpMac, 0, outMac, 0, 6);
                if (_gatewayIp != null && targetIp[0] == _gatewayIp[0] && targetIp[1] == _gatewayIp[1] &&
                    targetIp[2] == _gatewayIp[2] && targetIp[3] == _gatewayIp[3])
                {
                    Array.Copy(_pendingArpMac, 0, _macDest, 0, 6);
                }
                return true;
            }

            // 3. Fallback to router MAC if on same subnet
            if (_macDest != null && (_macDest[0] != 0 || _macDest[1] != 0 || _macDest[2] != 0) &&
                (_macDest[0] != 0xFF || _macDest[1] != 0xFF))
            {
                Array.Copy(_macDest, 0, outMac, 0, 6);
                return true;
            }

            return false;
        }

        public static void SendArpRequest(byte[] targetIp)
        {
            if (targetIp == null || targetIp.Length != 4) return;
            if (GetActiveNic() == null) return;
            var nic = GetActiveNic();
            _pendingArpIp = targetIp;
            _arpReplyReceived = false;
            byte[] arp = new byte[42];
            for (int i = 0; i < 6; i++) arp[i] = 0xFF; // Broadcast
            Array.Copy(_macSource, 0, arp, 6, 6);
            arp[12] = 0x08; arp[13] = 0x06; // ARP
            arp[14] = 0x00; arp[15] = 0x01; // Ethernet
            arp[16] = 0x08; arp[17] = 0x00; // IPv4
            arp[18] = 0x06; arp[19] = 0x04; // MAC/IP lens
            arp[20] = 0x00; arp[21] = 0x01; // Op: Request
            Array.Copy(_macSource, 0, arp, 22, 6);

            // Sender IP
            byte[] senderIp = new byte[4];
            if (_ipSource[0] != 0)
            {
                Array.Copy(_ipSource, 0, senderIp, 0, 4);
            }
            else
            {
                senderIp[0] = targetIp[0];
                senderIp[1] = targetIp[1];
                senderIp[2] = targetIp[2];
                senderIp[3] = 155;
            }
            Array.Copy(senderIp, 0, arp, 28, 4);

            for (int i = 0; i < 6; i++) arp[32 + i] = 0x00;
            Array.Copy(targetIp, 0, arp, 38, 4);
            _localTxBytes += (uint)arp.Length;
            nic.QueueBytes(arp);
        }

        private static void SendArpReply(byte[] reqPacket)
        {
            var nic = GetActiveNic();
            byte[] reply = new byte[42];
            Array.Copy(reqPacket, 22, reply, 0, 6); // Dest MAC = sender of request
            Array.Copy(_macSource, 0, reply, 6, 6);  // Src MAC = our MAC
            reply[12] = 0x08; reply[13] = 0x06;      // ARP
            reply[14] = 0x00; reply[15] = 0x01;      // Ethernet
            reply[16] = 0x08; reply[17] = 0x00;      // IPv4
            reply[18] = 0x06; reply[19] = 0x04;      // Lens
            reply[20] = 0x00; reply[21] = 0x02;      // Op: Reply
            Array.Copy(_macSource, 0, reply, 22, 6); // Sender MAC
            Array.Copy(_ipSource, 0, reply, 28, 4);  // Sender IP
            Array.Copy(reqPacket, 22, reply, 32, 6); // Target MAC
            Array.Copy(reqPacket, 28, reply, 38, 4); // Target IP
            _localTxBytes += (uint)reply.Length;
            nic.QueueBytes(reply);
        }

        public static bool Ping(Cosmos.System.Network.IPv4.Address targetAddress, out int rttMs)
        {
            rttMs = 0;
            if (_ipSource[0] == 0) return false;

            EnsurePacketHandlerActive();
            var nic = GetActiveNic();

            byte[] targetBytes = targetAddress.ToByteArray();

            // Determine destination MAC:
            // If target is on the same /24 subnet, resolve target IP directly via ARP.
            // If target is on a different subnet, resolve router gateway MAC.
            byte[] destMac = new byte[6];
            bool isSameSubnet = (targetBytes[0] == _ipSource[0] && targetBytes[1] == _ipSource[1] && targetBytes[2] == _ipSource[2]);

            if (isSameSubnet)
            {
                if (!ResolveArp(targetBytes, destMac, 500))
                {
                    return false;
                }
            }
            else
            {
                if (_gatewayIp[0] == 0) return false;
                if (_macDest[0] == 0xFF || (_macDest[0] == 0 && _macDest[1] == 0))
                {
                    if (!ResolveArp(_gatewayIp, _macDest, 500))
                    {
                        return false;
                    }
                }
                Array.Copy(_macDest, 0, destMac, 0, 6);
            }

            try
            {
                _pingReplyReceived = false;
                _pingIdentifier++;
                
                byte[] ping = new byte[74];
                Array.Copy(destMac, 0, ping, 0, 6);
                Array.Copy(_macSource, 0, ping, 6, 6);
                ping[12] = 0x08; ping[13] = 0x00; // IPv4
                
                ping[14] = 0x45; ping[15] = 0x00;
                ping[16] = 0x00; ping[17] = 60; // Total Length (60)
                ping[18] = (byte)(_ipId >> 8); ping[19] = (byte)(_ipId & 0xFF); _ipId++;
                ping[20] = 0x40; ping[21] = 0x00;
                ping[22] = 64;   // TTL
                ping[23] = 0x01; // ICMP
                ping[24] = 0x00; ping[25] = 0x00; // Checksum
                Array.Copy(_ipSource, 0, ping, 26, 4);
                Array.Copy(targetBytes, 0, ping, 30, 4);
                
                int ipSum = CalculateChecksum(ping, 14, 20);
                ping[24] = (byte)(ipSum >> 8); ping[25] = (byte)(ipSum & 0xFF);
                
                // ICMP
                ping[34] = 0x08; // Echo Request
                ping[35] = 0x00; // Code 0
                ping[36] = 0x00; ping[37] = 0x00; // Checksum
                ping[38] = (byte)(_pingIdentifier >> 8); ping[39] = (byte)(_pingIdentifier & 0xFF);
                ping[40] = 0x00; ping[41] = 0x01; // Seq 1
                
                string pingData = "WeatherOS Bare-Metal Ping Engine";
                for (int i = 0; i < 32 && i < pingData.Length; i++)
                {
                    ping[42 + i] = (byte)pingData[i];
                }
                
                int icmpSum = CalculateChecksum(ping, 34, 40);
                ping[36] = (byte)(icmpSum >> 8); ping[37] = (byte)(icmpSum & 0xFF);
                
                _localTxBytes += (uint)ping.Length;
                nic.QueueBytes(ping);
                
                int elapsedMs = 0;
                while (!_pingReplyReceived && elapsedMs < 1200)
                {
                    PollNetwork();
                    HardwareDelayMs(2);
                    elapsedMs += 2;
                }
                
                if (_pingReplyReceived)
                {
                    rttMs = elapsedMs;
                    if (rttMs < 1) rttMs = 1;
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public static bool QueryNtp(byte[] ntpServerIp, out uint unixEpochSeconds)
        {
            unixEpochSeconds = 0;
            if (GetActiveNic() == null) return false;
            var nic = GetActiveNic();
            if (nic == null || _ipSource[0] == 0) return false;

            byte[] targetIp = ntpServerIp;
            if (targetIp == null)
            {
                // Primary NTP: 162.159.200.123 (Cloudflare / pool.ntp.org)
                targetIp = new byte[] { 162, 159, 200, 123 };
            }

            // Ensure gateway MAC resolved
            if (_macDest == null || (_macDest[0] == 0xFF && _macDest[1] == 0xFF) || (_macDest[0] == 0 && _macDest[1] == 0))
            {
                if (_gatewayIp[0] != 0) ResolveArp(_gatewayIp, _macDest, 400);
            }

            byte[] ntpFrame = new byte[90];
            // Ethernet
            Array.Copy(_macDest, 0, ntpFrame, 0, 6);
            Array.Copy(_macSource, 0, ntpFrame, 6, 6);
            ntpFrame[12] = 0x08; ntpFrame[13] = 0x00; // IPv4

            // IPv4
            ntpFrame[14] = 0x45; ntpFrame[15] = 0x00;
            ntpFrame[16] = 0x00; ntpFrame[17] = 0x4C; // Total length 76
            ntpFrame[18] = (byte)(_ipId >> 8); ntpFrame[19] = (byte)(_ipId & 0xFF); _ipId++;
            ntpFrame[20] = 0x00; ntpFrame[21] = 0x00;
            ntpFrame[22] = 0x40; // TTL 64
            ntpFrame[23] = 17;   // UDP
            Array.Copy(_ipSource, 0, ntpFrame, 26, 4);
            Array.Copy(targetIp, 0, ntpFrame, 30, 4);
            int ipSum = CalculateChecksum(ntpFrame, 14, 20);
            ntpFrame[24] = (byte)(ipSum >> 8); ntpFrame[25] = (byte)(ipSum & 0xFF);

            // UDP
            ntpFrame[34] = 0x30; ntpFrame[35] = 0x0C; // Src port 12300
            ntpFrame[36] = 0x00; ntpFrame[37] = 123;  // Dest port 123
            ntpFrame[38] = 0x00; ntpFrame[39] = 0x38; // UDP length 56
            ntpFrame[40] = 0x00; ntpFrame[41] = 0x00; // Checksum 0

            // NTP v3 Client (0x1B = LI 0, VN 3, Mode 3)
            ntpFrame[42] = 0x1B;

            _ntpReplyReceived = false;
            EnsurePacketHandlerActive();

            try
            {
                nic.QueueBytes(ntpFrame);
                for (int i = 0; i < 60; i++)
                {
                    PollNetwork();
                    if (_ntpReplyReceived)
                    {
                        unixEpochSeconds = _lastNtpEpochSeconds;
                        return true;
                    }
                    HardwareDelayMs(10);
                }
            }
            catch { }

            return false;
        }

        public static string FetchGet(Address destIp, Address gatewayIp, string requestString)
        {
            if (GetActiveNic() == null) return null;
            var nic = GetActiveNic();

            // If IP not configured yet, attempt auto-config
            if (_ipSource[0] == 0 && _ipSource[1] == 0)
            {
                DoAutoConfig(nic);
                if (_ipSource[0] == 0 && _ipSource[1] == 0) return null;
            }

            if (_gatewayIp[0] == 0 && gatewayIp != null)
            {
                byte[] gwBytes = gatewayIp.ToByteArray();
                if (gwBytes[0] != 0)
                {
                    Array.Copy(gwBytes, 0, _gatewayIp, 0, 4);
                }
            }

            _ipDest = destIp.ToByteArray();
            TargetPort = 80;

            // Ensure router MAC address is resolved via ARP before starting TCP
            if (_gatewayIp[0] != 0)
            {
                if (_macDest == null || (_macDest[0] == 0xFF && _macDest[1] == 0xFF) || (_macDest[0] == 0 && _macDest[1] == 0))
                {
                    ResolveArp(_gatewayIp, _macDest, 600);
                }
            }

            // Flush any residual incoming packets from previous session
            for (int drain = 0; drain < 20; drain++)
            {
                PollNetwork();
                HardwareDelayMs(1);
            }

            _synAckReceived = false;
            _finReceived = false;
            _rxLength = 0;
            _jsonBraceCount = 0;
            _jsonStarted = false;
            Array.Clear(_rxBuffer, 0, _rxBuffer.Length);
            _sequence = 0x4F9B2C11 + (uint)(_sourcePort * 101);
            _acknowledgment = 0;

            EnsurePacketHandlerActive();

            try
            {
                // Send SYN with robust retransmission (up to 3 attempts, 600ms each)
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    SendTcpPacket(nic, 0x02, new byte[0]); // SYN
                    
                    int synTimeout = 0;
                    while (!_synAckReceived && synTimeout < 60) // 60 * 10ms = 600ms
                    {
                        PollNetwork();
                        HardwareDelayMs(10);
                        synTimeout++;
                    }
                    if (_synAckReceived) break;
                }

                if (!_synAckReceived)
                {
                    return null;
                }

                // Complete 3-way TCP handshake with pure ACK before sending HTTP request
                SendTcpPacket(nic, 0x10, new byte[0]);
                HardwareDelayMs(2);

                // Send ACK + HTTP Request Payload
                byte[] payload = new byte[requestString.Length];
                for (int p = 0; p < requestString.Length; p++)
                {
                    payload[p] = (byte)requestString[p];
                }
                SendTcpPacket(nic, 0x18, payload); // ACK + PSH
                _sequence += (uint)payload.Length;

                // Wait for complete response data (with real-time pacing, up to 4000ms max)
                int dataMs = 0;
                int lastRxLen = _rxLength;
                int quietMs = 0;
                while (!_finReceived && dataMs < 4000)
                {
                    PollNetwork();
                    HardwareDelayMs(10);
                    dataMs += 10;

                    if (_rxLength > lastRxLen)
                    {
                        lastRxLen = _rxLength;
                        quietMs = 0;
                    }
                    else if (_rxLength > 0)
                    {
                        quietMs += 10;
                        // Wait until full JSON is received or 300ms of silence
                        if (_rxLength > 200 && quietMs >= 300) break;
                    }
                }

                // Decode UTF-8 to ASCII
                byte[] validData = new byte[_rxLength];
                Array.Copy(_rxBuffer, 0, validData, 0, _rxLength);
                string responseStr = DecodeUtf8ToAscii(validData, _rxLength);

                // Passive Network Time Calibration: Parse HTTP Date header if present
                try
                {
                    int dateIdx = CustomIndexOf(responseStr, "\r\nDate: ");
                    if (dateIdx == -1) dateIdx = CustomIndexOf(responseStr, "\ndate: ");
                    if (dateIdx != -1)
                    {
                        int lineEnd = CustomIndexOf(responseStr, "\r\n", dateIdx + 2);
                        if (lineEnd != -1)
                        {
                            int dColon = CustomIndexOf(responseStr, ":", dateIdx);
                            string dateLine = (dColon != -1 && dColon < lineEnd) ? responseStr.Substring(dColon + 1, lineEnd - (dColon + 1)).Trim() : responseStr.Substring(dateIdx + 2, lineEnd - (dateIdx + 2)).Trim();
                            TimeService.SyncFromHttpDate(dateLine);
                        }
                    }
                }
                catch { }

                // Extract HTTP body
                int bodyIdx = CustomIndexOf(responseStr, "\r\n\r\n");
                string bodyStr = (bodyIdx != -1) ? responseStr.Substring(bodyIdx + 4) : responseStr;

                // De-chunk HTTP/1.1 Transfer-Encoding: chunked if present so JSON arrays aren't corrupted by hex chunk sizes
                if (CustomIndexOf(responseStr, "Transfer-Encoding: chunked") != -1 ||
                    CustomIndexOf(responseStr, "transfer-encoding: chunked") != -1)
                {
                    bodyStr = DecodeChunkedBody(bodyStr);
                }
                else
                {
                    // Strip any leading hex chunk size before the root JSON object '{'
                    int firstBrace = CustomIndexOf(bodyStr, "{");
                    if (firstBrace > 0 && firstBrace < 16)
                    {
                        bodyStr = bodyStr.Substring(firstBrace);
                    }
                }

                return bodyStr;
            }
            catch
            {
                return null;
            }
            finally
            {
                // Send TCP RST to cleanly close socket and free router/server state
                SendTcpPacket(nic, 0x04, new byte[0]);
                _sourcePort++;
                if (_sourcePort > 65000) _sourcePort = 50000;
            }
        }

        private static string DecodeChunkedBody(string body)
        {
            if (string.IsNullOrEmpty(body)) return "";
            int firstBrace = CustomIndexOf(body, "{");
            if (firstBrace == 0) return body; // Not chunked

            char[] outChars = new char[body.Length];
            int outLen = 0;
            int pos = 0;
            int len = body.Length;

            while (pos < len)
            {
                while (pos < len && (body[pos] == '\r' || body[pos] == '\n' || body[pos] == ' ')) pos++;
                if (pos >= len) break;

                int lineEnd = CustomIndexOf(body, "\r\n", pos);
                if (lineEnd == -1)
                {
                    while (pos < len) outChars[outLen++] = body[pos++];
                    break;
                }

                int chunkSize = 0;
                bool validHex = false;
                for (int i = pos; i < lineEnd; i++)
                {
                    char c = body[i];
                    if (c == ';') break;
                    int val = -1;
                    if (c >= '0' && c <= '9') val = c - '0';
                    else if (c >= 'a' && c <= 'f') val = 10 + (c - 'a');
                    else if (c >= 'A' && c <= 'F') val = 10 + (c - 'A');
                    else { validHex = false; break; }
                    chunkSize = (chunkSize << 4) | val;
                    validHex = true;
                }

                if (!validHex)
                {
                    while (pos < len) outChars[outLen++] = body[pos++];
                    break;
                }

                pos = lineEnd + 2;
                if (chunkSize == 0) break;

                int count = Math.Min(chunkSize, len - pos);
                for (int i = 0; i < count; i++)
                {
                    outChars[outLen++] = body[pos++];
                }
                if (pos + 2 <= len && body[pos] == '\r' && body[pos + 1] == '\n') pos += 2;
            }

            return outLen > 0 ? new string(outChars, 0, outLen) : body;
        }

        private static string DecodeUtf8ToAscii(byte[] data, int length)
        {
            if (data == null || length <= 0) return "";
            char[] chars = new char[length];
            int outLen = 0;
            for (int i = 0; i < length; i++)
            {
                byte b = data[i];
                if (b < 128)
                {
                    chars[outLen++] = (char)b;
                }
                else if (b == 0xC3 && i + 1 < length)
                {
                    byte b2 = data[i + 1];
                    i++;
                    switch (b2)
                    {
                        case 0xA1: case 0xA0: case 0xA3: case 0xA2: chars[outLen++] = 'a'; break;
                        case 0x81: case 0x80: case 0x83: case 0x82: chars[outLen++] = 'A'; break;
                        case 0xA9: case 0xAA: chars[outLen++] = 'e'; break;
                        case 0x89: case 0x8A: chars[outLen++] = 'E'; break;
                        case 0xAD: chars[outLen++] = 'i'; break;
                        case 0x8D: chars[outLen++] = 'I'; break;
                        case 0xB3: case 0xB4: case 0xB5: chars[outLen++] = 'o'; break;
                        case 0x93: case 0x94: case 0x95: chars[outLen++] = 'O'; break;
                        case 0xBA: chars[outLen++] = 'u'; break;
                        case 0x9A: chars[outLen++] = 'U'; break;
                        case 0xA7: chars[outLen++] = 'c'; break;
                        case 0x87: chars[outLen++] = 'C'; break;
                        default: chars[outLen++] = '?'; break;
                    }
                }
                else
                {
                    // Ignora outros bytes multibyte nao mapeados
                }
            }
            return new string(chars, 0, outLen);
        }

        public static int CustomIndexOf(string source, string search, int startIndex = 0)
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

        public static bool StartTcpSession(Address destIp, ushort destPort, int timeoutMs = 2500)
        {
            if (GetActiveNic() == null) return false;
            var nic = GetActiveNic();

            if (_ipSource[0] == 0 && _ipSource[1] == 0)
            {
                DoAutoConfig(nic);
                if (_ipSource[0] == 0 && _ipSource[1] == 0) return false;
            }

            _ipDest = destIp.ToByteArray();
            TargetPort = destPort;

            if (_gatewayIp[0] != 0)
            {
                if (_macDest == null || (_macDest[0] == 0xFF && _macDest[1] == 0xFF) || (_macDest[0] == 0 && _macDest[1] == 0))
                {
                    ResolveArp(_gatewayIp, _macDest, 600);
                }
            }

            // Drain any lingering packets
            for (int drain = 0; drain < 25; drain++)
            {
                PollNetwork();
                HardwareDelayMs(1);
            }

            _synAckReceived = false;
            _finReceived = false;
            _rxLength = 0;
            _sequence = 0x4F9B2C11 + (uint)(_sourcePort * 101);
            _acknowledgment = 0;

            _oldHandler = nic.DataReceived;
            nic.DataReceived = CustomDataReceived;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                SendTcpPacket(nic, 0x02, new byte[0]); // SYN
                int synTimeout = 0;
                while (!_synAckReceived && synTimeout < 50)
                {
                    PollNetwork();
                    HardwareDelayMs(10);
                    synTimeout++;
                }
                if (_synAckReceived)
                {
                    SendTcpPacket(nic, 0x10, new byte[0]); // Complete 3-way handshake with ACK!
                    break;
                }
            }

            return _synAckReceived;
        }

        public static bool SendTcpData(byte[] data)
        {
            if (!_synAckReceived || data == null || data.Length == 0) return false;
            if (GetActiveNic() == null) return false;
            var nic = GetActiveNic();

            SendTcpPacket(nic, 0x18, data); // ACK + PSH
            _sequence += (uint)data.Length;
            return true;
        }

        public static int ReadTcpData(byte[] buffer, int offset, int maxLen, int timeoutMs = 2500)
        {
            if (GetActiveNic() == null) return 0;

            int elapsed = 0;
            while (_rxLength == 0 && elapsed < timeoutMs)
            {
                PollNetwork();
                HardwareDelayMs(10);
                elapsed += 10;
            }

            if (_rxLength == 0) return 0;

            int toCopy = Math.Min(_rxLength, maxLen);
            Array.Copy(_rxBuffer, 0, buffer, offset, toCopy);

            if (_rxLength > toCopy)
            {
                Array.Copy(_rxBuffer, toCopy, _rxBuffer, 0, _rxLength - toCopy);
                _rxLength -= toCopy;
            }
            else
            {
                _rxLength = 0;
            }

            return toCopy;
        }

        public static void EndTcpSession()
        {
            if (GetActiveNic() == null) return;
            var nic = GetActiveNic();
            try
            {
                if (_synAckReceived)
                {
                    SendTcpPacket(nic, 0x11, new byte[0]); // FIN + ACK
                }
            }
            catch { }
        }

        private static void SendTcpPacket(Cosmos.HAL.NetworkDevice nic, byte tcpFlags, byte[] payload)
        {
            bool isSyn = (tcpFlags & 0x02) != 0;
            int tcpOptionsLen = isSyn ? 4 : 0;
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
            frame[18] = (byte)(_ipId >> 8); frame[19] = (byte)(_ipId & 0xFF); _ipId++;
            frame[20] = 0x40; frame[21] = 0x00;
            frame[22] = 0x80; frame[23] = 0x06;
            Array.Copy(_ipSource, 0, frame, 26, 4);
            Array.Copy(_ipDest, 0, frame, 30, 4);

            int ipChecksum = CalculateChecksum(frame, 14, 20);
            frame[24] = (byte)(ipChecksum >> 8); frame[25] = (byte)(ipChecksum & 0xFF);

            // TCP
            int tcpOffset = 34;
            frame[tcpOffset] = (byte)(_sourcePort >> 8); frame[tcpOffset + 1] = (byte)(_sourcePort & 0xFF);
            frame[tcpOffset + 2] = (byte)(TargetPort >> 8); frame[tcpOffset + 3] = (byte)(TargetPort & 0xFF);
            
            frame[tcpOffset + 4] = (byte)(_sequence >> 24); frame[tcpOffset + 5] = (byte)(_sequence >> 16);
            frame[tcpOffset + 6] = (byte)(_sequence >> 8); frame[tcpOffset + 7] = (byte)(_sequence & 0xFF);
            
            uint ackToSend = isSyn ? 0 : _acknowledgment;
            frame[tcpOffset + 8] = (byte)(ackToSend >> 24); frame[tcpOffset + 9] = (byte)(ackToSend >> 16);
            frame[tcpOffset + 10] = (byte)(ackToSend >> 8); frame[tcpOffset + 11] = (byte)(ackToSend & 0xFF);

            int dataOffset = 5 + (tcpOptionsLen / 4);
            frame[tcpOffset + 12] = (byte)(dataOffset << 4);
            frame[tcpOffset + 13] = tcpFlags;
            frame[tcpOffset + 14] = 0x20; frame[tcpOffset + 15] = 0x00; // Window Size 8192

            int currentOffset = tcpOffset + 20;

            if (isSyn)
            {
                // MSS = 1360 (0x0550) - safe for PPPoE and tunnel MTUs
                frame[currentOffset++] = 0x02;
                frame[currentOffset++] = 0x04;
                frame[currentOffset++] = 0x05;
                frame[currentOffset++] = 0x50;
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
            TotalTxPackets++;
            if (!nic.QueueBytes(frame))
            {
                if (!WeatherOS.Graphics.TuiDesktop.IsGuiActive) System.Console.WriteLine("\n[net] Tx Ring Full");
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

        private static void SendIcmpEchoReply(byte[] reqPacket, int icmpOff)
        {
            if (GetActiveNic() == null) return;
            var nic = GetActiveNic();

            byte[] reply = new byte[reqPacket.Length];
            Array.Copy(reqPacket, 0, reply, 0, reqPacket.Length);

            // Swap MACs
            Array.Copy(reqPacket, 6, reply, 0, 6); // Dest MAC = sender
            Array.Copy(_macSource, 0, reply, 6, 6); // Src MAC = us

            // Swap IPs
            Array.Copy(reqPacket, 26, reply, 30, 4); // Dest IP = sender
            Array.Copy(_ipSource, 0, reply, 26, 4);  // Src IP = us

            // IP checksum
            reply[24] = 0; reply[25] = 0;
            int ipHdrLen = (reply[14] & 0x0F) * 4;
            int ipSum = CalculateChecksum(reply, 14, ipHdrLen);
            reply[24] = (byte)(ipSum >> 8); reply[25] = (byte)(ipSum & 0xFF);

            // ICMP Type = 0 (Echo Reply)
            reply[icmpOff] = 0x00;
            reply[icmpOff + 1] = 0x00;
            reply[icmpOff + 2] = 0x00; reply[icmpOff + 3] = 0x00; // Reset checksum

            int icmpLen = reqPacket.Length - icmpOff;
            int icmpSum = CalculateChecksum(reply, icmpOff, icmpLen);
            reply[icmpOff + 2] = (byte)(icmpSum >> 8); reply[icmpOff + 3] = (byte)(icmpSum & 0xFF);

            _localTxBytes += (uint)reply.Length;
            nic.QueueBytes(reply);
        }

        private static void CustomDataReceived(byte[] packet)
        {
            if (_oldHandler == CustomDataReceived) _oldHandler = null; // Previne recursividade
            if (packet != null) { TotalRxBytes += (uint)packet.Length; TotalRxPackets++; }
            if (packet == null || packet.Length < 42) return;

            // 1. Is ARP?
            if (packet[12] == 0x08 && packet[13] == 0x06)
            {
                ushort op = (ushort)((packet[20] << 8) | packet[21]);
                if (op == 0x0002) // ARP Reply
                {
                    bool isPendingMatch = (_pendingArpIp != null &&
                        packet[28] == _pendingArpIp[0] && packet[29] == _pendingArpIp[1] &&
                        packet[30] == _pendingArpIp[2] && packet[31] == _pendingArpIp[3]);

                    bool isGatewayMatch = (_gatewayIp != null && _gatewayIp[0] != 0 &&
                        packet[28] == _gatewayIp[0] && packet[29] == _gatewayIp[1] &&
                        packet[30] == _gatewayIp[2] && packet[31] == _gatewayIp[3]);

                    if (isPendingMatch || isGatewayMatch)
                    {
                        if (isPendingMatch)
                        {
                            Array.Copy(packet, 22, _pendingArpMac, 0, 6);
                        }
                        if (isGatewayMatch || (_gatewayIp[0] == 0 && isPendingMatch))
                        {
                            Array.Copy(packet, 22, _macDest, 0, 6);
                        }
                        _arpReplyReceived = true;
                    }
                }
                else if (op == 0x0001) // ARP Request: Who has _ipSource?
                {
                    if (_ipSource[0] != 0 && packet[38] == _ipSource[0] && packet[39] == _ipSource[1] &&
                        packet[40] == _ipSource[2] && packet[41] == _ipSource[3])
                    {
                        SendArpReply(packet);
                    }
                }
                return;
            }

            if (packet[12] != 0x08 || packet[13] != 0x00) return; // Not IPv4

            // 2. Is ICMP?
            if (packet[23] == 0x01)
            {
                int ipHdrLen = (packet[14] & 0x0F) * 4;
                int icmpOff = 14 + ipHdrLen;
                if (packet.Length >= icmpOff + 8)
                {
                    byte icmpType = packet[icmpOff];
                    byte icmpCode = packet[icmpOff + 1];
                    if (icmpType == 0 && icmpCode == 0) // Echo Reply
                    {
                        ushort id = (ushort)((packet[icmpOff + 4] << 8) | packet[icmpOff + 5]);
                        if (id == _pingIdentifier)
                        {
                            _pingReplyReceived = true;
                        }
                    }
                    else if (icmpType == 8 && icmpCode == 0) // Echo Request (incoming ping from network)
                    {
                        if (_ipSource[0] != 0 && packet[30] == _ipSource[0] && packet[31] == _ipSource[1] &&
                            packet[32] == _ipSource[2] && packet[33] == _ipSource[3])
                        {
                            SendIcmpEchoReply(packet, icmpOff);
                        }
                    }
                }
                return;
            }

            // 3. Is UDP?
            if (packet[23] == 17)
            {
                int ipHdrLen = (packet[14] & 0x0F) * 4;
                int udpOff = 14 + ipHdrLen;
                if (packet.Length < udpOff + 8) return;

                int udpDestPort = (packet[udpOff + 2] << 8) | packet[udpOff + 3];
                int udpSrcPort = (packet[udpOff] << 8) | packet[udpOff + 1];
                int udpLen = (packet[udpOff + 4] << 8) | packet[udpOff + 5];

                if (udpDestPort == FileTransferService.PORT)
                {
                    FileTransferService.ProcessIncomingPacket(packet, udpOff);
                    return;
                }

                if (udpSrcPort == 123 || udpDestPort == 123 || udpDestPort == 12300)
                {
                    int ntpOff = udpOff + 8;
                    if (packet.Length >= ntpOff + 48)
                    {
                        uint b0 = packet[ntpOff + 40];
                        uint b1 = packet[ntpOff + 41];
                        uint b2 = packet[ntpOff + 42];
                        uint b3 = packet[ntpOff + 43];
                        uint ntpSec = (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
                        if (ntpSec > 2208988800u)
                        {
                            _lastNtpEpochSeconds = ntpSec - 2208988800u;
                            _ntpReplyReceived = true;
                        }
                    }
                    return;
                }

                if (packet[udpOff + 2] == 0x00 && packet[udpOff + 3] == 0x44)
                {
                    int dhcpOff = udpOff + 8;
                    int dhcpEnd = udpOff + udpLen;
                    if (dhcpEnd > packet.Length) dhcpEnd = packet.Length;

                    if (dhcpEnd >= dhcpOff + 240 && packet[dhcpOff + 236] == 0x63 && packet[dhcpOff + 237] == 0x82)
                    {
                        byte dhcpMsgType = 0;
                        int optIdx = dhcpOff + 240;
                        while (optIdx + 1 < dhcpEnd && packet[optIdx] != 255)
                        {
                            byte opt = packet[optIdx];
                            if (opt == 0) { optIdx++; continue; }
                            byte len = packet[optIdx + 1];
                            if (len == 0 || optIdx + 2 + len > dhcpEnd) break;
                            if (opt == 53 && len >= 1) dhcpMsgType = packet[optIdx + 2];
                            if (opt == 3 && len >= 4) Array.Copy(packet, optIdx + 2, _gatewayIp, 0, 4);
                            if (opt == 54 && len >= 4) Array.Copy(packet, optIdx + 2, _dhcpServerIp, 0, 4);
                            optIdx += 2 + len;
                        }

                        if (dhcpMsgType == 2) // DHCP Offer
                        {
                            Array.Copy(packet, dhcpOff + 16, _ipSource, 0, 4);
                            Array.Copy(packet, 6, _macDest, 0, 6); // Grab Router MAC from Ethernet frame!
                            if (_gatewayIp[0] == 0) { Array.Copy(_ipSource, 0, _gatewayIp, 0, 4); _gatewayIp[3] = 1; }
                            _dhcpOfferReceived = true;
                        }
                        else if (dhcpMsgType == 5) // DHCP ACK
                        {
                            Array.Copy(packet, dhcpOff + 16, _ipSource, 0, 4);
                            Array.Copy(packet, 6, _macDest, 0, 6); // Grab Router MAC from Ethernet frame!
                            if (_gatewayIp[0] == 0) { Array.Copy(_ipSource, 0, _gatewayIp, 0, 4); _gatewayIp[3] = 1; }
                            _dhcpAckReceived = true;
                        }
                    }
                }
                return;
            }

            // 4. Is TCP?
            if (packet[23] != 0x06) return; // Drop any non-TCP
            if (packet.Length < 54) return;
            if (packet[30] != _ipSource[0] || packet[31] != _ipSource[1] || packet[32] != _ipSource[2] || packet[33] != _ipSource[3]) return;

            int ipHeaderLen = (packet[14] & 0x0F) * 4;
            int ipTotalLen = (packet[16] << 8) | packet[17];
            int tcpOffset = 14 + ipHeaderLen;

            int destPort = (packet[tcpOffset + 2] << 8) | packet[tcpOffset + 3];
            if (destPort != _sourcePort) return; // Drop! NEVER forward TCP to Cosmos!

            byte flags = packet[tcpOffset + 13];
            uint remoteSeq = (uint)((packet[tcpOffset + 4] << 24) | (packet[tcpOffset + 5] << 16) | (packet[tcpOffset + 6] << 8) | packet[tcpOffset + 7]);
            
            int tcpHeaderLen = (packet[tcpOffset + 12] >> 4) * 4;
            int payloadOffset = tcpOffset + tcpHeaderLen;
            int payloadLen = ipTotalLen - ipHeaderLen - tcpHeaderLen;

            // RST (0x04)
            if ((flags & 0x04) == 0x04) return;

            // SYN-ACK (0x12)
            if ((flags & 0x12) == 0x12 && !_synAckReceived)
            {
                _acknowledgment = remoteSeq + 1;
                _sequence += 1;
                _synAckReceived = true;
            }
            // ACK or PSH-ACK (Data received)
            else if ((flags & 0x10) == 0x10 && _synAckReceived)
            {
                if (payloadLen > 0 && payloadOffset >= 0 && payloadOffset < packet.Length)
                {
                    if (payloadOffset + payloadLen > packet.Length)
                    {
                        payloadLen = packet.Length - payloadOffset;
                    }

                    if (_acknowledgment == 0 || remoteSeq >= _acknowledgment)
                    {
                        _acknowledgment = remoteSeq + (uint)payloadLen;
                        
                        if (_rxLength + payloadLen < _rxBuffer.Length)
                        {
                            Array.Copy(packet, payloadOffset, _rxBuffer, _rxLength, payloadLen);
                            _rxLength += payloadLen;
                        }

                        // Fast incremental detection of balanced root JSON braces
                        for (int bi = 0; bi < payloadLen; bi++)
                        {
                            byte ch = packet[payloadOffset + bi];
                            if (ch == '{') { _jsonBraceCount++; _jsonStarted = true; }
                            else if (ch == '}') { _jsonBraceCount--; }
                        }
                        if (_jsonStarted && _jsonBraceCount == 0 && _rxLength > 200)
                        {
                            _finReceived = true;
                        }
                    }

                    // Send ACK
                    var nic = GetActiveNic();
                    SendTcpPacket(nic, 0x10, new byte[0]);
                }
            }
            
            // FIN
            if ((flags & 0x01) == 0x01 || (flags & 0x11) == 0x11)
            {
                _acknowledgment = remoteSeq + 1;
                var nic = GetActiveNic();
                SendTcpPacket(nic, 0x10, new byte[0]); // ACK FIN
                _finReceived = true;
            }
        }
    }
}
