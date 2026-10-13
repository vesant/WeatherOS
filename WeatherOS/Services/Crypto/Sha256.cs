using System;

namespace WeatherOS.Services.Crypto
{
    /// <summary>
    /// Pure C# implementation of the SHA-256 cryptographic hash algorithm (FIPS 180-4).
    /// Designed specifically for Cosmos OS bare-metal x86 environments without standard crypto libraries.
    /// </summary>
    public static class Sha256
    {
        /// <summary>
        /// Initial hash value H0 (fractional part of square root of 2).
        /// </summary>
        public const uint H0 = 0x6a09e667;

        /// <summary>
        /// Initial hash value H1 (fractional part of square root of 3).
        /// </summary>
        public const uint H1 = 0xbb67ae85;

        /// <summary>
        /// Initial hash value H2 (fractional part of square root of 5).
        /// </summary>
        public const uint H2 = 0x3c6ef372;

        /// <summary>
        /// Initial hash value H3 (fractional part of square root of 7).
        /// </summary>
        public const uint H3 = 0xa54ff53a;

        /// <summary>
        /// Initial hash value H4 (fractional part of square root of 11).
        /// </summary>
        public const uint H4 = 0x510e527f;

        /// <summary>
        /// Initial hash value H5 (fractional part of square root of 13).
        /// </summary>
        public const uint H5 = 0x9b05688c;

        /// <summary>
        /// Initial hash value H6 (fractional part of square root of 17).
        /// </summary>
        public const uint H6 = 0x1f83d9ab;

        /// <summary>
        /// Initial hash value H7 (fractional part of square root of 19).
        /// </summary>
        public const uint H7 = 0x5be0cd19;

        /// <summary>
        /// Default 8 initial 32-bit hash values (H^(0)).
        /// </summary>
        public static readonly uint[] InitialH = new uint[8]
        {
            H0, H1, H2, H3, H4, H5, H6, H7
        };

        /// <summary>
        /// The 64 32-bit round constants for SHA-256 (first 32 bits of fractional parts of cube roots of first 64 primes).
        /// </summary>
        public static readonly uint[] K = new uint[64]
        {
            0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5,
            0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
            0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3,
            0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
            0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc,
            0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
            0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7,
            0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
            0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13,
            0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
            0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3,
            0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
            0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5,
            0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
            0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208,
            0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
        };

        /// <summary>
        /// Performs circular right rotation on a 32-bit unsigned integer.
        /// </summary>
        /// <param name="x">The value to rotate.</param>
        /// <param name="n">The number of bits to rotate right (1 to 31).</param>
        /// <returns>The rotated 32-bit value.</returns>
        public static uint RotateRight(uint x, int n)
        {
            return (x >> n) | (x << (32 - n));
        }

        /// <summary>
        /// Choose function: Ch(x, y, z) = (x AND y) XOR (NOT x AND z).
        /// </summary>
        public static uint Ch(uint x, uint y, uint z)
        {
            return (x & y) ^ (~x & z);
        }

        /// <summary>
        /// Majority function: Maj(x, y, z) = (x AND y) XOR (x AND z) XOR (y AND z).
        /// </summary>
        public static uint Maj(uint x, uint y, uint z)
        {
            return (x & y) ^ (x & z) ^ (y & z);
        }

        /// <summary>
        /// Upper-case Sigma0 round function: ROTR^2(x) XOR ROTR^13(x) XOR ROTR^22(x).
        /// </summary>
        public static uint Sigma0(uint x)
        {
            return RotateRight(x, 2) ^ RotateRight(x, 13) ^ RotateRight(x, 22);
        }

        /// <summary>
        /// Upper-case Sigma1 round function: ROTR^6(x) XOR ROTR^11(x) XOR ROTR^25(x).
        /// </summary>
        public static uint Sigma1(uint x)
        {
            return RotateRight(x, 6) ^ RotateRight(x, 11) ^ RotateRight(x, 25);
        }

        /// <summary>
        /// Lower-case sigma0 message schedule function: ROTR^7(x) XOR ROTR^18(x) XOR (x >> 3).
        /// </summary>
        public static uint sigma0(uint x)
        {
            return RotateRight(x, 7) ^ RotateRight(x, 18) ^ (x >> 3);
        }

        /// <summary>
        /// Lower-case sigma1 message schedule function: ROTR^17(x) XOR ROTR^19(x) XOR (x >> 10).
        /// </summary>
        public static uint sigma1(uint x)
        {
            return RotateRight(x, 17) ^ RotateRight(x, 19) ^ (x >> 10);
        }

        /// <summary>
        /// Processes a single 64-byte block, updating the 8-word internal working state in place.
        /// </summary>
        /// <param name="state">The 8-element uint state array.</param>
        /// <param name="w">The 64-element uint message schedule buffer to avoid per-block allocations.</param>
        /// <param name="block">The byte array containing the 64-byte block.</param>
        /// <param name="offset">The starting offset of the block.</param>
        public static void ProcessBlock(uint[] state, uint[] w, byte[] block, int offset)
        {
            // Step 1: Decode 16 big-endian 32-bit words into W[0..15]
            for (int t = 0; t < 16; t++)
            {
                int idx = offset + (t * 4);
                w[t] = ((uint)block[idx] << 24) |
                       ((uint)block[idx + 1] << 16) |
                       ((uint)block[idx + 2] << 8) |
                       ((uint)block[idx + 3]);
            }

            unchecked
            {
                // Step 2: Extend W[16..63]
                for (int t = 16; t < 64; t++)
                {
                    w[t] = sigma1(w[t - 2]) + w[t - 7] + sigma0(w[t - 15]) + w[t - 16];
                }

                // Step 3: Initialize working variables with current hash values
                uint a = state[0];
                uint b = state[1];
                uint c = state[2];
                uint d = state[3];
                uint e = state[4];
                uint f = state[5];
                uint g = state[6];
                uint h = state[7];

                // Step 4: Perform 64 compression rounds
                for (int t = 0; t < 64; t++)
                {
                    uint t1 = h + Sigma1(e) + Ch(e, f, g) + K[t] + w[t];
                    uint t2 = Sigma0(a) + Maj(a, b, c);
                    h = g;
                    g = f;
                    f = e;
                    e = d + t1;
                    d = c;
                    c = b;
                    b = a;
                    a = t1 + t2;
                }

                // Step 5: Compute intermediate hash values
                state[0] += a;
                state[1] += b;
                state[2] += c;
                state[3] += d;
                state[4] += e;
                state[5] += f;
                state[6] += g;
                state[7] += h;
            }
        }

        /// <summary>
        /// Computes the 32-byte SHA-256 hash for the given byte array.
        /// </summary>
        /// <param name="data">The input byte array.</param>
        /// <returns>A 32-byte array containing the SHA-256 hash.</returns>
        public static byte[] ComputeHash(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            return ComputeHash(data, 0, data.Length);
        }

        /// <summary>
        /// Computes the 32-byte SHA-256 hash for a segment of the given byte array.
        /// </summary>
        /// <param name="data">The input byte array.</param>
        /// <param name="offset">Starting index in data.</param>
        /// <param name="length">Number of bytes to hash.</param>
        /// <returns>A 32-byte array containing the SHA-256 hash.</returns>
        public static byte[] ComputeHash(byte[] data, int offset, int length)
        {
            Sha256Context ctx = new Sha256Context();
            ctx.Update(data, offset, length);
            return ctx.Finalize();
        }

        /// <summary>
        /// Formats a byte array as a lowercase hexadecimal string.
        /// </summary>
        /// <param name="bytes">Byte array to convert.</param>
        /// <returns>Hexadecimal string representation.</returns>
        public static string ToHexString(byte[] bytes)
        {
            if (bytes == null)
            {
                return string.Empty;
            }

            char[] hex = new char[bytes.Length * 2];
            const string hexChars = "0123456789abcdef";
            for (int i = 0; i < bytes.Length; i++)
            {
                hex[i * 2] = hexChars[(bytes[i] >> 4) & 0x0F];
                hex[i * 2 + 1] = hexChars[bytes[i] & 0x0F];
            }

            return new string(hex);
        }
    }

    /// <summary>
    /// Context for incremental SHA-256 hashing.
    /// Supports streaming/chunked updates without loading entire datasets into memory.
    /// </summary>
    public class Sha256Context
    {
        private readonly uint[] _state = new uint[8];
        private readonly uint[] _w = new uint[64];
        private readonly byte[] _buffer = new byte[64];
        private int _bufferLength;
        private ulong _totalBytes;
        private bool _isFinalized;

        /// <summary>
        /// Initializes a new instance of <see cref="Sha256Context"/>.
        /// </summary>
        public Sha256Context()
        {
            Reset();
        }

        /// <summary>
        /// Resets the context to its initial state for computing a new hash.
        /// </summary>
        public void Reset()
        {
            _state[0] = Sha256.H0;
            _state[1] = Sha256.H1;
            _state[2] = Sha256.H2;
            _state[3] = Sha256.H3;
            _state[4] = Sha256.H4;
            _state[5] = Sha256.H5;
            _state[6] = Sha256.H6;
            _state[7] = Sha256.H7;

            _bufferLength = 0;
            _totalBytes = 0;
            _isFinalized = false;
            Array.Clear(_buffer, 0, _buffer.Length);
            Array.Clear(_w, 0, _w.Length);
        }

        /// <summary>
        /// Updates the hash context with all bytes from the provided array.
        /// </summary>
        /// <param name="data">The byte array to append.</param>
        public void Update(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            Update(data, 0, data.Length);
        }

        /// <summary>
        /// Updates the hash context with a slice of the provided byte array.
        /// </summary>
        /// <param name="data">The source byte array.</param>
        /// <param name="offset">Starting offset in data.</param>
        /// <param name="length">Number of bytes to process.</param>
        public void Update(byte[] data, int offset, int length)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), "Offset cannot be negative.");
            }

            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "Length cannot be negative.");
            }

            if (offset + length > data.Length)
            {
                throw new ArgumentException("Offset and length exceed buffer bounds.");
            }

            if (_isFinalized)
            {
                throw new InvalidOperationException("Cannot update a finalized hash context. Call Reset() first.");
            }

            if (length == 0)
            {
                return;
            }

            _totalBytes += (ulong)length;

            int currentOffset = offset;
            int remaining = length;

            // Fill partial buffer if any bytes were left from previous updates
            if (_bufferLength > 0)
            {
                int toCopy = 64 - _bufferLength;
                if (remaining < toCopy)
                {
                    Array.Copy(data, currentOffset, _buffer, _bufferLength, remaining);
                    _bufferLength += remaining;
                    return;
                }

                Array.Copy(data, currentOffset, _buffer, _bufferLength, toCopy);
                Sha256.ProcessBlock(_state, _w, _buffer, 0);
                _bufferLength = 0;
                currentOffset += toCopy;
                remaining -= toCopy;
            }

            // Process full 64-byte blocks directly from the input array
            while (remaining >= 64)
            {
                Sha256.ProcessBlock(_state, _w, data, currentOffset);
                currentOffset += 64;
                remaining -= 64;
            }

            // Buffer remaining bytes for the next update or finalization
            if (remaining > 0)
            {
                Array.Copy(data, currentOffset, _buffer, 0, remaining);
                _bufferLength = remaining;
            }
        }

        /// <summary>
        /// Finalizes the hash calculation, applies standard padding, and returns the 32-byte digest.
        /// Once finalized, call <see cref="Reset"/> before computing another hash with this instance.
        /// </summary>
        /// <returns>The computed 32-byte SHA-256 hash.</returns>
        public byte[] Finalize()
        {
            if (_isFinalized)
            {
                throw new InvalidOperationException("Context already finalized. Call Reset() before reusing.");
            }

            ulong totalBits = _totalBytes * 8UL;

            // Step 1: Append single 1-bit (0x80 byte)
            _buffer[_bufferLength++] = 0x80;

            // Step 2: If remaining space in the block is less than 8 bytes for length, pad and process block
            if (_bufferLength > 56)
            {
                while (_bufferLength < 64)
                {
                    _buffer[_bufferLength++] = 0;
                }

                Sha256.ProcessBlock(_state, _w, _buffer, 0);
                _bufferLength = 0;
            }

            // Step 3: Pad with zeros up to 56 bytes
            while (_bufferLength < 56)
            {
                _buffer[_bufferLength++] = 0;
            }

            // Step 4: Append 64-bit integer bit length in big-endian order
            _buffer[56] = (byte)(totalBits >> 56);
            _buffer[57] = (byte)(totalBits >> 48);
            _buffer[58] = (byte)(totalBits >> 40);
            _buffer[59] = (byte)(totalBits >> 32);
            _buffer[60] = (byte)(totalBits >> 24);
            _buffer[61] = (byte)(totalBits >> 16);
            _buffer[62] = (byte)(totalBits >> 8);
            _buffer[63] = (byte)(totalBits);

            Sha256.ProcessBlock(_state, _w, _buffer, 0);

            // Step 5: Produce 32-byte hash from working state (big-endian)
            byte[] hash = new byte[32];
            for (int i = 0; i < 8; i++)
            {
                uint val = _state[i];
                hash[i * 4] = (byte)(val >> 24);
                hash[i * 4 + 1] = (byte)(val >> 16);
                hash[i * 4 + 2] = (byte)(val >> 8);
                hash[i * 4 + 3] = (byte)(val);
            }

            _isFinalized = true;
            return hash;
        }
    }
}
