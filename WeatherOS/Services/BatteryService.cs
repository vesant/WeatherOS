using System;
using Cosmos.Core;
using Cosmos.HAL;

namespace WeatherOS.Services
{
    public struct BatteryInfo
    {
        public bool HasBattery;
        public bool IsLaptop;
        public bool IsAcConnected;
        public int Percentage;
        public string Status;
        public string Chemistry;
        public string ChassisType;
        public string Voltage;
    }

    public static class BatteryService
    {
        private static bool _initialized = false;
        private static BatteryInfo _cachedInfo;
        private static int _tickCounter = 0;
        private static ushort _smbusBase = 0;
        private static bool _smbusProbed = false;
        private static bool _manualOverrideActive = false;
        private static string _busSummary = "SMBus / ACPI EC probing...";

        public static BatteryInfo GetBatteryInfo()
        {
            if (!_initialized)
            {
                ProbeHardware();
                _initialized = true;
            }

            _tickCounter++;

            // Perform automatic live hardware sensing periodically if no manual override
            if (!_manualOverrideActive && (_tickCounter % 5 == 0))
            {
                DetectHardwarePowerState();
            }

            // Sync status text and clamps
            if (_cachedInfo.Percentage <= 0) _cachedInfo.Percentage = 75;
            if (_cachedInfo.Percentage > 100) _cachedInfo.Percentage = 100;

            if (_cachedInfo.IsAcConnected)
            {
                _cachedInfo.Status = "AC Plugged (Charging)";
            }
            else
            {
                _cachedInfo.Status = "On Battery (Discharging)";
            }

            return _cachedInfo;
        }

        public static void ToggleAcOverride()
        {
            if (!_initialized)
            {
                ProbeHardware();
                _initialized = true;
            }

            _manualOverrideActive = true;
            _cachedInfo.IsAcConnected = !_cachedInfo.IsAcConnected;
            if (_cachedInfo.IsAcConnected)
            {
                _cachedInfo.Status = "AC Plugged (Charging)";
                _busSummary = "Manual Override: AC Connected";
            }
            else
            {
                _cachedInfo.Status = "On Battery (Discharging)";
                _busSummary = "Manual Override: Discharging on Battery";
            }
        }

        public static void ForceHardwareProbe()
        {
            _manualOverrideActive = false;
            _smbusProbed = false;
            ProbeHardware();
            DetectHardwarePowerState();
        }

        public static string GetHardwareBusSummary()
        {
            return _busSummary;
        }

        private static void DetectHardwarePowerState()
        {
            bool stateDetermined = false;

            // 1. Authoritative: Intel / AMD SMBus Smart Battery System (SBS v1.1)
            try
            {
                InitSmbus();
                if (_smbusBase > 0)
                {
                    // Query Smart Battery at slave address 0x0B
                    // Command 0x0A: Current (mA, signed)
                    if (TrySmbusReadWord(0x0B, 0x0A, out ushort rawCurrent))
                    {
                        short currentMa = (short)rawCurrent;
                        if (currentMa > 20)
                        {
                            _cachedInfo.IsAcConnected = true;
                            stateDetermined = true;
                            _busSummary = $"SMBus 0x{_smbusBase:X4}: Charging (+{currentMa}mA)";
                        }
                        else if (currentMa < -20)
                        {
                            _cachedInfo.IsAcConnected = false;
                            stateDetermined = true;
                            _busSummary = $"SMBus 0x{_smbusBase:X4}: Discharging ({currentMa}mA)";
                        }
                    }

                    // Command 0x16: BatteryStatus
                    if (!stateDetermined && TrySmbusReadWord(0x0B, 0x16, out ushort bst) && bst != 0 && bst != 0xFFFF)
                    {
                        // Bit 5 (0x0020) = CHARGING
                        // Bit 6 (0x0040) = DISCHARGING
                        bool isCharging = (bst & 0x0020) != 0;
                        bool isDischarging = (bst & 0x0040) != 0;

                        if (isCharging)
                        {
                            _cachedInfo.IsAcConnected = true;
                            stateDetermined = true;
                            _busSummary = $"SMBus SBS 0x0B: AC Online (Status 0x{bst:X4})";
                        }
                        else if (isDischarging)
                        {
                            _cachedInfo.IsAcConnected = false;
                            stateDetermined = true;
                            _busSummary = $"SMBus SBS 0x0B: On Battery (Status 0x{bst:X4})";
                        }
                    }

                    // Command 0x0D: RelativeStateOfCharge (%)
                    if (TrySmbusReadWord(0x0B, 0x0D, out ushort pct) && pct <= 100 && pct > 0)
                    {
                        _cachedInfo.Percentage = (int)pct;
                    }

                    // Command 0x09: Voltage (mV)
                    if (TrySmbusReadWord(0x0B, 0x09, out ushort mv) && mv > 1000 && mv < 25000)
                    {
                        _cachedInfo.Voltage = $"{mv / 1000}.{(mv % 1000) / 100}V";
                    }
                }
            }
            catch { }

            // 2. Authoritative: ACPI Embedded Controller (EC) Battery Status
            if (!stateDetermined)
            {
                try
                {
                    var ecStatusPort = new IOPort(0x66);
                    byte currentEcStatus = ecStatusPort.Byte;
                    if (currentEcStatus != 0xFF)
                    {
                        // Check standard Compal / Quanta battery status byte at offset 0xB0
                        if (TryReadEcByte(0xB0, out byte bstByte))
                        {
                            if ((bstByte & 0x02) == 0x02) // Charging bit
                            {
                                _cachedInfo.IsAcConnected = true;
                                stateDetermined = true;
                                _busSummary = $"ACPI EC 0x66/62: AC Plugged (Reg 0xB0=0x{bstByte:X2})";
                            }
                            else if ((bstByte & 0x01) == 0x01) // Discharging bit
                            {
                                _cachedInfo.IsAcConnected = false;
                                stateDetermined = true;
                                _busSummary = $"ACPI EC 0x66/62: On Battery (Reg 0xB0=0x{bstByte:X2})";
                            }
                        }

                        // Read fuel gauge percentage if available
                        if (TryReadEcByte(0xB2, out byte pctByte) && pctByte >= 5 && pctByte <= 100)
                        {
                            _cachedInfo.Percentage = pctByte;
                        }
                        else if (TryReadEcByte(0xD0, out byte d0Byte) && d0Byte >= 5 && d0Byte <= 100)
                        {
                            _cachedInfo.Percentage = d0Byte;
                        }
                    }
                }
                catch { }
            }

            // 3. Fallback: If AC is not positively confirmed connected, default to Discharging on Battery!
            if (!stateDetermined)
            {
                _cachedInfo.IsAcConnected = false;
                _busSummary = "Defaulting to Battery Mode (Discharging)";
            }
        }

        private static void InitSmbus()
        {
            if (_smbusProbed) return;
            _smbusProbed = true;

            try
            {
                // Scan PCI bus for SMBus Host Controller (Class 0x0C, Subclass 0x05)
                foreach (var device in Cosmos.HAL.PCI.Devices)
                {
                    if (device.ClassCode == 0x0C && device.Subclass == 0x05)
                    {
                        var pciDev = new PCIDeviceNormal(device.bus, device.slot, device.function);
                        ushort cmd = pciDev.ReadRegister16(0x04);
                        if ((cmd & 1) == 0)
                        {
                            pciDev.WriteRegister16(0x04, (ushort)(cmd | 1));
                        }

                        uint bar4 = pciDev.ReadRegister32(0x20);
                        if ((bar4 & 1) == 1)
                        {
                            _smbusBase = (ushort)(bar4 & 0xFFE0);
                            _busSummary = $"SMBus Host detected at I/O 0x{_smbusBase:X4}";
                            return;
                        }
                    }
                }

                // Intel ICH standard fallback at PCI 00:1F.3
                var pci = new PCIDeviceNormal(0, 0x1F, 3);
                if (pci != null && pci.VendorID == 0x8086)
                {
                    ushort cmd = pci.ReadRegister16(0x04);
                    if ((cmd & 1) == 0)
                    {
                        pci.WriteRegister16(0x04, (ushort)(cmd | 1));
                    }

                    uint bar4 = pci.ReadRegister32(0x20);
                    if ((bar4 & 1) == 1)
                    {
                        _smbusBase = (ushort)(bar4 & 0xFFE0);
                        _busSummary = $"Intel SMBus detected at I/O 0x{_smbusBase:X4}";
                    }
                }
            }
            catch { }
        }

        private static bool TrySmbusReadWord(byte slave7Bit, byte cmd, out ushort result)
        {
            result = 0;
            if (_smbusBase == 0) return false;

            try
            {
                var stsPort = new IOPort((ushort)(_smbusBase + 0x00));
                var ctlPort = new IOPort((ushort)(_smbusBase + 0x02));
                var cmdPort = new IOPort((ushort)(_smbusBase + 0x03));
                var addrPort = new IOPort((ushort)(_smbusBase + 0x04));
                var d0Port = new IOPort((ushort)(_smbusBase + 0x05));
                var d1Port = new IOPort((ushort)(_smbusBase + 0x06));

                // Clear status
                stsPort.Byte = 0xFE;

                int wait = 1000;
                while ((stsPort.Byte & 0x01) != 0 && --wait > 0) ;
                if (wait == 0) return false;

                // Read Word protocol: 0x4C (START bit 6, bits 4:2 = 011 Read Word)
                addrPort.Byte = (byte)((slave7Bit << 1) | 0x01);
                cmdPort.Byte = cmd;
                ctlPort.Byte = 0x4C;

                wait = 5000;
                while ((stsPort.Byte & 0x01) != 0 && --wait > 0) ;
                if (wait == 0) return false;

                byte sts = stsPort.Byte;
                if ((sts & 0x1C) != 0)
                {
                    stsPort.Byte = sts;
                    return false;
                }

                if ((sts & 0x02) != 0)
                {
                    result = (ushort)(d0Port.Byte | (d1Port.Byte << 8));
                    stsPort.Byte = 0x02;
                    return true;
                }
            }
            catch { }
            return false;
        }

        public static bool TryReadEcByte(byte addr, out byte val)
        {
            val = 0;
            try
            {
                var cmdPort = new IOPort(0x66);
                var dataPort = new IOPort(0x62);

                // Wait for IBF (Input Buffer Full, bit 1) to clear
                int timeout = 50000;
                while ((cmdPort.Byte & 0x02) != 0 && --timeout > 0) ;
                if (timeout == 0) return false;

                // Send Read command 0x80
                cmdPort.Byte = 0x80;

                // Wait for IBF to clear
                timeout = 50000;
                while ((cmdPort.Byte & 0x02) != 0 && --timeout > 0) ;
                if (timeout == 0) return false;

                // Send address to read
                dataPort.Byte = addr;

                // Wait for OBF (Output Buffer Full, bit 0) to be set
                timeout = 50000;
                while ((cmdPort.Byte & 0x01) == 0 && --timeout > 0) ;
                if (timeout == 0) return false;

                val = dataPort.Byte;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void ProbeHardware()
        {
            _cachedInfo = new BatteryInfo
            {
                HasBattery = true,
                IsLaptop = true,
                IsAcConnected = false,
                Percentage = 78,
                Status = "On Battery (Discharging)",
                Chemistry = "Li-Ion",
                ChassisType = "Portable / Laptop",
                Voltage = "11.1V"
            };

            try
            {
                // 1. Probe SMBIOS in physical memory range 0xF0000 - 0xFFFFF
                var biosBlock = new MemoryBlock(0x000F0000, 0x10000);
                int smbiosOffset = -1;

                for (uint i = 0; i < 0x10000 - 4; i += 16)
                {
                    if (biosBlock[i] == 0x5F && biosBlock[i + 1] == 0x53 &&
                        biosBlock[i + 2] == 0x4D && biosBlock[i + 3] == 0x5F) // "_SM_"
                    {
                        smbiosOffset = (int)i;
                        break;
                    }
                }

                if (smbiosOffset != -1)
                {
                    uint tableAddress = (uint)(biosBlock[(uint)(smbiosOffset + 0x16)] |
                                              (biosBlock[(uint)(smbiosOffset + 0x17)] << 8) |
                                              (biosBlock[(uint)(smbiosOffset + 0x18)] << 16) |
                                              (biosBlock[(uint)(smbiosOffset + 0x19)] << 24));

                    uint tableLength = (uint)(biosBlock[(uint)(smbiosOffset + 0x1A)] |
                                             (biosBlock[(uint)(smbiosOffset + 0x1B)] << 8));

                    if (tableAddress > 0 && tableLength > 0 && tableLength < 0x10000)
                    {
                        var tableBlock = new MemoryBlock(tableAddress, tableLength);
                        uint pos = 0;

                        while (pos + 4 < tableLength)
                        {
                            byte structType = (byte)tableBlock[pos];
                            byte structLen = (byte)tableBlock[pos + 1];
                            if (structLen < 4) break;

                            if (structType == 3) // Enclosure / Chassis
                            {
                                if (pos + 5 < tableLength)
                                {
                                    byte chassis = (byte)(tableBlock[pos + 5] & 0x7F);
                                    if (chassis == 8 || chassis == 9 || chassis == 10 || chassis == 11 || chassis == 14 || chassis == 30 || chassis == 31 || chassis == 32)
                                    {
                                        _cachedInfo.IsLaptop = true;
                                        _cachedInfo.HasBattery = true;
                                        _cachedInfo.IsAcConnected = false;
                                        _cachedInfo.Percentage = 78;
                                        _cachedInfo.Status = "On Battery (Discharging)";
                                        _cachedInfo.ChassisType = "Laptop / Notebook";
                                    }
                                    else if (chassis == 3 || chassis == 4 || chassis == 6 || chassis == 7)
                                    {
                                        _cachedInfo.ChassisType = "Desktop / Tower PC";
                                    }
                                }
                            }
                            else if (structType == 22) // Portable Battery
                            {
                                _cachedInfo.HasBattery = true;
                                _cachedInfo.IsLaptop = true;
                                _cachedInfo.IsAcConnected = false;
                                _cachedInfo.Percentage = 78;
                                _cachedInfo.Status = "On Battery (Discharging)";
                                if (pos + 9 < tableLength)
                                {
                                    byte chem = (byte)tableBlock[pos + 9];
                                    if (chem == 6) _cachedInfo.Chemistry = "Lithium-Ion (Li-Ion)";
                                    else if (chem == 8) _cachedInfo.Chemistry = "Lithium-Polymer (Li-Poly)";
                                    else if (chem == 5) _cachedInfo.Chemistry = "NiMH";
                                }
                            }

                            pos += structLen;
                            while (pos + 1 < tableLength && !(tableBlock[pos] == 0 && tableBlock[pos + 1] == 0))
                            {
                                pos++;
                            }
                            pos += 2;
                        }
                    }
                }
            }
            catch { }

            try
            {
                // 2. Probe ACPI Embedded Controller (Port 0x66 status)
                var ecStatusPort = new IOPort(0x66);
                byte ecStatus = ecStatusPort.Byte;
                if (ecStatus != 0xFF)
                {
                    _cachedInfo.IsLaptop = true;
                    _cachedInfo.HasBattery = true;
                    _cachedInfo.IsAcConnected = false;
                    _cachedInfo.Percentage = 78;
                    _cachedInfo.Status = "On Battery (Discharging)";
                }
            }
            catch { }

            // Initial hardware power state detection
            DetectHardwarePowerState();
        }
    }
}
