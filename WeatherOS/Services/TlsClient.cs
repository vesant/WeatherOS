using System;
using System.Collections.Generic;
using System.Text;
using Cosmos.System.Network.IPv4;

namespace WeatherOS.Services
{
    // ==============================================================
    // 1. Pure C# SHA-256 & HMAC-SHA256 & TLS 1.2 PRF (Zero BCL dependencies)
    // ==============================================================
    public static class MiniSha256
    {
        private static readonly uint[] K = new uint[]
        {
            0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
            0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
            0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
            0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
            0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
            0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
            0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
            0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
        };

        private static uint RotateRight(uint x, int n) => (x >> n) | (x << (32 - n));
        private static uint Ch(uint x, uint y, uint z) => (x & y) ^ (~x & z);
        private static uint Maj(uint x, uint y, uint z) => (x & y) ^ (x & z) ^ (y & z);
        private static uint Sigma0(uint x) => RotateRight(x, 2) ^ RotateRight(x, 13) ^ RotateRight(x, 22);
        private static uint Sigma1(uint x) => RotateRight(x, 6) ^ RotateRight(x, 11) ^ RotateRight(x, 25);
        private static uint Gamma0(uint x) => RotateRight(x, 7) ^ RotateRight(x, 18) ^ (x >> 3);
        private static uint Gamma1(uint x) => RotateRight(x, 17) ^ RotateRight(x, 19) ^ (x >> 10);

        public static byte[] ComputeHash(byte[] message)
        {
            if (message == null) message = new byte[0];
            int msgLen = message.Length;
            int padLen = (msgLen % 64 < 56) ? (56 - (msgLen % 64)) : (120 - (msgLen % 64));
            byte[] padded = new byte[msgLen + padLen + 8];
            Array.Copy(message, 0, padded, 0, msgLen);
            padded[msgLen] = 0x80;

            ulong bitLen = (ulong)msgLen * 8;
            for (int i = 0; i < 8; i++)
            {
                padded[padded.Length - 1 - i] = (byte)(bitLen >> (i * 8));
            }

            uint h0 = 0x6a09e667, h1 = 0xbb67ae85, h2 = 0x3c6ef372, h3 = 0xa54ff53a;
            uint h4 = 0x510e527f, h5 = 0x9b05688c, h6 = 0x1f83d9ab, h7 = 0x5be0cd19;

            uint[] w = new uint[64];
            for (int chunk = 0; chunk < padded.Length; chunk += 64)
            {
                for (int i = 0; i < 16; i++)
                {
                    int o = chunk + (i * 4);
                    w[i] = ((uint)padded[o] << 24) | ((uint)padded[o + 1] << 16) | ((uint)padded[o + 2] << 8) | padded[o + 3];
                }
                for (int i = 16; i < 64; i++)
                {
                    w[i] = Gamma1(w[i - 2]) + w[i - 7] + Gamma0(w[i - 15]) + w[i - 16];
                }

                uint a = h0, b = h1, c = h2, d = h3, e = h4, f = h5, g = h6, h = h7;
                for (int i = 0; i < 64; i++)
                {
                    uint t1 = h + Sigma1(e) + Ch(e, f, g) + K[i] + w[i];
                    uint t2 = Sigma0(a) + Maj(a, b, c);
                    h = g; g = f; f = e; e = d + t1;
                    d = c; c = b; b = a; a = t1 + t2;
                }

                h0 += a; h1 += b; h2 += c; h3 += d; h4 += e; h5 += f; h6 += g; h7 += h;
            }

            byte[] hash = new byte[32];
            uint[] finalH = new uint[] { h0, h1, h2, h3, h4, h5, h6, h7 };
            for (int i = 0; i < 8; i++)
            {
                hash[i * 4] = (byte)(finalH[i] >> 24);
                hash[i * 4 + 1] = (byte)(finalH[i] >> 16);
                hash[i * 4 + 2] = (byte)(finalH[i] >> 8);
                hash[i * 4 + 3] = (byte)finalH[i];
            }
            return hash;
        }

        public static byte[] Hmac(byte[] key, byte[] data)
        {
            byte[] k = key;
            if (k.Length > 64) k = ComputeHash(k);
            if (k.Length < 64)
            {
                byte[] paddedK = new byte[64];
                Array.Copy(k, 0, paddedK, 0, k.Length);
                k = paddedK;
            }

            byte[] ipad = new byte[64 + data.Length];
            byte[] opad = new byte[64 + 32];
            for (int i = 0; i < 64; i++)
            {
                ipad[i] = (byte)(k[i] ^ 0x36);
                opad[i] = (byte)(k[i] ^ 0x5c);
            }
            Array.Copy(data, 0, ipad, 64, data.Length);
            byte[] innerHash = ComputeHash(ipad);
            Array.Copy(innerHash, 0, opad, 64, 32);
            return ComputeHash(opad);
        }

        public static byte[] Prf(byte[] secret, byte[] label, byte[] seed, int length)
        {
            byte[] labelSeed = new byte[label.Length + seed.Length];
            Array.Copy(label, 0, labelSeed, 0, label.Length);
            Array.Copy(seed, 0, labelSeed, label.Length, seed.Length);

            byte[] result = new byte[length];
            int generated = 0;
            byte[] a = labelSeed;

            while (generated < length)
            {
                a = Hmac(secret, a);
                byte[] hmacInput = new byte[a.Length + labelSeed.Length];
                Array.Copy(a, 0, hmacInput, 0, a.Length);
                Array.Copy(labelSeed, 0, hmacInput, a.Length, labelSeed.Length);
                byte[] block = Hmac(secret, hmacInput);

                int toCopy = Math.Min(block.Length, length - generated);
                Array.Copy(block, 0, result, generated, toCopy);
                generated += toCopy;
            }
            return result;
        }
    }

    // ==============================================================
    // 2. Pure C# AES-128 & CBC Mode
    // ==============================================================
    public static class MiniAes128
    {
        private static readonly byte[] SBox = new byte[256]
        {
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

        private static readonly byte[] InvSBox = new byte[256];
        private static readonly uint[] Rcon = new uint[] { 0x01, 0x02, 0x04, 0x08, 0x10, 0x20, 0x40, 0x80, 0x1B, 0x36 };

        static MiniAes128()
        {
            for (int i = 0; i < 256; i++) InvSBox[SBox[i]] = (byte)i;
        }

        private static byte Gmul(byte a, byte b)
        {
            byte p = 0;
            for (int counter = 0; counter < 8; counter++)
            {
                if ((b & 1) != 0) p ^= a;
                bool hi_bit_set = (a & 0x80) != 0;
                a <<= 1;
                if (hi_bit_set) a ^= 0x1B;
                b >>= 1;
            }
            return p;
        }

        private static uint[] ExpandKey(byte[] key)
        {
            uint[] w = new uint[44];
            for (int i = 0; i < 4; i++)
            {
                w[i] = ((uint)key[4 * i] << 24) | ((uint)key[4 * i + 1] << 16) | ((uint)key[4 * i + 2] << 8) | key[4 * i + 3];
            }
            for (int i = 4; i < 44; i++)
            {
                uint temp = w[i - 1];
                if (i % 4 == 0)
                {
                    temp = (temp << 8) | (temp >> 24);
                    temp = ((uint)SBox[(temp >> 24) & 0xFF] << 24) |
                           ((uint)SBox[(temp >> 16) & 0xFF] << 16) |
                           ((uint)SBox[(temp >> 8) & 0xFF] << 8) |
                           SBox[temp & 0xFF];
                    temp ^= (Rcon[(i / 4) - 1] << 24);
                }
                w[i] = w[i - 4] ^ temp;
            }
            return w;
        }

        public static void EncryptBlock(byte[] input, int inOff, byte[] output, int outOff, uint[] roundKeys)
        {
            byte[] state = new byte[16];
            for (int i = 0; i < 16; i++) state[i] = (byte)(input[inOff + i] ^ (roundKeys[i / 4] >> (24 - 8 * (i % 4))));

            for (int round = 1; round <= 10; round++)
            {
                for (int i = 0; i < 16; i++) state[i] = SBox[state[i]];

                byte t = state[1]; state[1] = state[5]; state[5] = state[9]; state[9] = state[13]; state[13] = t;
                t = state[2]; state[2] = state[10]; state[10] = t; t = state[6]; state[6] = state[14]; state[14] = t;
                t = state[15]; state[15] = state[11]; state[11] = state[7]; state[7] = state[3]; state[3] = t;

                if (round < 10)
                {
                    for (int c = 0; c < 4; c++)
                    {
                        int idx = c * 4;
                        byte a0 = state[idx], a1 = state[idx + 1], a2 = state[idx + 2], a3 = state[idx + 3];
                        state[idx] = (byte)(Gmul(2, a0) ^ Gmul(3, a1) ^ a2 ^ a3);
                        state[idx + 1] = (byte)(a0 ^ Gmul(2, a1) ^ Gmul(3, a2) ^ a3);
                        state[idx + 2] = (byte)(a0 ^ a1 ^ Gmul(2, a2) ^ Gmul(3, a3));
                        state[idx + 3] = (byte)(Gmul(3, a0) ^ a1 ^ a2 ^ Gmul(2, a3));
                    }
                }

                for (int i = 0; i < 16; i++)
                {
                    state[i] ^= (byte)(roundKeys[round * 4 + (i / 4)] >> (24 - 8 * (i % 4)));
                }
            }
            Array.Copy(state, 0, output, outOff, 16);
        }

        public static void DecryptBlock(byte[] input, int inOff, byte[] output, int outOff, uint[] roundKeys)
        {
            byte[] state = new byte[16];
            for (int i = 0; i < 16; i++)
            {
                state[i] = (byte)(input[inOff + i] ^ (roundKeys[40 + (i / 4)] >> (24 - 8 * (i % 4))));
            }

            for (int round = 9; round >= 0; round--)
            {
                byte t = state[13]; state[13] = state[9]; state[9] = state[5]; state[5] = state[1]; state[1] = t;
                t = state[2]; state[2] = state[10]; state[10] = t; t = state[6]; state[6] = state[14]; state[14] = t;
                t = state[3]; state[3] = state[7]; state[7] = state[11]; state[11] = state[15]; state[15] = t;

                for (int i = 0; i < 16; i++) state[i] = InvSBox[state[i]];

                for (int i = 0; i < 16; i++)
                {
                    state[i] ^= (byte)(roundKeys[round * 4 + (i / 4)] >> (24 - 8 * (i % 4)));
                }

                if (round > 0)
                {
                    for (int c = 0; c < 4; c++)
                    {
                        int idx = c * 4;
                        byte a0 = state[idx], a1 = state[idx + 1], a2 = state[idx + 2], a3 = state[idx + 3];
                        state[idx] = (byte)(Gmul(0x0e, a0) ^ Gmul(0x0b, a1) ^ Gmul(0x0d, a2) ^ Gmul(0x09, a3));
                        state[idx + 1] = (byte)(Gmul(0x09, a0) ^ Gmul(0x0e, a1) ^ Gmul(0x0b, a2) ^ Gmul(0x0d, a3));
                        state[idx + 2] = (byte)(Gmul(0x0d, a0) ^ Gmul(0x09, a1) ^ Gmul(0x0e, a2) ^ Gmul(0x0b, a3));
                        state[idx + 3] = (byte)(Gmul(0x0b, a0) ^ Gmul(0x0d, a1) ^ Gmul(0x09, a2) ^ Gmul(0x0e, a3));
                    }
                }
            }
            Array.Copy(state, 0, output, outOff, 16);
        }

        public static byte[] EncryptCbc(byte[] key, byte[] iv, byte[] data)
        {
            uint[] roundKeys = ExpandKey(key);
            byte[] result = new byte[data.Length];
            byte[] prev = (byte[])iv.Clone();
            byte[] block = new byte[16];

            for (int i = 0; i < data.Length; i += 16)
            {
                for (int j = 0; j < 16; j++) block[j] = (byte)(data[i + j] ^ prev[j]);
                EncryptBlock(block, 0, result, i, roundKeys);
                Array.Copy(result, i, prev, 0, 16);
            }
            return result;
        }

        public static byte[] DecryptCbc(byte[] key, byte[] iv, byte[] data)
        {
            uint[] roundKeys = ExpandKey(key);
            byte[] result = new byte[data.Length];
            byte[] prev = (byte[])iv.Clone();
            byte[] block = new byte[16];

            for (int i = 0; i < data.Length; i += 16)
            {
                DecryptBlock(data, i, block, 0, roundKeys);
                for (int j = 0; j < 16; j++) result[i + j] = (byte)(block[j] ^ prev[j]);
                Array.Copy(data, i, prev, 0, 16);
            }
            return result;
        }
    }

    // ==============================================================
    // 3. Pure C# 4096-bit Modular Exponentiation (M^65537 mod n)
    // ==============================================================
    public class MiniBigInt
    {
        public const int NUM_WORDS = 136;
        public uint[] d = new uint[NUM_WORDS];

        public MiniBigInt() { }

        public static MiniBigInt FromBigEndianBytes(byte[] bytes)
        {
            var res = new MiniBigInt();
            int len = bytes.Length;
            for (int i = 0; i < len; i++)
            {
                int byteIdx = len - 1 - i;
                int wordIdx = i / 4;
                int shift = (i % 4) * 8;
                if (wordIdx < NUM_WORDS)
                {
                    res.d[wordIdx] |= ((uint)bytes[byteIdx]) << shift;
                }
            }
            return res;
        }

        public byte[] ToBigEndianBytes(int targetLen)
        {
            byte[] res = new byte[targetLen];
            for (int i = 0; i < targetLen; i++)
            {
                int byteIdx = targetLen - 1 - i;
                int wordIdx = i / 4;
                int shift = (i % 4) * 8;
                if (wordIdx < NUM_WORDS)
                {
                    res[byteIdx] = (byte)(d[wordIdx] >> shift);
                }
            }
            return res;
        }

        public static int Compare(MiniBigInt a, MiniBigInt b)
        {
            for (int i = NUM_WORDS - 1; i >= 0; i--)
            {
                if (a.d[i] > b.d[i]) return 1;
                if (a.d[i] < b.d[i]) return -1;
            }
            return 0;
        }

        public static void SubtractInPlace(MiniBigInt a, MiniBigInt b)
        {
            long borrow = 0;
            for (int i = 0; i < NUM_WORDS; i++)
            {
                long diff = (long)a.d[i] - b.d[i] - borrow;
                if (diff < 0)
                {
                    diff += 0x100000000L;
                    borrow = 1;
                }
                else
                {
                    borrow = 0;
                }
                a.d[i] = (uint)diff;
            }
        }

        public static void ShiftLeftOneInPlace(MiniBigInt a)
        {
            uint carry = 0;
            for (int i = 0; i < NUM_WORDS; i++)
            {
                uint newCarry = a.d[i] >> 31;
                a.d[i] = (a.d[i] << 1) | carry;
                carry = newCarry;
            }
        }

        public static MiniBigInt MultiplyMod(MiniBigInt a, MiniBigInt b, MiniBigInt m)
        {
            var res = new MiniBigInt();
            var cur = new MiniBigInt();
            Array.Copy(a.d, cur.d, NUM_WORDS);

            int topWord = NUM_WORDS - 1;
            while (topWord > 0 && b.d[topWord] == 0) topWord--;
            int totalBits = (topWord + 1) * 32;

            for (int bit = 0; bit < totalBits; bit++)
            {
                int wordIdx = bit / 32;
                int bitIdx = bit % 32;

                if (((b.d[wordIdx] >> bitIdx) & 1) != 0)
                {
                    ulong carry = 0;
                    for (int i = 0; i < NUM_WORDS; i++)
                    {
                        ulong sum = (ulong)res.d[i] + cur.d[i] + carry;
                        res.d[i] = (uint)sum;
                        carry = sum >> 32;
                    }
                    if (Compare(res, m) >= 0) SubtractInPlace(res, m);
                }

                ShiftLeftOneInPlace(cur);
                if (Compare(cur, m) >= 0) SubtractInPlace(cur, m);
            }
            return res;
        }

        public static MiniBigInt ModPow65537(MiniBigInt m, MiniBigInt modulus)
        {
            MiniBigInt x = new MiniBigInt();
            Array.Copy(m.d, x.d, NUM_WORDS);

            for (int i = 0; i < 16; i++)
            {
                x = MultiplyMod(x, x, modulus);
            }
            return MultiplyMod(x, m, modulus);
        }
    }

    // ==============================================================
    // 4. Pure C# Bare-Metal TLS 1.2 Client Engine
    // ==============================================================
    public static class TlsClient
    {
        public static string FetchHttps(Address targetIp, string host, string path, int timeoutMs = 5000)
        {
            try
            {
                // 1. TCP Connection to port 443
                bool connected = RawTcpHttp.StartTcpSession(targetIp, 443, timeoutMs);
                if (!connected) return null;

                // 2. Build ClientHello offering TLS_RSA_WITH_AES_128_CBC_SHA256 (0x003C)
                byte[] clientRandom = new byte[32];
                var rnd = new Random();
                rnd.NextBytes(clientRandom);

                byte[] sniExt = BuildSniExtension(host);
                byte[] sigExt = new byte[] { 0x00, 0x0D, 0x00, 0x0A, 0x00, 0x08, 0x04, 0x01, 0x05, 0x01, 0x06, 0x01, 0x02, 0x01 };
                byte[] extensions = Combine(sniExt, sigExt);

                byte[] ciphers = new byte[] { 0x00, 0x3C };
                byte[] chBody = Combine(
                    new byte[] { 0x03, 0x03 },
                    clientRandom,
                    new byte[] { 0x00 },
                    new byte[] { 0x00, 0x02 }, ciphers,
                    new byte[] { 0x01, 0x00 },
                    new byte[] { (byte)(extensions.Length >> 8), (byte)extensions.Length }, extensions
                );

                byte[] chMsg = Combine(
                    new byte[] { 0x01, 0x00, (byte)(chBody.Length >> 8), (byte)chBody.Length },
                    chBody
                );

                byte[] chRecord = Combine(
                    new byte[] { 0x16, 0x03, 0x03, (byte)(chMsg.Length >> 8), (byte)chMsg.Length },
                    chMsg
                );

                RawTcpHttp.SendTcpData(chRecord);

                // 3. Read ServerHello, Certificate, ServerHelloDone
                var handshakeMsgs = new List<byte>(chMsg);
                byte[] serverRandom = null;
                byte[] certDer = null;

                byte[] rxBuf = new byte[8192];
                int rxTotal = 0;
                bool gotHelloDone = false;
                int waitElapsed = 0;

                while (!gotHelloDone && waitElapsed < timeoutMs)
                {
                    int read = RawTcpHttp.ReadTcpData(rxBuf, rxTotal, rxBuf.Length - rxTotal, 150);
                    if (read > 0)
                    {
                        rxTotal += read;

                        int idx = 0;
                        while (idx + 5 <= rxTotal)
                        {
                            byte rType = rxBuf[idx];
                            int rLen = (rxBuf[idx + 3] << 8) | rxBuf[idx + 4];
                            if (idx + 5 + rLen > rxTotal) break;

                            if (rType == 0x16) // Handshake
                            {
                                int hIdx = idx + 5;
                                int hEnd = hIdx + rLen;
                                while (hIdx + 4 <= hEnd)
                                {
                                    byte hType = rxBuf[hIdx];
                                    int hLen = (rxBuf[hIdx + 1] << 16) | (rxBuf[hIdx + 2] << 8) | rxBuf[hIdx + 3];
                                    byte[] hData = new byte[4 + hLen];
                                    Array.Copy(rxBuf, hIdx, hData, 0, 4 + hLen);
                                    handshakeMsgs.AddRange(hData);

                                    if (hType == 2) // ServerHello
                                    {
                                        serverRandom = new byte[32];
                                        Array.Copy(rxBuf, hIdx + 6, serverRandom, 0, 32);
                                    }
                                    else if (hType == 11) // Certificate
                                    {
                                        int firstCertLen = (rxBuf[hIdx + 7] << 16) | (rxBuf[hIdx + 8] << 8) | rxBuf[hIdx + 9];
                                        certDer = new byte[firstCertLen];
                                        Array.Copy(rxBuf, hIdx + 10, certDer, 0, firstCertLen);
                                    }
                                    else if (hType == 14) // ServerHelloDone
                                    {
                                        gotHelloDone = true;
                                    }
                                    hIdx += 4 + hLen;
                                }
                            }
                            idx += 5 + rLen;
                        }
                    }
                    else
                    {
                        waitElapsed += 150;
                    }
                }

                if (!gotHelloDone || certDer == null || serverRandom == null)
                {
                    return null;
                }

                // 4. Extract RSA 4096-bit Public Key
                int pkIdx = 0;
                for (int i = 0; i < certDer.Length - 10; i++)
                {
                    if (certDer[i] == 0x03 && certDer[i + 1] == 0x82 && certDer[i + 4] == 0x00 && certDer[i + 5] == 0x30)
                    {
                        pkIdx = i + 5;
                        break;
                    }
                }

                int modIdx = pkIdx + 4;
                int modLen = (certDer[modIdx + 2] << 8) | certDer[modIdx + 3];
                int modStart = modIdx + 4;
                byte[] modBytes = new byte[modLen];
                Array.Copy(certDer, modStart, modBytes, 0, modLen);
                if (modBytes[0] == 0)
                {
                    byte[] trimmed = new byte[modLen - 1];
                    Array.Copy(modBytes, 1, trimmed, 0, trimmed.Length);
                    modBytes = trimmed;
                }

                // 5. Generate PreMasterSecret & RSA Encrypt
                byte[] preMaster = new byte[48];
                preMaster[0] = 0x03; preMaster[1] = 0x03;
                rnd.NextBytes(preMaster);
                preMaster[0] = 0x03; preMaster[1] = 0x03;

                byte[] padded = new byte[modBytes.Length];
                padded[0] = 0x00; padded[1] = 0x02;
                int padLen = modBytes.Length - 3 - 48;
                for (int i = 0; i < padLen; i++)
                {
                    byte b;
                    do { b = (byte)rnd.Next(1, 256); } while (b == 0);
                    padded[2 + i] = b;
                }
                padded[2 + padLen] = 0x00;
                Array.Copy(preMaster, 0, padded, 3 + padLen, 48);

                var mBi = MiniBigInt.FromBigEndianBytes(padded);
                var nBi = MiniBigInt.FromBigEndianBytes(modBytes);
                var cBi = MiniBigInt.ModPow65537(mBi, nBi);
                byte[] encPreMaster = cBi.ToBigEndianBytes(modBytes.Length);

                // 6. Send ClientKeyExchange
                byte[] ckeBody = Combine(
                    new byte[] { (byte)(encPreMaster.Length >> 8), (byte)encPreMaster.Length },
                    encPreMaster
                );
                byte[] ckeMsg = Combine(
                    new byte[] { 0x10, 0x00, (byte)(ckeBody.Length >> 8), (byte)ckeBody.Length },
                    ckeBody
                );
                byte[] ckeRecord = Combine(
                    new byte[] { 0x16, 0x03, 0x03, (byte)(ckeMsg.Length >> 8), (byte)ckeMsg.Length },
                    ckeMsg
                );

                RawTcpHttp.SendTcpData(ckeRecord);
                handshakeMsgs.AddRange(ckeMsg);

                // 7. Derive MasterSecret & KeyBlock
                byte[] randSeed = Combine(clientRandom, serverRandom);
                byte[] masterSecret = MiniSha256.Prf(preMaster, Encoding.ASCII.GetBytes("master secret"), randSeed, 48);

                byte[] keyExpansionSeed = Combine(serverRandom, clientRandom);
                byte[] keyBlock = MiniSha256.Prf(masterSecret, Encoding.ASCII.GetBytes("key expansion"), keyExpansionSeed, 96);

                byte[] clientMacKey = new byte[32];
                byte[] serverMacKey = new byte[32];
                byte[] clientWriteKey = new byte[16];
                byte[] serverWriteKey = new byte[16];

                Array.Copy(keyBlock, 0, clientMacKey, 0, 32);
                Array.Copy(keyBlock, 32, serverMacKey, 0, 32);
                Array.Copy(keyBlock, 64, clientWriteKey, 0, 16);
                Array.Copy(keyBlock, 80, serverWriteKey, 0, 16);

                // 8. Send ChangeCipherSpec
                byte[] ccsRecord = new byte[] { 0x14, 0x03, 0x03, 0x00, 0x01, 0x01 };
                RawTcpHttp.SendTcpData(ccsRecord);

                // 9. Send Encrypted Finished
                byte[] handshakeHash = MiniSha256.ComputeHash(handshakeMsgs.ToArray());
                byte[] verifyData = MiniSha256.Prf(masterSecret, Encoding.ASCII.GetBytes("client finished"), handshakeHash, 12);
                byte[] finMsg = Combine(new byte[] { 0x14, 0x00, 0x00, 0x0C }, verifyData);

                ulong clientSeq = 0;
                byte[] finRecord = BuildEncryptedRecord(0x16, finMsg, clientSeq++, clientWriteKey, clientMacKey, rnd);
                RawTcpHttp.SendTcpData(finRecord);

                // 10. Read Server ChangeCipherSpec and Server Finished
                byte[] srvResp = new byte[1024];
                int srvRead = RawTcpHttp.ReadTcpData(srvResp, 0, srvResp.Length, 2000);
                if (srvRead <= 0 || srvResp[0] != 0x14)
                {
                    return null;
                }

                // 11. Send Encrypted HTTP GET Request
                string httpReq = $"GET {path} HTTP/1.1\r\nHost: {host}\r\nUser-Agent: WeatherOS-Cosmos/3.0\r\nAccept: application/vnd.github.v3+json\r\nConnection: close\r\n\r\n";
                byte[] reqBytes = Encoding.ASCII.GetBytes(httpReq);
                byte[] appRecord = BuildEncryptedRecord(0x17, reqBytes, clientSeq++, clientWriteKey, clientMacKey, rnd);
                RawTcpHttp.SendTcpData(appRecord);

                // 12. Read and Decrypt Encrypted Application Data
                byte[] encResp = new byte[65536];
                int encTotal = 0;
                int appWait = 0;

                while (appWait < 2000 && encTotal < encResp.Length - 1024)
                {
                    int r = RawTcpHttp.ReadTcpData(encResp, encTotal, encResp.Length - encTotal, 200);
                    if (r > 0)
                    {
                        encTotal += r;
                        appWait = 0;
                        if (encTotal > 15000) break; // Plenty of commits received
                    }
                    else
                    {
                        if (encTotal > 0) appWait += 400;
                        else appWait += 200;
                    }
                }

                if (encTotal > 5)
                {
                    var fullPlain = new StringBuilder();
                    int recOffset = 0;

                    while (recOffset + 5 < encTotal)
                    {
                        byte recType = encResp[recOffset];
                        if (recType != 0x17) break;

                        int appLen = (encResp[recOffset + 3] << 8) | encResp[recOffset + 4];
                        if (recOffset + 5 + appLen > encTotal)
                        {
                            appLen = encTotal - (recOffset + 5);
                        }

                        if (appLen > 16)
                        {
                            byte[] iv = new byte[16];
                            Array.Copy(encResp, recOffset + 5, iv, 0, 16);
                            byte[] cipherText = new byte[appLen - 16];
                            Array.Copy(encResp, recOffset + 21, cipherText, 0, cipherText.Length);

                            byte[] decrypted = MiniAes128.DecryptCbc(serverWriteKey, iv, cipherText);
                            if (decrypted != null && decrypted.Length > 0)
                            {
                                int padVal = decrypted[decrypted.Length - 1];
                                int plainLen = decrypted.Length;
                                if (padVal > 0 && padVal <= 16 && padVal < decrypted.Length)
                                {
                                    plainLen -= (padVal + 1);
                                }
                                if (plainLen > 32)
                                {
                                    plainLen -= 32;
                                }
                                if (plainLen > 0)
                                {
                                    fullPlain.Append(Encoding.ASCII.GetString(decrypted, 0, plainLen));
                                }
                            }
                        }

                        recOffset += 5 + appLen;
                    }

                    string plainHttp = fullPlain.ToString();
                    if (!string.IsNullOrEmpty(plainHttp))
                    {
                        int bodyIdx = plainHttp.IndexOf("\r\n\r\n");
                        if (bodyIdx >= 0)
                        {
                            return plainHttp.Substring(bodyIdx + 4);
                        }
                        return plainHttp;
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
            finally
            {
                RawTcpHttp.EndTcpSession();
            }
        }

        private static byte[] BuildEncryptedRecord(byte recordType, byte[] payload, ulong seqNum, byte[] encKey, byte[] macKey, Random rnd)
        {
            byte[] seqBytes = new byte[8];
            for (int i = 0; i < 8; i++) seqBytes[7 - i] = (byte)(seqNum >> (i * 8));

            byte[] macIn = Combine(
                seqBytes,
                new byte[] { recordType, 0x03, 0x03, (byte)(payload.Length >> 8), (byte)payload.Length },
                payload
            );
            byte[] mac = MiniSha256.Hmac(macKey, macIn);

            byte[] plain = Combine(payload, mac);
            int padLen = 16 - ((plain.Length + 1) % 16);
            if (padLen == 16) padLen = 0;
            byte[] padding = new byte[padLen + 1];
            for (int i = 0; i < padding.Length; i++) padding[i] = (byte)padLen;

            byte[] toEncrypt = Combine(plain, padding);
            byte[] iv = new byte[16];
            rnd.NextBytes(iv);

            byte[] cipherText = MiniAes128.EncryptCbc(encKey, iv, toEncrypt);
            byte[] recBody = Combine(iv, cipherText);

            return Combine(
                new byte[] { recordType, 0x03, 0x03, (byte)(recBody.Length >> 8), (byte)recBody.Length },
                recBody
            );
        }

        private static byte[] BuildSniExtension(string host)
        {
            byte[] hBytes = Encoding.ASCII.GetBytes(host);
            byte[] sniData = Combine(new byte[] { 0x00, (byte)(hBytes.Length >> 8), (byte)hBytes.Length }, hBytes);
            byte[] listData = Combine(new byte[] { (byte)(sniData.Length >> 8), (byte)sniData.Length }, sniData);
            return Combine(new byte[] { 0x00, 0x00, (byte)(listData.Length >> 8), (byte)listData.Length }, listData);
        }

        private static byte[] Combine(params byte[][] arrays)
        {
            int total = 0;
            foreach (var a in arrays) total += a.Length;
            byte[] res = new byte[total];
            int off = 0;
            foreach (var a in arrays)
            {
                Array.Copy(a, 0, res, off, a.Length);
                off += a.Length;
            }
            return res;
        }
    }
}
