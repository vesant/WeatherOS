using System;
using System.Text;

namespace Cosmos.HAL.Network
{
    public enum Wpa2HandshakeState
    {
        Idle,
        ConnectingAuthAssoc,
        WaitingMsg1,
        Msg2Sent,
        WaitingMsg3,
        Completed,
        Failed
    }

    /// <summary>
    /// Pure C# RFC 3174 SHA-1 implementation without any System.Security.Cryptography dependencies.
    /// Perfectly compatible with Cosmos OS / IL2CPU bare-metal runtime.
    /// </summary>
    public class PureSha1
    {
        private uint[] _h = new uint[5];
        private byte[] _buffer = new byte[64];
        private int _bufferLength;
        private ulong _totalBytes;
        private uint[] _w = new uint[80];

        public PureSha1()
        {
            Reset();
        }

        public void Reset()
        {
            _h[0] = 0x67452301u;
            _h[1] = 0xEFCDAB89u;
            _h[2] = 0x98BADCFEu;
            _h[3] = 0x10325476u;
            _h[4] = 0xC3D2E1F0u;
            _bufferLength = 0;
            _totalBytes = 0;
        }

        public void Update(byte[] data, int offset, int count)
        {
            if (data == null || count <= 0) return;
            _totalBytes += (ulong)count;

            int i = 0;
            if (_bufferLength > 0)
            {
                int toCopy = Math.Min(64 - _bufferLength, count);
                Array.Copy(data, offset, _buffer, _bufferLength, toCopy);
                _bufferLength += toCopy;
                i += toCopy;

                if (_bufferLength == 64)
                {
                    ProcessBlock(_buffer, 0);
                    _bufferLength = 0;
                }
            }

            while (i + 64 <= count)
            {
                ProcessBlock(data, offset + i);
                i += 64;
            }

            if (i < count)
            {
                int remaining = count - i;
                Array.Copy(data, offset + i, _buffer, 0, remaining);
                _bufferLength = remaining;
            }
        }

        public void Final(byte[] output, int outOffset)
        {
            ulong totalBits = _totalBytes * 8;

            _buffer[_bufferLength++] = 0x80;

            if (_bufferLength > 56)
            {
                while (_bufferLength < 64) _buffer[_bufferLength++] = 0;
                ProcessBlock(_buffer, 0);
                _bufferLength = 0;
            }

            while (_bufferLength < 56) _buffer[_bufferLength++] = 0;

            _buffer[56] = (byte)(totalBits >> 56);
            _buffer[57] = (byte)(totalBits >> 48);
            _buffer[58] = (byte)(totalBits >> 40);
            _buffer[59] = (byte)(totalBits >> 32);
            _buffer[60] = (byte)(totalBits >> 24);
            _buffer[61] = (byte)(totalBits >> 16);
            _buffer[62] = (byte)(totalBits >> 8);
            _buffer[63] = (byte)(totalBits);

            ProcessBlock(_buffer, 0);

            for (int i = 0; i < 5; i++)
            {
                output[outOffset + i * 4] = (byte)(_h[i] >> 24);
                output[outOffset + i * 4 + 1] = (byte)(_h[i] >> 16);
                output[outOffset + i * 4 + 2] = (byte)(_h[i] >> 8);
                output[outOffset + i * 4 + 3] = (byte)(_h[i]);
            }
        }

        private void ProcessBlock(byte[] block, int offset)
        {
            for (int t = 0; t < 16; t++)
            {
                int idx = offset + t * 4;
                _w[t] = ((uint)block[idx] << 24) |
                        ((uint)block[idx + 1] << 16) |
                        ((uint)block[idx + 2] << 8) |
                        (uint)block[idx + 3];
            }

            for (int t = 16; t < 80; t++)
            {
                uint temp = _w[t - 3] ^ _w[t - 8] ^ _w[t - 14] ^ _w[t - 16];
                _w[t] = (temp << 1) | (temp >> 31);
            }

            uint a = _h[0];
            uint b = _h[1];
            uint c = _h[2];
            uint d = _h[3];
            uint e = _h[4];

            for (int t = 0; t < 80; t++)
            {
                uint f, k;
                if (t < 20)
                {
                    f = (b & c) | ((~b) & d);
                    k = 0x5A827999u;
                }
                else if (t < 40)
                {
                    f = b ^ c ^ d;
                    k = 0x6ED9EBA1u;
                }
                else if (t < 60)
                {
                    f = (b & c) | (b & d) | (c & d);
                    k = 0x8F1BBCDCu;
                }
                else
                {
                    f = b ^ c ^ d;
                    k = 0xCA62C1D6u;
                }

                uint temp = ((a << 5) | (a >> 27)) + f + e + k + _w[t];
                e = d;
                d = c;
                c = (b << 30) | (b >> 2);
                b = a;
                a = temp;
            }

            _h[0] += a;
            _h[1] += b;
            _h[2] += c;
            _h[3] += d;
            _h[4] += e;
        }

        public static byte[] Hash(byte[] data)
        {
            var sha = new PureSha1();
            sha.Update(data, 0, data != null ? data.Length : 0);
            byte[] res = new byte[20];
            sha.Final(res, 0);
            return res;
        }
    }

    /// <summary>
    /// Pure C# RFC 2104 HMAC-SHA1 implementation with zero heap allocations during iterations.
    /// </summary>
    public class PureHmacSha1
    {
        private PureSha1 _sha = new PureSha1();
        private byte[] _keyPad = new byte[64];
        private byte[] _innerHash = new byte[20];
        private byte[] _ipad = new byte[64];
        private byte[] _opad = new byte[64];

        public void ComputeHmac(byte[] key, byte[] data, int dataOffset, int dataLen, byte[] output, int outOffset)
        {
            Array.Clear(_keyPad, 0, 64);
            if (key != null)
            {
                if (key.Length > 64)
                {
                    byte[] kh = PureSha1.Hash(key);
                    Array.Copy(kh, 0, _keyPad, 0, 20);
                }
                else
                {
                    Array.Copy(key, 0, _keyPad, 0, key.Length);
                }
            }

            for (int i = 0; i < 64; i++)
            {
                _ipad[i] = (byte)(_keyPad[i] ^ 0x36);
                _opad[i] = (byte)(_keyPad[i] ^ 0x5C);
            }

            _sha.Reset();
            _sha.Update(_ipad, 0, 64);
            if (data != null && dataLen > 0)
            {
                _sha.Update(data, dataOffset, dataLen);
            }
            _sha.Final(_innerHash, 0);

            _sha.Reset();
            _sha.Update(_opad, 0, 64);
            _sha.Update(_innerHash, 0, 20);
            _sha.Final(output, outOffset);
        }

        public static byte[] Compute(byte[] key, byte[] data)
        {
            var hmac = new PureHmacSha1();
            byte[] res = new byte[20];
            hmac.ComputeHmac(key, data, 0, data != null ? data.Length : 0, res, 0);
            return res;
        }
    }

    /// <summary>
    /// Pure C# RFC 2898 / PKCS#5 v2.0 PBKDF2-HMAC-SHA1 for deriving 32-byte PMK.
    /// </summary>
    public static class PurePbkdf2
    {
        public static byte[] DerivePmk(string passphrase, string ssid)
        {
            byte[] passBytes = Encoding.UTF8.GetBytes(passphrase ?? "");
            byte[] ssidBytes = Encoding.UTF8.GetBytes(ssid ?? "");
            return DeriveKey(passBytes, ssidBytes, 4096, 32);
        }

        public static byte[] DeriveKey(byte[] password, byte[] salt, int iterations, int keyLen)
        {
            byte[] output = new byte[keyLen];
            var hmac = new PureHmacSha1();
            byte[] saltPlusBlock = new byte[salt.Length + 4];
            Array.Copy(salt, 0, saltPlusBlock, 0, salt.Length);

            byte[] uBlock = new byte[20];
            byte[] uPrev = new byte[20];
            byte[] uCur = new byte[20];

            int totalBlocks = (keyLen + 19) / 20;
            int outOffset = 0;

            for (int block = 1; block <= totalBlocks; block++)
            {
                saltPlusBlock[salt.Length] = (byte)(block >> 24);
                saltPlusBlock[salt.Length + 1] = (byte)(block >> 16);
                saltPlusBlock[salt.Length + 2] = (byte)(block >> 8);
                saltPlusBlock[salt.Length + 3] = (byte)block;

                hmac.ComputeHmac(password, saltPlusBlock, 0, saltPlusBlock.Length, uPrev, 0);
                Array.Copy(uPrev, 0, uBlock, 0, 20);

                for (int iter = 2; iter <= iterations; iter++)
                {
                    hmac.ComputeHmac(password, uPrev, 0, 20, uCur, 0);
                    for (int x = 0; x < 20; x++)
                    {
                        uBlock[x] ^= uCur[x];
                        uPrev[x] = uCur[x];
                    }
                }

                int toCopy = Math.Min(20, keyLen - outOffset);
                Array.Copy(uBlock, 0, output, outOffset, toCopy);
                outOffset += toCopy;
            }

            return output;
        }
    }

    /// <summary>
    /// Pure C# IEEE 802.11i-2004 PRF-512 Pseudo-Random Function for PTK derivation.
    /// </summary>
    public static class PurePrf512
    {
        public static byte[] DerivePtk(byte[] pmk, byte[] aa, byte[] spa, byte[] anonce, byte[] snonce)
        {
            byte[] label = Encoding.ASCII.GetBytes("Pairwise key expansion");

            // Lexicographical order: Min(AA, SPA) || Max(AA, SPA)
            byte[] minMac = LessThan(aa, spa) ? aa : spa;
            byte[] maxMac = LessThan(aa, spa) ? spa : aa;

            // Lexicographical order: Min(ANonce, SNonce) || Max(ANonce, SNonce)
            byte[] minNonce = LessThan(anonce, snonce) ? anonce : snonce;
            byte[] maxNonce = LessThan(anonce, snonce) ? snonce : anonce;

            byte[] dataB = new byte[12 + 64];
            Array.Copy(minMac, 0, dataB, 0, 6);
            Array.Copy(maxMac, 0, dataB, 6, 6);
            Array.Copy(minNonce, 0, dataB, 12, 32);
            Array.Copy(maxNonce, 0, dataB, 44, 32);

            byte[] input = new byte[label.Length + 1 + dataB.Length + 1];
            Array.Copy(label, 0, input, 0, label.Length);
            input[label.Length] = 0x00;
            Array.Copy(dataB, 0, input, label.Length + 1, dataB.Length);

            byte[] ptk = new byte[64];
            byte[] hmacOut = new byte[20];
            var hmac = new PureHmacSha1();

            for (byte i = 0; i < 4; i++)
            {
                input[input.Length - 1] = i;
                hmac.ComputeHmac(pmk, input, 0, input.Length, hmacOut, 0);
                int copyLen = Math.Min(20, 64 - i * 20);
                Array.Copy(hmacOut, 0, ptk, i * 20, copyLen);
            }

            return ptk;
        }

        private static bool LessThan(byte[] a, byte[] b)
        {
            int len = Math.Min(a.Length, b.Length);
            for (int i = 0; i < len; i++)
            {
                if (a[i] < b[i]) return true;
                if (a[i] > b[i]) return false;
            }
            return a.Length < b.Length;
        }
    }

    /// <summary>
    /// Pure bare-metal IEEE 802.11i WPA2-PSK 4-Way Handshake Engine.
    /// Authenticates with modern WPA2-PSK access points and phone hotspots.
    /// Derives PMK, PTK, KCK, KEK, and Temporal Key (TK), sends valid EAPOL Messages 2 & 4,
    /// verifies AP Message 3 MIC, and rejects incorrect passwords.
    /// </summary>
    public class Wpa2HandshakeEngine
    {
        public Wpa2HandshakeState State { get; set; } = Wpa2HandshakeState.Idle;
        public string FailureReason { get; set; } = "";

        public byte[] ApBssid { get; private set; } = new byte[6];
        public byte[] StationMac { get; private set; } = new byte[6];
        public string Ssid { get; private set; } = "";
        public string Passphrase { get; private set; } = "";
        public int Channel { get; private set; } = 1;

        public byte[] Pmk { get; private set; } = new byte[32];
        public byte[] Ptk { get; private set; } = new byte[64];
        public byte[] Kck { get; private set; } = new byte[16];
        public byte[] Kek { get; private set; } = new byte[16];
        public byte[] Tk { get; private set; } = new byte[16];

        public byte[] ANonce { get; private set; } = new byte[32];
        public byte[] SNonce { get; private set; } = new byte[32];
        public byte[] Msg1ReplayCounter { get; private set; } = new byte[8];
        public byte[] Msg3ReplayCounter { get; private set; } = new byte[8];

        public ushort SequenceNumber = 1;

        // Station RSN Information Element (Tag 48, Length 20)
        private static readonly byte[] StationRsnIe = new byte[] {
            0x30, 0x14,
            0x01, 0x00,                         // Version: 1
            0x00, 0x0F, 0xAC, 0x04,             // Group Cipher: AES-CCMP
            0x01, 0x00, 0x00, 0x0F, 0xAC, 0x04, // Pairwise Cipher: 1 AES-CCMP
            0x01, 0x00, 0x00, 0x0F, 0xAC, 0x02, // AKM Suite: 1 PSK
            0x00, 0x00                          // RSN Capabilities
        };

        public void Initialize(byte[] apBssid, byte[] stationMac, string ssid, string passphrase, int channel)
        {
            Array.Copy(apBssid, 0, ApBssid, 0, 6);
            Array.Copy(stationMac, 0, StationMac, 0, 6);
            Ssid = ssid ?? "";
            Passphrase = passphrase ?? "";
            Channel = channel;
            State = Wpa2HandshakeState.ConnectingAuthAssoc;
            FailureReason = "";

            // Derive 32-byte PMK using PBKDF2 (4096 iterations)
            Pmk = PurePbkdf2.DerivePmk(Passphrase, Ssid);
        }

        public static byte[] GenerateSNonce(byte[] stationMac, byte[] apBssid, uint tsfL, uint tsfU)
        {
            byte[] entropy = new byte[36];
            for (int i = 0; i < 6; i++)
            {
                entropy[i] = stationMac != null && i < stationMac.Length ? stationMac[i] : (byte)0x11;
                entropy[6 + i] = apBssid != null && i < apBssid.Length ? apBssid[i] : (byte)0x22;
            }
            entropy[12] = (byte)(tsfL);
            entropy[13] = (byte)(tsfL >> 8);
            entropy[14] = (byte)(tsfL >> 16);
            entropy[15] = (byte)(tsfL >> 24);
            entropy[16] = (byte)(tsfU);
            entropy[17] = (byte)(tsfU >> 8);
            entropy[18] = (byte)(tsfU >> 16);
            entropy[19] = (byte)(tsfU >> 24);
            entropy[20] = 0xAA; entropy[21] = 0x55; entropy[22] = 0x12; entropy[23] = 0x34;
            entropy[24] = 0x56; entropy[25] = 0x78; entropy[26] = 0x9A; entropy[27] = 0xBC;
            entropy[28] = 0xDE; entropy[29] = 0xF0; entropy[30] = 0xFE; entropy[31] = 0xED;
            entropy[32] = 0xCA; entropy[33] = 0xFE; entropy[34] = 0xBA; entropy[35] = 0xBE;

            byte[] h1 = PureSha1.Hash(entropy);
            for (int i = 0; i < 36; i++) entropy[i] ^= 0x5C;
            byte[] h2 = PureSha1.Hash(entropy);

            byte[] snonce = new byte[32];
            Array.Copy(h1, 0, snonce, 0, 20);
            Array.Copy(h2, 0, snonce, 20, 12);
            return snonce;
        }

        public bool ProcessEapolFrame(byte[] pkt, int eapolOffset, uint tsfL, uint tsfU, out byte[] responseFrame)
        {
            responseFrame = null;
            if (pkt == null || eapolOffset + 4 > pkt.Length) return false;

            byte eapolType = pkt[eapolOffset + 1];
            if (eapolType != 3) return false; // 3 = EAPOL-Key

            int bodyLen = (pkt[eapolOffset + 2] << 8) | pkt[eapolOffset + 3];
            if (eapolOffset + 4 + bodyLen > pkt.Length) return false;

            int kbOffset = eapolOffset + 4;
            byte descType = pkt[kbOffset];
            if (descType != 2 && descType != 254) return false; // 2 = RSN, 254 = WPA

            ushort keyInfo = (ushort)((pkt[kbOffset + 1] << 8) | pkt[kbOffset + 2]);
            bool isPairwise = (keyInfo & (1 << 3)) != 0;
            bool isInstall = (keyInfo & (1 << 6)) != 0;
            bool isAck = (keyInfo & (1 << 7)) != 0;
            bool isMic = (keyInfo & (1 << 8)) != 0;

            // Handle Message 1 (Pairwise=1, Ack=1, MIC=0)
            if (isPairwise && isAck && !isMic)
            {
                // Replay counter (8 bytes at kbOffset + 5)
                for (int i = 0; i < 8; i++) Msg1ReplayCounter[i] = pkt[kbOffset + 5 + i];

                // ANonce (32 bytes at kbOffset + 13)
                for (int i = 0; i < 32; i++) ANonce[i] = pkt[kbOffset + 13 + i];

                // Generate SNonce
                SNonce = GenerateSNonce(StationMac, ApBssid, tsfL, tsfU);

                // Derive PTK (64 bytes)
                Ptk = PurePrf512.DerivePtk(Pmk, ApBssid, StationMac, ANonce, SNonce);
                Array.Copy(Ptk, 0, Kck, 0, 16);
                Array.Copy(Ptk, 16, Kek, 0, 16);
                Array.Copy(Ptk, 32, Tk, 0, 16);

                // Construct Message 2
                responseFrame = BuildMessage2();
                State = Wpa2HandshakeState.Msg2Sent;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  [EAPOL] Message 1/4 received from AP! Sent Message 2/4 (with computed MIC).");
                Console.ForegroundColor = ConsoleColor.Cyan;
                return true;
            }

            // Handle Message 3 (Pairwise=1, Ack=1, MIC=1)
            if (isPairwise && isAck && isMic && (State == Wpa2HandshakeState.Msg2Sent || State == Wpa2HandshakeState.WaitingMsg3))
            {
                for (int i = 0; i < 8; i++) Msg3ReplayCounter[i] = pkt[kbOffset + 5 + i];

                // Extract AP MIC (16 bytes at kbOffset + 77)
                byte[] apMic = new byte[16];
                for (int i = 0; i < 16; i++) apMic[i] = pkt[kbOffset + 77 + i];

                // Zero out MIC in a copy of the packet and verify HMAC-SHA1
                int eapolPacketLen = 4 + bodyLen;
                byte[] verifyBuf = new byte[eapolPacketLen];
                Array.Copy(pkt, eapolOffset, verifyBuf, 0, eapolPacketLen);
                for (int i = 0; i < 16; i++) verifyBuf[4 + 77 + i] = 0;

                byte[] computedHmac = PureHmacSha1.Compute(Kck, verifyBuf);
                bool valid = true;
                for (int i = 0; i < 16; i++)
                {
                    if (computedHmac[i] != apMic[i])
                    {
                        valid = false;
                        break;
                    }
                }

                if (!valid)
                {
                    State = Wpa2HandshakeState.Failed;
                    FailureReason = "AP Message 3 MIC verification failed! Incorrect passphrase.";
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("  [EAPOL] Message 3/4 MIC mismatch! Password incorrect.");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    return false;
                }

                // AP Message 3 verified! Build Message 4
                responseFrame = BuildMessage4();
                State = Wpa2HandshakeState.Completed;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  [EAPOL] Message 3/4 verified! AP acknowledged station. Sent Message 4/4.");
                Console.ForegroundColor = ConsoleColor.Cyan;
                return true;
            }

            return false;
        }

        public byte[] BuildMessage2()
        {
            int rsnLen = StationRsnIe.Length; // 22 bytes
            int bodyLen = 95 + rsnLen;       // 117 bytes
            int eapolLen = 4 + bodyLen;      // 121 bytes

            byte[] eapol = new byte[eapolLen];
            eapol[0] = 0x01; // Version 1
            eapol[1] = 0x03; // EAPOL-Key
            eapol[2] = (byte)(bodyLen >> 8);
            eapol[3] = (byte)(bodyLen & 0xFF);

            int kb = 4;
            eapol[kb] = 0x02; // Descriptor Type: RSN
            // Key Info: Pairwise=1 (bit 3), MIC=1 (bit 8), Version=2 (bits 0-2) -> 0x010A
            eapol[kb + 1] = 0x01;
            eapol[kb + 2] = 0x0A;
            eapol[kb + 3] = 0x00; // Key Length: 0
            eapol[kb + 4] = 0x00;

            // Replay Counter matching Message 1
            for (int i = 0; i < 8; i++) eapol[kb + 5 + i] = Msg1ReplayCounter[i];

            // SNonce
            for (int i = 0; i < 32; i++) eapol[kb + 13 + i] = SNonce[i];

            // Key IV (16 bytes = 0), RSC (8 bytes = 0), Reserved (8 bytes = 0), MIC (16 bytes = 0)
            // Array defaults to 0.

            // Key Data Length (22 bytes)
            eapol[kb + 93] = (byte)(rsnLen >> 8);
            eapol[kb + 94] = (byte)(rsnLen & 0xFF);

            // Key Data: Station RSN IE
            Array.Copy(StationRsnIe, 0, eapol, kb + 95, rsnLen);

            // Compute 16-byte HMAC-SHA1 MIC over whole EAPOL frame
            byte[] mic = PureHmacSha1.Compute(Kck, eapol);
            Array.Copy(mic, 0, eapol, kb + 77, 16);

            return WrapIn80211DataFrame(eapol);
        }

        public byte[] BuildMessage4()
        {
            int bodyLen = 95;           // 95 bytes (Key Data Length = 0)
            int eapolLen = 4 + bodyLen; // 99 bytes

            byte[] eapol = new byte[eapolLen];
            eapol[0] = 0x01; // Version 1
            eapol[1] = 0x03; // EAPOL-Key
            eapol[2] = (byte)(bodyLen >> 8);
            eapol[3] = (byte)(bodyLen & 0xFF);

            int kb = 4;
            eapol[kb] = 0x02; // Descriptor Type: RSN
            // Key Info: Pairwise=1 (bit 3), MIC=1 (bit 8), Secure=1 (bit 9), Version=2 (bits 0-2) -> 0x030A
            eapol[kb + 1] = 0x03;
            eapol[kb + 2] = 0x0A;
            eapol[kb + 3] = 0x00; // Key Length: 0
            eapol[kb + 4] = 0x00;

            // Replay Counter matching Message 3
            for (int i = 0; i < 8; i++) eapol[kb + 5 + i] = Msg3ReplayCounter[i];

            // SNonce is 0 in Message 4
            // Key IV, RSC, Reserved, MIC are 0.
            eapol[kb + 93] = 0x00; // Key Data Length = 0
            eapol[kb + 94] = 0x00;

            // Compute 16-byte HMAC-SHA1 MIC
            byte[] mic = PureHmacSha1.Compute(Kck, eapol);
            Array.Copy(mic, 0, eapol, kb + 77, 16);

            return WrapIn80211DataFrame(eapol);
        }

        private byte[] WrapIn80211DataFrame(byte[] eapolPayload)
        {
            // Standard 802.11 Data Frame (compatible with all APs & phone hotspots):
            // 24 bytes MAC header + 8 bytes LLC/SNAP + EAPOL
            int totalLen = 24 + 8 + eapolPayload.Length;
            byte[] frame = new byte[totalLen];

            // Frame Control: Data (0x08), ToDS=1 (0x01)
            frame[0] = 0x08;
            frame[1] = 0x01;
            frame[2] = 0x00; // Duration
            frame[3] = 0x00;

            // Address 1: Receiver / AP BSSID
            Array.Copy(ApBssid, 0, frame, 4, 6);
            // Address 2: Transmitter / Station MAC
            Array.Copy(StationMac, 0, frame, 10, 6);
            // Address 3: Destination / AP BSSID
            Array.Copy(ApBssid, 0, frame, 16, 6);

            // Sequence Control
            ushort seq = (ushort)((SequenceNumber++ & 0x0FFF) << 4);
            frame[22] = (byte)(seq & 0xFF);
            frame[23] = (byte)(seq >> 8);

            // LLC / SNAP Header (802.1X EtherType 0x888E)
            frame[24] = 0xAA;
            frame[25] = 0xAA;
            frame[26] = 0x03;
            frame[27] = 0x00;
            frame[28] = 0x00;
            frame[29] = 0x00;
            frame[30] = 0x88;
            frame[31] = 0x8E;

            // EAPOL Payload
            Array.Copy(eapolPayload, 0, frame, 32, eapolPayload.Length);

            return frame;
        }
    }

public static class PureAes128
    {
        private static readonly byte[] SBox = new byte[256] {
            0x63, 0x7c, 0x77, 0x7b, 0xf2, 0x6b, 0x6f, 0xc5, 0x30, 0x01, 0x67, 0x2b, 0xfe, 0xd7, 0xab, 0x76,
            0xca, 0x82, 0xc9, 0x7d, 0xfa, 0x59, 0x47, 0xf0, 0xad, 0xd4, 0xa2, 0xaf, 0x9c, 0xa4, 0x72, 0xc0,
            0xb7, 0xfd, 0x93, 0x26, 0x36, 0x3f, 0xf7, 0xcc, 0x34, 0xa5, 0xe5, 0xf1, 0x71, 0xd8, 0x31, 0x15,
            0x04, 0xc7, 0x23, 0xc3, 0x18, 0x96, 0x05, 0x9a, 0x07, 0x12, 0x80, 0xe2, 0xeb, 0x27, 0xb2, 0x75,
            0x09, 0x83, 0x2c, 0x1a, 0x1b, 0x6e, 0x5a, 0xa0, 0x52, 0x3b, 0xd6, 0xb3, 0x29, 0xe3, 0x2f, 0x84,
            0x53, 0xd1, 0x00, 0xed, 0x20, 0xfc, 0xb1, 0x5b, 0x6a, 0xcb, 0xbe, 0x39, 0x4a, 0x4c, 0x58, 0xcf,
            0xd0, 0xef, 0xaa, 0xfb, 0x43, 0x4d, 0x33, 0x85, 0x45, 0xf9, 0x02, 0x7f, 0x50, 0x3c, 0x9f, 0xa8,
            0x51, 0xa3, 0x40, 0x8f, 0x92, 0x9d, 0x38, 0xf5, 0xbc, 0xb6, 0xda, 0x21, 0x10, 0xff, 0xf3, 0xd2,
            0xcd, 0x0c, 0x13, 0xec, 0x5f, 0x97, 0x44, 0x17, 0xc4, 0xa7, 0x7e, 0x3d, 0x64, 0x5d, 0x19, 0x73,
            0x60, 0x81, 0x4f, 0xdc, 0x22, 0x2a, 0x90, 0x88, 0x46, 0xee, 0xb8, 0x14, 0xde, 0x5e, 0x0b, 0xdb,
            0xe0, 0x32, 0x3a, 0x0a, 0x49, 0x06, 0x24, 0x5c, 0xc2, 0xd3, 0xac, 0x62, 0x91, 0x95, 0xe4, 0x79,
            0xe7, 0xc8, 0x37, 0x6d, 0x8d, 0xd5, 0x4e, 0xa9, 0x6c, 0x56, 0xf4, 0xea, 0x65, 0x7a, 0xae, 0x08,
            0xba, 0x78, 0x25, 0x2e, 0x1c, 0xa6, 0xb4, 0xc6, 0xe8, 0xdd, 0x74, 0x1f, 0x4b, 0xbd, 0x8b, 0x8a,
            0x70, 0x3e, 0xb5, 0x66, 0x48, 0x03, 0xf6, 0x0e, 0x61, 0x35, 0x57, 0xb9, 0x86, 0xc1, 0x1d, 0x9e,
            0xe1, 0xf8, 0x98, 0x11, 0x69, 0xd9, 0x8e, 0x94, 0x9b, 0x1e, 0x87, 0xe9, 0xce, 0x55, 0x28, 0xdf,
            0x8c, 0xa1, 0x89, 0x0d, 0xbf, 0xe6, 0x42, 0x68, 0x41, 0x99, 0x2d, 0x0f, 0xb0, 0x54, 0xbb, 0x16
        };

        private static readonly byte[] RCon = new byte[10] {
            0x01, 0x02, 0x04, 0x08, 0x10, 0x20, 0x40, 0x80, 0x1B, 0x36
        };

        public static uint[] KeyExpansion(byte[] key)
        {
            uint[] w = new uint[44];
            for (int i = 0; i < 4; i++)
            {
                w[i] = ((uint)key[4 * i] << 24) | ((uint)key[4 * i + 1] << 16) | ((uint)key[4 * i + 2] << 8) | (uint)key[4 * i + 3];
            }
            for (int i = 4; i < 44; i++)
            {
                uint temp = w[i - 1];
                if (i % 4 == 0)
                {
                    uint rot = (temp << 8) | (temp >> 24);
                    uint sub = ((uint)SBox[(rot >> 24) & 0xFF] << 24) |
                               ((uint)SBox[(rot >> 16) & 0xFF] << 16) |
                               ((uint)SBox[(rot >> 8) & 0xFF] << 8) |
                               (uint)SBox[rot & 0xFF];
                    temp = sub ^ ((uint)RCon[(i / 4) - 1] << 24);
                }
                w[i] = w[i - 4] ^ temp;
            }
            return w;
        }

        private static byte XTime(byte a)
        {
            return (byte)(((a << 1) ^ (((a & 0x80) != 0) ? 0x1B : 0x00)) & 0xFF);
        }

        private static void MixSingleColumn(byte[] a)
        {
            byte t = (byte)(a[0] ^ a[1] ^ a[2] ^ a[3]);
            byte u = a[0];
            a[0] ^= (byte)(t ^ XTime((byte)(a[0] ^ a[1])));
            a[1] ^= (byte)(t ^ XTime((byte)(a[1] ^ a[2])));
            a[2] ^= (byte)(t ^ XTime((byte)(a[2] ^ a[3])));
            a[3] ^= (byte)(t ^ XTime((byte)(a[3] ^ u)));
        }

        public static void EncryptBlock(uint[] w, byte[] inBlock, int inOffset, byte[] outBlock, int outOffset)
        {
            byte[] state = new byte[16];
            for (int i = 0; i < 16; i++)
            {
                state[i] = inBlock[inOffset + i];
            }

            for (int c = 0; c < 4; c++)
            {
                uint kw = w[c];
                int c4 = c * 4;
                state[0 + c4] ^= (byte)((kw >> 24) & 0xFF);
                state[1 + c4] ^= (byte)((kw >> 16) & 0xFF);
                state[2 + c4] ^= (byte)((kw >> 8) & 0xFF);
                state[3 + c4] ^= (byte)(kw & 0xFF);
            }

            byte[] col = new byte[4];
            for (int round = 1; round < 10; round++)
            {
                for (int i = 0; i < 16; i++)
                    state[i] = SBox[state[i]];

                // ShiftRows
                byte t = state[1 + 0];
                state[1 + 0] = state[1 + 4];
                state[1 + 4] = state[1 + 8];
                state[1 + 8] = state[1 + 12];
                state[1 + 12] = t;

                t = state[2 + 0]; byte t2 = state[2 + 4];
                state[2 + 0] = state[2 + 8];
                state[2 + 4] = state[2 + 12];
                state[2 + 8] = t;
                state[2 + 12] = t2;

                t = state[3 + 12];
                state[3 + 12] = state[3 + 8];
                state[3 + 8] = state[3 + 4];
                state[3 + 4] = state[3 + 0];
                state[3 + 0] = t;

                // MixColumns
                for (int c = 0; c < 4; c++)
                {
                    int c4 = c * 4;
                    col[0] = state[0 + c4];
                    col[1] = state[1 + c4];
                    col[2] = state[2 + c4];
                    col[3] = state[3 + c4];
                    MixSingleColumn(col);
                    state[0 + c4] = col[0];
                    state[1 + c4] = col[1];
                    state[2 + c4] = col[2];
                    state[3 + c4] = col[3];
                }

                // AddRoundKey
                for (int c = 0; c < 4; c++)
                {
                    uint kw = w[round * 4 + c];
                    int c4 = c * 4;
                    state[0 + c4] ^= (byte)((kw >> 24) & 0xFF);
                    state[1 + c4] ^= (byte)((kw >> 16) & 0xFF);
                    state[2 + c4] ^= (byte)((kw >> 8) & 0xFF);
                    state[3 + c4] ^= (byte)(kw & 0xFF);
                }
            }

            // Final round
            for (int i = 0; i < 16; i++)
                state[i] = SBox[state[i]];

            byte finT = state[1 + 0];
            state[1 + 0] = state[1 + 4];
            state[1 + 4] = state[1 + 8];
            state[1 + 8] = state[1 + 12];
            state[1 + 12] = finT;

            finT = state[2 + 0]; byte finT2 = state[2 + 4];
            state[2 + 0] = state[2 + 8];
            state[2 + 4] = state[2 + 12];
            state[2 + 8] = finT;
            state[2 + 12] = finT2;

            finT = state[3 + 12];
            state[3 + 12] = state[3 + 8];
            state[3 + 8] = state[3 + 4];
            state[3 + 4] = state[3 + 0];
            state[3 + 0] = finT;

            for (int c = 0; c < 4; c++)
            {
                uint kw = w[40 + c];
                int c4 = c * 4;
                state[0 + c4] ^= (byte)((kw >> 24) & 0xFF);
                state[1 + c4] ^= (byte)((kw >> 16) & 0xFF);
                state[2 + c4] ^= (byte)((kw >> 8) & 0xFF);
                state[3 + c4] ^= (byte)(kw & 0xFF);
            }

            for (int i = 0; i < 16; i++)
            {
                outBlock[outOffset + i] = state[i];
            }
        }
    }

    public static class PureCcmp
    {
        public static byte[] Encrypt(byte[] tk, byte[] staMac, byte[] apBssid, ref ulong pn, byte[] ethBuf, int ethOff, int ethLen, ushort seqNum)
        {
            uint[] w = PureAes128.KeyExpansion(tk);
            int pLen = 8 + (ethLen - 14); // LLC/SNAP (8B) + payload
            byte[] plaintext = new byte[pLen];
            plaintext[0] = 0xAA; plaintext[1] = 0xAA; plaintext[2] = 0x03;
            plaintext[3] = 0x00; plaintext[4] = 0x00; plaintext[5] = 0x00;
            plaintext[6] = ethBuf[ethOff + 12]; // EtherType
            plaintext[7] = ethBuf[ethOff + 13];
            Array.Copy(ethBuf, ethOff + 14, plaintext, 8, ethLen - 14);

            byte[] pnBytes = new byte[6];
            for (int i = 0; i < 6; i++)
            {
                pnBytes[i] = (byte)((pn >> (8 * i)) & 0xFF);
            }
            pn++; // Increment packet number

            // Construct Nonce (13 bytes): Priority (0) + A2 (staMac) + big-endian PN
            byte[] nonce = new byte[13];
            nonce[0] = 0x00;
            Array.Copy(staMac, 0, nonce, 1, 6);
            nonce[7] = pnBytes[5]; nonce[8] = pnBytes[4]; nonce[9] = pnBytes[3];
            nonce[10] = pnBytes[2]; nonce[11] = pnBytes[1]; nonce[12] = pnBytes[0];

            // Construct B0 (16 bytes): 0x59 + Nonce (13B) + Plaintext Length (2B)
            byte[] b0 = new byte[16];
            b0[0] = 0x59;
            Array.Copy(nonce, 0, b0, 1, 13);
            b0[14] = (byte)((pLen >> 8) & 0xFF);
            b0[15] = (byte)(pLen & 0xFF);

            // Construct AAD (22 bytes for non-QoS):
            // FC (0x08, 0x41), masked FC (0x08, 0x01)
            // A1 (apBssid), A2 (staMac), A3 (Ethernet dest MAC)
            // SC (seqNum << 4), masked SC (sc[0] & 0x0F, 0x00)
            byte[] aad = new byte[22];
            aad[0] = 0x08;
            aad[1] = 0x01; // ToDS=1, Protected masked to 0
            Array.Copy(apBssid, 0, aad, 2, 6);
            Array.Copy(staMac, 0, aad, 8, 6);
            Array.Copy(ethBuf, ethOff, aad, 14, 6); // A3 = Dest MAC
            ushort sc = (ushort)((seqNum & 0x0FFF) << 4);
            aad[20] = (byte)((sc & 0xFF) & 0x0F);
            aad[21] = 0x00;

            byte[] aadBlock = new byte[32];
            aadBlock[0] = 0x00; aadBlock[1] = 0x16;
            Array.Copy(aad, 0, aadBlock, 2, 22);

            // AES-CBC-MAC
            byte[] x = new byte[16];
            byte[] tmpIn = new byte[16];
            PureAes128.EncryptBlock(w, b0, 0, x, 0);

            for (int i = 0; i < 16; i++) tmpIn[i] = (byte)(x[i] ^ aadBlock[i]);
            PureAes128.EncryptBlock(w, tmpIn, 0, x, 0);

            for (int i = 0; i < 16; i++) tmpIn[i] = (byte)(x[i] ^ aadBlock[16 + i]);
            PureAes128.EncryptBlock(w, tmpIn, 0, x, 0);

            int numBlocks = (pLen + 15) / 16;
            for (int b = 0; b < numBlocks; b++)
            {
                int bStart = b * 16;
                int bLen = pLen - bStart;
                if (bLen > 16) bLen = 16;
                for (int i = 0; i < 16; i++)
                {
                    byte pb = (i < bLen) ? plaintext[bStart + i] : (byte)0;
                    tmpIn[i] = (byte)(x[i] ^ pb);
                }
                PureAes128.EncryptBlock(w, tmpIn, 0, x, 0);
            }

            // CTR0 for MIC
            byte[] ctr0 = new byte[16];
            ctr0[0] = 0x01;
            Array.Copy(nonce, 0, ctr0, 1, 13);
            byte[] s0 = new byte[16];
            PureAes128.EncryptBlock(w, ctr0, 0, s0, 0);
            byte[] mic = new byte[8];
            for (int i = 0; i < 8; i++) mic[i] = (byte)(x[i] ^ s0[i]);

            // CTR1..CTRN for payload encryption
            byte[] ciphertext = new byte[pLen];
            byte[] ctri = new byte[16];
            byte[] si = new byte[16];
            ctri[0] = 0x01;
            Array.Copy(nonce, 0, ctri, 1, 13);

            for (int b = 0; b < numBlocks; b++)
            {
                int ctrVal = b + 1;
                ctri[14] = (byte)((ctrVal >> 8) & 0xFF);
                ctri[15] = (byte)(ctrVal & 0xFF);
                PureAes128.EncryptBlock(w, ctri, 0, si, 0);

                int bStart = b * 16;
                int bLen = pLen - bStart;
                if (bLen > 16) bLen = 16;
                for (int i = 0; i < bLen; i++)
                {
                    ciphertext[bStart + i] = (byte)(plaintext[bStart + i] ^ si[i]);
                }
            }

            // Assemble Full 802.11 Frame (24B MAC Hdr + 8B CCMP Hdr + Ciphertext + 8B MIC)
            int frameLen = 24 + 8 + pLen + 8;
            byte[] frame = new byte[frameLen];

            // MAC Header: Data (0x08), ToDS=1 | Protected=1 (0x41)
            frame[0] = 0x08;
            frame[1] = 0x41;
            frame[2] = 0x00; // Duration
            frame[3] = 0x00;
            Array.Copy(apBssid, 0, frame, 4, 6);   // A1: RA (AP)
            Array.Copy(staMac, 0, frame, 10, 6);   // A2: TA (Station)
            Array.Copy(ethBuf, ethOff, frame, 16, 6); // A3: DA (Destination)
            frame[22] = (byte)(sc & 0xFF);
            frame[23] = (byte)(sc >> 8);

            // CCMP Header (8 bytes): PN0, PN1, 0x00, 0x20, PN2, PN3, PN4, PN5
            frame[24] = pnBytes[0];
            frame[25] = pnBytes[1];
            frame[26] = 0x00;
            frame[27] = 0x20;
            frame[28] = pnBytes[2];
            frame[29] = pnBytes[3];
            frame[30] = pnBytes[4];
            frame[31] = pnBytes[5];

            // Ciphertext & MIC
            Array.Copy(ciphertext, 0, frame, 32, pLen);
            Array.Copy(mic, 0, frame, 32 + pLen, 8);

            return frame;
        }

        public static byte[] Decrypt(byte[] tk, byte[] frame)
        {
            if (tk == null || frame == null || frame.Length < 40) return null;

            byte fc0 = frame[0];
            byte fc1 = frame[1];
            bool isQos = (fc0 & 0x80) != 0;
            int hdrLen = isQos ? 26 : 24;

            if (frame.Length < hdrLen + 16) return null;

            byte[] a1 = new byte[6]; Array.Copy(frame, 4, a1, 0, 6);
            byte[] a2 = new byte[6]; Array.Copy(frame, 10, a2, 0, 6);
            byte[] a3 = new byte[6]; Array.Copy(frame, 16, a3, 0, 6);
            byte sc0 = frame[22];
            byte tid = isQos ? (byte)(frame[24] & 0x0F) : (byte)0;

            int ccmpOff = hdrLen;
            byte[] pnBytes = new byte[6] {
                frame[ccmpOff], frame[ccmpOff + 1],
                frame[ccmpOff + 4], frame[ccmpOff + 5],
                frame[ccmpOff + 6], frame[ccmpOff + 7]
            };

            int cipherStart = ccmpOff + 8;
            int micStart = frame.Length - 8;
            int pLen = micStart - cipherStart;
            if (pLen < 8) return null; // Must contain at least LLC/SNAP (8B)

            uint[] w = PureAes128.KeyExpansion(tk);

            byte priority = tid;
            byte[] nonce = new byte[13];
            nonce[0] = priority;
            Array.Copy(a2, 0, nonce, 1, 6);
            nonce[7] = pnBytes[5]; nonce[8] = pnBytes[4]; nonce[9] = pnBytes[3];
            nonce[10] = pnBytes[2]; nonce[11] = pnBytes[1]; nonce[12] = pnBytes[0];

            // Decrypt Payload with AES-CTR
            int numBlocks = (pLen + 15) / 16;
            byte[] plaintext = new byte[pLen];
            byte[] ctri = new byte[16];
            byte[] si = new byte[16];
            ctri[0] = 0x01;
            Array.Copy(nonce, 0, ctri, 1, 13);

            for (int b = 0; b < numBlocks; b++)
            {
                int ctrVal = b + 1;
                ctri[14] = (byte)((ctrVal >> 8) & 0xFF);
                ctri[15] = (byte)(ctrVal & 0xFF);
                PureAes128.EncryptBlock(w, ctri, 0, si, 0);

                int bStart = b * 16;
                int bLen = pLen - bStart;
                if (bLen > 16) bLen = 16;
                for (int i = 0; i < bLen; i++)
                {
                    plaintext[bStart + i] = (byte)(frame[cipherStart + bStart + i] ^ si[i]);
                }
            }

            // Verify MIC with AES-CBC-MAC
            byte[] ctr0 = new byte[16];
            ctr0[0] = 0x01;
            Array.Copy(nonce, 0, ctr0, 1, 13);
            byte[] s0 = new byte[16];
            PureAes128.EncryptBlock(w, ctr0, 0, s0, 0);

            byte[] expectedTag = new byte[8];
            for (int i = 0; i < 8; i++)
            {
                expectedTag[i] = (byte)(frame[micStart + i] ^ s0[i]);
            }

            byte[] b0 = new byte[16];
            b0[0] = 0x59;
            Array.Copy(nonce, 0, b0, 1, 13);
            b0[14] = (byte)((pLen >> 8) & 0xFF);
            b0[15] = (byte)(pLen & 0xFF);

            int aadLen = isQos ? 24 : 22;
            byte[] aad = new byte[aadLen];
            aad[0] = (byte)(fc0 & 0xC7);
            aad[1] = (byte)(fc1 & 0x8F);
            Array.Copy(a1, 0, aad, 2, 6);
            Array.Copy(a2, 0, aad, 8, 6);
            Array.Copy(a3, 0, aad, 14, 6);
            aad[20] = (byte)(sc0 & 0x0F);
            aad[21] = 0x00;
            if (isQos)
            {
                aad[22] = tid;
                aad[23] = 0x00;
            }

            byte[] aadBlock = new byte[32];
            aadBlock[0] = 0x00;
            aadBlock[1] = (byte)aadLen;
            Array.Copy(aad, 0, aadBlock, 2, aadLen);

            byte[] x = new byte[16];
            byte[] tmpIn = new byte[16];
            PureAes128.EncryptBlock(w, b0, 0, x, 0);

            for (int i = 0; i < 16; i++) tmpIn[i] = (byte)(x[i] ^ aadBlock[i]);
            PureAes128.EncryptBlock(w, tmpIn, 0, x, 0);

            for (int i = 0; i < 16; i++) tmpIn[i] = (byte)(x[i] ^ aadBlock[16 + i]);
            PureAes128.EncryptBlock(w, tmpIn, 0, x, 0);

            for (int b = 0; b < numBlocks; b++)
            {
                int bStart = b * 16;
                int bLen = pLen - bStart;
                if (bLen > 16) bLen = 16;
                for (int i = 0; i < bLen; i++)
                {
                    byte pb = (i < bLen) ? plaintext[bStart + i] : (byte)0;
                    tmpIn[i] = (byte)(x[i] ^ pb);
                }
                PureAes128.EncryptBlock(w, tmpIn, 0, x, 0);
            }

            for (int i = 0; i < 8; i++)
            {
                if (x[i] != expectedTag[i])
                {
                    return null; // MIC Mismatch
                }
            }

            // Verify LLC/SNAP header (AA AA 03 00 00 00)
            if (plaintext[0] != 0xAA || plaintext[1] != 0xAA || plaintext[2] != 0x03 ||
                plaintext[3] != 0x00 || plaintext[4] != 0x00 || plaintext[5] != 0x00)
            {
                return null;
            }

            // Build decapsulated 802.3 Ethernet frame:
            // DA (6B), SA (6B), EtherType (2B), Payload
            bool fromDs = (fc1 & 0x02) != 0;
            byte[] da = fromDs ? a1 : a3;
            byte[] sa = fromDs ? a3 : a2;

            int ethPayloadLen = pLen - 8;
            byte[] ethFrame = new byte[14 + ethPayloadLen];
            Array.Copy(da, 0, ethFrame, 0, 6);
            Array.Copy(sa, 0, ethFrame, 6, 6);
            ethFrame[12] = plaintext[6]; // EtherType high byte
            ethFrame[13] = plaintext[7]; // EtherType low byte
            Array.Copy(plaintext, 8, ethFrame, 14, ethPayloadLen);

            return ethFrame;
        }
    }
}
