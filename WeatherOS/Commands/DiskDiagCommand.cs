using System;
using WeatherOS.Services;

namespace WeatherOS.Commands
{
    public class DiskDiagCommand : ICommand
    {
        public string Name => "diskdiag";
        public string Description => "Hardware disk diagnostics & analyzer (probes PCI Class 0x01, AHCI ABAR, SATA ports, and IDE)";

        public void Execute(string[] args)
        {
            DiskDiagnosticsService.PrintCliReport();
        }
    }
}
