using System;

class Program
{
    static int CalculateChecksum(byte[] buffer, int offset, int length)
    {
        long sum = 0;
        int i = 0;
        while (length > 1) { sum += (uint)((buffer[offset + i] << 8) | buffer[offset + i + 1]); i += 2; length -= 2; }
        if (length > 0) sum += (uint)(buffer[offset + i] << 8);
        while ((sum >> 16) != 0) sum = (sum & 0xFFFF) + (sum >> 16);
        return (ushort)(~sum);
    }

    static void Main()
    {
        byte[] ipSrc = { 10, 0, 2, 15 };
        byte[] ipDst1 = { 141, 95, 99, 79 }; // OpenWeather
        byte[] ipDst2 = { 79, 127, 134, 228 }; // WeatherAPI
        
        ushort srcPort = 54321;
        
        TestPacket(ipSrc, ipDst1, srcPort);
        TestPacket(ipSrc, ipDst2, srcPort);
    }

    static void TestPacket(byte[] src, byte[] dst, ushort srcPort)
    {
        int tcpLen = 20; // Basic SYN
        byte[] pseudo = new byte[12 + tcpLen];
        Array.Copy(src, 0, pseudo, 0, 4);
        Array.Copy(dst, 0, pseudo, 4, 4);
        pseudo[8] = 0; pseudo[9] = 0x06;
        pseudo[10] = (byte)(tcpLen >> 8); pseudo[11] = (byte)(tcpLen & 0xFF);
        
        byte[] tcp = new byte[tcpLen];
        tcp[0] = (byte)(srcPort >> 8); tcp[1] = (byte)(srcPort & 0xFF);
        tcp[2] = 0x00; tcp[3] = 0x50; // port 80
        // seq = 1000
        tcp[4] = 0; tcp[5] = 0; tcp[6] = 0x03; tcp[7] = 0xE8;
        // ack = 0
        // data offset = 5, flags = 0x02 (SYN)
        tcp[12] = 0x50; tcp[13] = 0x02;
        // window = 64240
        tcp[14] = 0xFA; tcp[15] = 0xF0;
        // checksum = 0, urg ptr = 0
        
        Array.Copy(tcp, 0, pseudo, 12, tcpLen);
        
        int chk = CalculateChecksum(pseudo, 0, pseudo.Length);
        Console.WriteLine($"Dst: {dst[0]}.{dst[1]}.{dst[2]}.{dst[3]} -> Checksum: {chk:X4}");
    }
}
