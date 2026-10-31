# WeatherOS Cumulative System Update v3.2.0 (Release Package)

**Package File**: `WeatherOS-Update-FileManager-v3.2.0.pkg`  
**Target Architecture**: x86 (IA-32, 32-bit Bare-Metal Protected Mode)  
**Binary Size**: 8,595,358 bytes (~8.20 MB)  
**Checksum (CRC32)**: `0x440C3319`  
**Release Date**: October 18, 2026  
**Target Slot**: LBA 64 (`0:\boot\WeatherOS.bin`) & Boot Sectors LBA 0–8  

---

## 1. Overview & Scope

The **v3.2.0 Cumulative Update Package** delivers the unified File & Storage Explorer suite, comprehensive bare-metal storage persistence, and fundamental architectural remediations to the bootloader pipeline and ATA physical disk flashing engine. 

This update resolves the `FAULT: 06 (#UD Invalid Opcode, EIP: 00000007)` bare-metal boot failure on physical Intel Atom / ICH7-M systems, eliminates multi-second network boot hangs during `raw_tcp` initialization, and transitions the OTA package installer to a firmware-grade, platter-level flash protocol with 100% read-back sector verification.

---

## 2. Key Features & Subsystem Upgrades

### 2.1. File Explorer & Notepad Storage Suite (`diskmgr` & `notepad`)
- **Directory Browsing & Hierarchical Navigation**: Native support for navigating multi-level directory trees on FAT32 partitions directly on bare metal.
- **Folder Creation & File Operations**: Interactive folder creation (`mkdir`), file deletion, and directory refresh without OS reboot.
- **Unified Storage Clipboard**: Universal Copy/Paste clipboard subsystem allowing seamless transfer of files and folders between physical drives (Live USB, Internal IDE/SATA HDD, Virtual Store).
- **Deep Notepad Integration**: Context-aware "Open in Notepad" file binding, unified file picker modal, and safe file persistence with anti-wipe protections.

### 2.2. Network Boot Latency Remediation (`raw_tcp`)
- **Strict DHCP Timing Bound**: Reduced DHCP Discover and Request timeout window from multi-second polling loops to an absolute maximum of 150 ms (retransmitting once at 50 ms).
- **Direct Cable Fallback**: Eliminated blocking ARP discovery passes across candidate router gateways (Xiaomi, MEO, TP-Link, QEMU). When DHCP is silent, the network stack transitions instantly to direct peer-to-peer link mode (`192.168.1.180`, Gateway `192.168.1.1`, Subnet `255.255.255.0`), allowing hardware network initialization to complete in under 200 ms.

---

## 3. Engineering Analysis: Bootloader & ATA Storage Pipeline Fixes

### 3.1. Problem Analysis: `FAULT: 06` on Disk Boot
During hard disk boot on physical machines (Toshiba NB100 / Intel Atom N270), the kernel halted with an invalid opcode exception:

    WeatheGDPIG0G1G2G3G4R0R1H0H1H2H3RZG5E0V0
    FAULT: 06 ERR: 00000000 EIP: 00000007 ESP: 0205001C
    STK: 02051AF5 02776585 00001903 00000000
    STK2: 00000B91 02050038 028311A8 00000011

Investigation revealed a dual failure mode:
1. **Volatile Drive Cache Truncation**: When flashing the kernel to internal mechanical HDDs, issuing `0xEA` (FLUSH CACHE EXT) following 28-bit sector writes (`0x30`) caused legacy IDE controllers to reject the flush with an `ERR` status. An immediate hardware reboot via I/O port `0xCF9` reset the machine while sector 1210 (containing the Virtual Method Table array `mTypes_Contents`) was still residing in the drive's volatile DRAM track buffer, leading to silent platter corruption.
2. **Bootloader Stack & Mode Transition Instability**: The legacy Stage 2 bootloader performed 263 consecutive Real/Protected mode transitions with uninitialized 32-bit stack registers (`SS`), crossed PhoenixBIOS 63-sector track limits, and zeroed 32 MB of memory directly into active address space.

### 3.2. Engineering Remediation

    +-----------------------------------------------------------------------------------+
    |                           WEATHEROS v3.2.0 FLASH PIPELINE                         |
    +-----------------------------------------------------------------------------------+
    | [1/5] 28-bit ATA PIO Write (LBA 64+) with 64 KB Periodic Cache Drains             |
    | [2/5] Standard ATA FLUSH CACHE (0xE7) + FLUSH CACHE EXT (0xEA) with DRDY Polling  |
    | [3/5] 100% Comprehensive Read-Back Verification across all 16,786 Sectors         |
    | [4/5] Stage 2 Bootloader & MBR Binary Upgrade to LBAs 0-8                         |
    | [5/5] Final Platter Sync & 3-Second Spindle/Head Park Delay before Safe Reboot    |
    +-----------------------------------------------------------------------------------+

1. **Firmware-Grade Platter-Level Flash Protocol**:
   - `CustomPciAtaDriverService.cs` was updated to enforce standard ATA command `0xE7` (FLUSH CACHE) with active `DRDY` (Drive Ready) polling for up to 10 seconds, ensuring mechanical drive DRAM buffers are completely committed to magnetic platters before execution resumes.
   - Periodic cache synchronizations are enforced every 128 sectors (64 KB) during kernel writes.
2. **100% Sector Verification Engine**:
   - The installer now reads back all **16,786 sectors** from physical media in 16 KB batches, verifying them byte-for-byte against the package payload. Any defective sector triggers an automatic rewrite and re-verification cycle before installation is declared complete.
3. **Stage 2 Protected Mode Loader Hardening**:
   - `custom_stage2.asm` was rebuilt with dedicated 32-bit stack allocation (`SS = 0x10`, `ESP = 0x0007C000`), track-safe 32-sector (16 KB) transfer blocks, Fast A20 gate hardware locking via port `0x92`, CPU cache invalidation (`wbinvd`), and automatic RAM fallback entry point scanning.
4. **Controlled Reboot & Spindle Park Sequence**:
   - `NetTransferApp.cs` implements a 3-second hardware countdown with final cache flush prior to CPU reboot, allowing 2.5" mechanical drive heads to reach the landing zone safely.

---

## 4. Package Deployment Instructions

### Method A: Over-The-Air (OTA) via WeatherTransfer
1. Boot WeatherOS on the target machine (via Live USB or internal storage) and connect the Ethernet cable.
2. Press **[F7]** on the desktop to launch the **Network Transfer & OTA Synchronizer**.
3. On the companion PC, run `WeatherTransfer.exe`:
   `WeatherTransfer.exe final\Updates\WeatherOS-Update-FileManager-v3.2.0.pkg`
4. Once transmission reaches 100%, press **[ENTER]** in WeatherOS to initiate the physical storage flash.
5. The system will execute the 5-step firmware flash sequence, verify 100% of sectors, park the drive heads, and reboot into WeatherOS v3.2.0.

### Method B: Live USB Clean Reinstallation
Write `final/WeatherOS.img` or `final/WeatherOS.iso` to a USB flash drive using **Rufus** (DD Image Mode) or `dd`, boot into the Live environment, and select **[2] Install WeatherOS to Hard Disk** from the boot manager.
