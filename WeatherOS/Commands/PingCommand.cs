using System;
using Cosmos.System.Network.IPv4;
using WeatherOS.Services;

namespace WeatherOS.Commands
{
    public class PingCommand : ICommand
    {
        public string Name => "ping";
        public string Description => "Test network connectivity (e.g. ping 192.168.31.1).";

        public void Execute(string[] args)
        {
            if (args.Length < 2)
            {
                System.Console.WriteLine("Usage: ping <ip_address> (Example: ping 192.168.31.1)");
                return;
            }

            try
            {
                var address = Address.Parse(args[1]);
                System.Console.WriteLine($"Pinging {args[1]} with 32 bytes of data (Bare-Metal Engine)...");
                
                int successCount = 0;
                for (int i = 0; i < 4; i++)
                {
                    int rtt = 0;
                    bool ok = RawTcpHttp.Ping(address, out rtt);
                    if (ok)
                    {
                        System.Console.WriteLine($"Reply from {args[1]}: bytes=32 time={rtt}ms TTL=64");
                        successCount++;
                    }
                    else
                    {
                        System.Console.WriteLine("Request timed out.");
                    }
                }
                System.Console.WriteLine($"Ping statistics: 4 sent, {successCount} received, {4 - successCount} lost.\n");
            }
            catch
            {
                System.Console.WriteLine("Invalid IP address or network hardware offline.\n");
            }
        }
    }
}
