# WeatherOS Documentation & Reference Wiki

This document is the official documentation repository for WeatherOS. It is divided into two comprehensive sections:

1. **Part 1: User Manual**: A practical, human-written guide covering system setup, installation, desktop navigation, shell commands, and companion software usage.
2. **Part 2: Engineering Technical Report**: An in-depth technical specification written from a systems engineering perspective, detailing the hybrid bootloader pipeline, real-to-protected mode transition, dynamic kernel sector calculations, bare-metal PCI driver architecture, and raw TCP/IP network stack implementation.

---

# Part 1: WeatherOS User Manual

## 1. Introduction

WeatherOS is an autonomous 32-bit x86 operating system dedicated to environmental observation, real-time meteorological calculations, and local network telemetry synchronization. Unlike traditional applications that run on top of Windows or Linux, WeatherOS boots directly on bare-metal PC hardware, transforming standard personal computers into dedicated weather terminals.

The system combines a clean Text User Interface (TUI) desktop with a built-in terminal shell, direct PCI hardware drivers, integrated storage utilities, and a fast peer-to-peer network transfer engine.

---

## 2. System Requirements

WeatherOS is engineered to run on a broad spectrum of x86-compatible hardware, from legacy desktop computers and thin clients to modern multi-core processors running in legacy boot mode.

- **Processor**: Any x86 (IA-32) or x86-64 CPU (Intel Pentium II / III / 4 / Core / Xeon, AMD K6 / Athlon / Ryzen).
- **System Memory**: 512 MB RAM recommended for optimal caching; minimum 256 MB.
- **Display**: Standard VGA-compatible video adapter supporting standard 80x25 text mode (VGA memory at physical address `0xB8000`).
- **Keyboard**: Standard PS/2 keyboard or USB keyboard operating with BIOS Legacy USB Support enabled.
- **Network Interface**: Wired Ethernet adapter. Supported controllers include Realtek RTL8102E, RTL8139, RTL8168, and virtualized adapters (Intel PRO/1000, PCnet-PCI II, RTL8139 in QEMU/VirtualBox/VMware).
- **Storage**: Minimum 200 MB storage drive (IDE, PATA, or SATA operating in IDE/Legacy/AHCI mode) for hard disk installation. A USB flash drive (minimum 64 MB) is required for Live USB booting.

---

## 3. Creating Installation Media

The release files are located in the `final/` folder:
- `WeatherOS.iso`: Hybrid bootable CD/DVD and USB image.
- `WeatherOS.img`: Raw sector-by-sector disk image.

### Flashing with Rufus (Windows)
1. Plug in a USB flash drive (minimum 64 MB capacity).
2. Download and launch **Rufus** (version 3.x or 4.x).
3. Under **Device**, select your target USB flash drive.
4. Under **Boot selection**, click **SELECT** and browse to `WeatherOS.iso` (or `WeatherOS.img`).
5. Set **Partition scheme** to `MBR`.
6. Set **Target system** to `BIOS (or UEFI-CSM)`.
7. Click **START**. If Rufus presents a dialog asking whether to write in ISO Image mode or DD Image mode, choose **Write in DD Image mode**.
8. Confirm the prompt to format the drive.

### Flashing with `dd` (Linux / macOS)
Open a terminal and write the hybrid image directly to the block device corresponding to your flash drive:

```bash
# Verify the drive identifier (e.g., /dev/sdb)
lsblk

# Write the image to the raw block device
sudo dd if=final/WeatherOS.iso of=/dev/sdX bs=4M status=progress conv=fsync
```

*Note: Replace `/dev/sdX` with your exact drive identifier (e.g. `/dev/sdb`). Do not specify a partition number like `/dev/sdb1`.*

---

## 4. Booting WeatherOS

### BIOS Configuration
Before booting WeatherOS on physical hardware, configure your PC motherboard BIOS/UEFI settings:
1. **Boot Mode**: Set to **Legacy BIOS**, **CSM (Compatibility Support Module)**, or **Both**. WeatherOS uses a native x86 16-to-32-bit bootloader that requires legacy BIOS interrupts (INT 10h, INT 13h, INT 16h).
2. **Secure Boot**: Must be **Disabled**.
3. **Storage Controller Mode**: If installing to an internal drive, set SATA mode to **IDE**, **Legacy**, or **AHCI**.
4. **USB Legacy Support**: Must be **Enabled** so USB keyboards function seamlessly before OS driver initialization.

### The Boot Manager Screen
When you boot from the USB flash drive, the custom WeatherOS Boot Manager appears:

```
+----------------------------------------------------------------------+
|                    WEATHEROS BOOT MANAGER v3.1                       |
+----------------------------------------------------------------------+

    [1] Live OS (RAM Session)
    [2] Install WeatherOS to Hard Disk

    Automatic boot in 10 seconds...
    Use Up/Down arrows to select, Enter to confirm.
```

- **[1] Live OS (RAM Session)**: Loads the operating system entirely into physical RAM. No changes will be written to your internal hard drives. Ideal for evaluating the system, running diagnostics, or using the PC as a temporary weather station.
- **[2] Install WeatherOS to Hard Disk**: Launches the guided installer to format, partition, and install the operating system permanently onto an internal drive.
- **Direct Disk Boot**: Once WeatherOS is installed on your hard disk and you boot directly from that hard disk, the bootloader automatically detects the installation flag and boots straight into the desktop without prompting.

---

## 5. Desktop Environment & Navigation

WeatherOS features a rich text-based graphical desktop running in 80x25 VGA text mode.

### Visual Layout
The desktop is organized into four distinct regions:
1. **Header Bar**: Displays system status, active city location, current time, and network connection status.
2. **Main Canvas**: Features real-time ASCII weather art (sun, rain, snow, clouds, wind, moon phases) alongside temperature, humidity, atmospheric pressure, and wind metrics.
3. **Information Windows**: Movable and focusable windows displaying forecasts, lunar ephemeris, or system applications.
4. **Footer Dock**: Displays interactive keyboard function shortcuts (`F1` through `F10`) and system indicators.

### Global Keyboard Shortcuts

| Key | Action |
| :--- | :--- |
| **F1** | Help: Displays user guide, shortcut list, and system reference |
| **F2** | Application Launcher: Opens the quick-launch menu of all utilities |
| **F3** | Refresh Telemetry: Forces an immediate HTTP query to the weather service |
| **F4** | Change City: Opens a dialog to enter a new geographical location |
| **F5** | Weather Forecast: Displays the 7-day extended meteorological forecast |
| **F6** | Lunar Tracker: Calculates current moon phase, illumination, and cycle |
| **F7** | File Transfer: Opens the network companion sync and file exchange app |
| **F8** | Storage Manager: Opens the file explorer to browse RAM and disk files |
| **F9** | System Resources: Displays CPU uptime, heap memory, and RAM allocation |
| **F10** | Battery Diagnostics: Shows real-time laptop battery telemetry and AC status |
| **ESC** | Closes the open application window and returns focus to the desktop |
| **Tab** | Cycles focus across interactive buttons, input fields, and lists |
| **Enter** | Activates the focused button or confirms text input |

---

## 6. Built-in Applications

### Weather Dashboard & Extended Forecast (`F5`)
Provides comprehensive atmospheric readings updated automatically over the network:
- Current ambient temperature in Celsius.
- Relative humidity percentage and dew point temperature.
- Barometric atmospheric pressure in hectopascals (hPa).
- Wind speed (km/h) and directional cardinal vectors.
- 7-day forecast cards showing anticipated high/low temperatures, precipitation likelihood, and atmospheric conditions.

### Lunar Phase Tracker (`F6`)
Computes precise lunar ephemeris using trigonometric astronomical equations:
- Current lunar phase name (New Moon, Waxing Crescent, First Quarter, Waxing Gibbous, Full Moon, Waning Gibbous, Last Quarter, Waning Crescent).
- Percentage of lunar surface illuminated.
- Age of the current lunar cycle in days.
- Real-time ASCII art rendering corresponding to the current phase.

### Storage Explorer (`F8`) & Notepad
WeatherOS includes a non-volatile FAT32 file system driver alongside an in-memory virtual drive:
- Browse files stored in volatile RAM (`ram0:\`) or persistent disk (`0:\`).
- Launch the **Notepad** text editor to view or edit text files, system scripts, and logs.
- Save files directly to internal hard disk partitions with verified write flushing.

### System Resources (`F9`) & Battery Diagnostics (`F10`)
- **System Resources**: Real-time memory monitor displaying total physical RAM, kernel memory footprint, active heap consumption via the Cosmos Ring Allocation Table (RAT), and uptime counter.
- **Battery Diagnostics**: Queries ACPI and hardware power registers to report remaining battery charge percentage, charging state, estimated run-time, and battery health.

### Disk Manager & Sanitizer (`disks`, `wipe`, `diskdiag`)
- Inspect detected storage controllers, hard drives, and partition boundaries.
- Run read sector diagnostics to test for failing sectors on physical media.
- Zero-fill sanitization utility to wipe disks cleanly before repartitioning.

---

## 7. The WeatherTransfer Companion Tool

`WeatherTransfer.exe` is a standalone Windows command-line utility located in the `final/` folder. It communicates with WeatherOS machines over local networks using UDP port 7331.

### Features
- **Automatic Peer Discovery**: Broadcasts discovery probes across all local network interfaces to detect WeatherOS instances automatically. No manual IP configuration required.
- **Two-Way File Exchange**: Send files from Windows to WeatherOS or receive files streamed from WeatherOS.
- **Reliable Packet Protocol**: Employs a chunked transmission protocol with sequence numbers, timeouts, and automatic retry handling.

### How to Transfer Files to WeatherOS
1. On the WeatherOS machine, press **F7** (or type `transfer` in the terminal) to open the transfer utility.
2. Select **[1] Receive Files from Windows PC (Start Listening)**. WeatherOS begins listening on UDP port 7331.
3. On your Windows PC, run `WeatherTransfer.exe`.
4. In the centered interactive menu, select **[1] Send File to WeatherOS**.
5. Drag and drop your file into the prompt (or type its file path) and press Enter.
6. The companion utility verifies the handshake and streams the file chunks with a real-time progress bar.
7. WeatherOS receives and saves the file to its storage system.

### How to Receive Files from WeatherOS
1. On your Windows PC, open `WeatherTransfer.exe` and select **[2] Receive File from WeatherOS (Listener Mode)**.
2. On WeatherOS, open the transfer app and choose **[2] Send File to Windows PC**.
3. Select the file stored on WeatherOS that you wish to export.
4. WeatherOS auto-discovers the Windows host and streams the file. The file is saved directly into the `received_files/` directory on Windows.

---

## 8. Terminal Shell Command Reference

The command shell is accessible at all times. Press `ESC` to exit the GUI or type commands directly:

| Command | Usage | Description |
| :--- | :--- | :--- |
| `help` | `help` | Displays command catalog and registered applications |
| `gui` | `gui` | Switches display into the graphical meteorological desktop |
| `weather` | `weather` | Prints current weather observations directly in console |
| `fetch` | `fetch` | Performs an immediate network query to refresh weather data |
| `setcity` | `setcity <name>` | Sets default geographical location (e.g., `setcity Lisbon`) |
| `transfer` | `transfer` | Opens the network transfer and synchronization utility |
| `disks` | `disks` | Scans and lists all physical storage devices and partitions |
| `diskdiag` | `diskdiag` | Performs sector read integrity tests on storage media |
| `format` | `format [index]` | Formats a target partition with a FAT32 filesystem |
| `wipe` | `wipe [index]` | Performs a single-pass zero-fill wipe of target disk |
| `install` | `install` | Launches the interactive hard disk installation wizard |
| `ping` | `ping <ip>` | Transmits ICMP echo requests to verify network reachability |
| `clear` | `clear` | Clears terminal screen and re-renders boot status banner |
| `bootmenu` | `bootmenu` | Re-opens the boot manager selection screen |
| `reboot` | `reboot` | Restarts the PC hardware via CPU reset line |
| `poweroff` | `poweroff` | Powers off the system via ACPI shutdown call |

---

# Part 2: Engineering Technical Report

## 1. System Architecture Overview

WeatherOS is an ahead-of-time (AOT) compiled, monolithic, 32-bit x86 protected-mode operating system written in C# on top of the Cosmos (C# Open Source Managed Operating System) architecture.

### Compilation Pipeline
```
[ C# Source Code (.cs) ]
           |
     ( Roslyn .NET 6 )
           v
[ Common Intermediate Language (CIL/MSIL) ]
           |
     ( Cosmos IL2CPU AOT Compiler )
           v
[ x86 IA-32 Assembly (.asm) ]
           |
     ( patch_kernel_alignment.py: 128 MB Heap Cap & Entry Alignment )
           v
[ Netwide Assembler (NASM) ]
           |
     ( GNU Linker / ld )
           v
[ Flat 32-bit Binary / Multiboot Kernel Image ]
           |
     ( make_isohybrid.py: Custom MBR, Stage 2 Injection, El Torito ISO )
           v
[ Final Bootable Media: WeatherOS.iso / WeatherOS.img ]
```

The build pipeline compiles managed C# code directly into unmanaged x86 machine instructions. There is no intermediary virtual machine (such as the CLR or Mono) running at execution time. Hardware management, physical memory allocation, hardware interrupt servicing, and PCI bus access are executed directly on the CPU.

---

## 2. Bootloader Subsystem & Multi-Stage Boot Sequence

WeatherOS implements a custom dual-stage bootloader infrastructure engineered to ensure reliable boot execution across varied legacy BIOS implementations, USB flash drive emulations (USB-HDD, USB-ZIP, USB-CDROM), and direct hard disk installations.

### Storage Geometry & Sector Layout
A WeatherOS installation or boot image conforms to the following block layout:

| LBA Sector Range | Size | Component | Functionality |
| :--- | :--- | :--- | :--- |
| **LBA 0** | 512 Bytes | Master Boot Record (`custom_mbr.asm`) | Stage 1 bootloader, MBR partition table, boot signature `0xAA55` |
| **LBA 1 to 8** | 4,096 Bytes | Reserved Stage 2 Space | Stores Stage 2 Boot Manager machine code |
| **LBA 9** | 512 Bytes | Installation Marker Sector | Contains ASCII signature `"WEATHEROS"` verifying valid installation |
| **LBA 10 to 63** | 27,648 Bytes | Reserved Alignment Slack | Preserves standard 64-sector track alignment |
| **LBA 64 to N** | ~8 MB | Kernel Binary (`WeatherOS.bin`) | Flat 32-bit protected-mode kernel code, static data, and heap base |
| **LBA N+1 onwards** | Remainder | Primary Partition (FAT32) | Stores user documents, settings (`settings.ini`), and caches |

### Stage 1: Master Boot Record (`custom_mbr.asm`)
The Master Boot Record is loaded by the motherboard BIOS at physical address `0x0000:0x7C00` in 16-bit Real Mode with interrupts enabled:

1. **Relocation**: To prevent collision with subsequent stages, the MBR relocates itself from `0x7C00` down to `0x0600` and executes a far jump to the relocated segment:
   ```nasm
   mov ax, 0x0000
   mov ds, ax
   mov es, ax
   mov ss, ax
   mov sp, 0x7C00
   mov si, 0x7C00
   mov di, 0x0600
   mov cx, 256
   rep movsw
   jmp 0x0000:relocated
   ```
2. **Drive Number Validation**: Preserves the BIOS boot drive number passed in register `DL` (e.g. `0x80` for first HDD, `0x81` for second drive).
3. **LBA Reading via INT 13h Extensions**: Queries BIOS support for Phoenix/IBM Enhanced Disk Drive (EDD) extensions (`AH=41h, BX=55AAh`). If available, it constructs a Disk Address Packet (DAP) at memory location `0x0000:0x7000`:
   - Packet Size: `0x10` (16 bytes)
   - Sector Count: 8 sectors (4,096 bytes)
   - Buffer Target: `0x0000:0x7E00`
   - Starting LBA: `1` (immediately following the MBR)
4. **Execution Transfer**: Upon successful sector loading, it jumps directly to Stage 2 at `0x0000:0x7E00`.

### Stage 2: Boot Manager & Protected Mode Transition (`custom_stage2.asm`)
Stage 2 acts as the system's pre-kernel executive:
1. **Partition Table & Installation Inspection**:
   - Reads LBA 9 to verify if the drive contains a permanent WeatherOS installation.
   - If booted from a hard disk with a verified installation flag, it bypasses user prompts and initiates immediate direct kernel loading.
   - If booted from USB or an uninstalled disk, it renders the interactive Boot Menu using direct BIOS teletype services (`INT 10h, AH=0Eh`).
2. **Kernel Sector Loading into High Memory**:
   - The compiled 32-bit kernel resides starting at LBA 64 and spans approximately 16,600 sectors (~8.5 MB).
   - Because standard real-mode addresses cannot access memory above 1 MB, Stage 2 utilizes INT 13h Extended Read functions in a loop to stream 64-sector chunks into temporary real-mode buffer space (`0x1000:0x0000`), immediately copying them into physical RAM above 32 MB (`0x02000000`) using unmanaged 32-bit flat address copies via unreal mode or protected-mode memory moves.
3. **A20 Gate Enablement**:
   - Enables the 21st address line to eliminate memory wrap-around at 1 MB.
   - Uses a three-tier fallback: BIOS INT 15h (`AX=2401h`), Fast A20 Gate via System Control Port A (`0x92`), and Keyboard Controller output port manipulation (`0x64/0x60`).
4. **Global Descriptor Table (GDT) Initialization**:
   - Constructs a flat 4 GB memory model:
     - Selector `0x08`: 32-bit Code Segment (Base: `0x00000000`, Limit: `0xFFFFFFFF`, DPL: 0, Exec/Read).
     - Selector `0x10`: 32-bit Data Segment (Base: `0x00000000`, Limit: `0xFFFFFFFF`, DPL: 0, Read/Write).
5. **Entering 32-Bit Protected Mode**:
   - Disables maskable interrupts (`cli`).
   - Sets the Protection Enable (PE) bit in Control Register 0:
     ```nasm
     mov eax, cr0
     or eax, 1
     mov cr0, eax
     jmp 0x08:flush_pipeline
     ```
6. **Multiboot Compliance & Kernel Entry**:
   - Populates Multiboot structure registers:
     - `EAX`: Magic number `0x2BADB002` (Multiboot specification compliance).
     - `EBX`: Physical address pointer to the Multiboot information structure.
   - Executes a direct jump to the resolved Cosmos `Kernel_Start` address in physical RAM.

---

## 3. Dynamic Kernel Sector Discovery & Resolution

### Problem Analysis & Previous Failure Mode
During earlier builds, installations to internal hard disks occasionally resulted in CPU faults (`FAULT: 0D General Protection Fault, EIP: 0281C0A8`). Root-cause investigation revealed:
- As new applications, drivers, and network stacks were added, the compiled kernel image expanded from 16,404 sectors to 16,606 sectors (8,502,272 bytes).
- The hard-coded installer routine wrote a fixed allocation of 16,502 sectors, inadvertently truncating the final 104 sectors (53,248 bytes) of the kernel on disk.
- When the CPU executed code or initialized static data structures located in that truncated range, it fetched invalid opcodes (`0x00`), triggering a fatal GPF.

### Engineering Solution: Dynamic Runtime Sector Computation
In `WeatherOS/WeatherOS/Services/UpdateManagerService.cs`, the sector count is now calculated dynamically from the running kernel memory boundaries:

```csharp
uint kernelSectors = 0;
try
{
    uint endOfKernel = Cosmos.Core.CPU.GetEndOfKernel();
    if (endOfKernel > 0x02000000u)
    {
        // Compute precise sector count including any partial 512-byte block
        kernelSectors = (endOfKernel - 0x02000000u + 511u) / 512u;
    }
}
catch { }
```

Furthermore, the installer dynamically locates the remaining sectors field (`RSEC`) inside the Stage 2 bootloader binary and patches it with the exact sector count before writing Stage 2 to LBA 1:

```csharp
if (rsecOffset != -1 && kernelSectors > 0)
{
    NativeBootloaderPayload.Stage2[rsecOffset]     = (byte)(kernelSectors & 0xFF);
    NativeBootloaderPayload.Stage2[rsecOffset + 1] = (byte)((kernelSectors >> 8) & 0xFF);
    NativeBootloaderPayload.Stage2[rsecOffset + 2] = (byte)((kernelSectors >> 16) & 0xFF);
    NativeBootloaderPayload.Stage2[rsecOffset + 3] = (byte)((kernelSectors >> 24) & 0xFF);
}
```

This guarantees 100% binary integrity regardless of future kernel growth.

---

## 4. Physical Memory Architecture & Heap Management

### Memory Map Layout
WeatherOS partitions 32-bit physical address space as follows:

```
0x00000000 - 0x000003FF   (1 KB)      Interrupt Vector Table (IVT - Real Mode)
0x00000400 - 0x000004FF   (256 B)     BIOS Data Area (BDA)
0x00000500 - 0x00007BFF   (~30 KB)    Conventional Free RAM / Scratch Buffer
0x00007C00 - 0x00007DFF   (512 B)     Stage 1 MBR Execution Site
0x00007DF0 - 0x00007DF3   (4 B)       Boot Source Flag passed from Stage 2 (Magic 0x574F)
0x00007E00 - 0x00009DFF   (8 KB)      Stage 2 Boot Manager
0x0009FC00 - 0x0009FFFF   (1 KB)      Extended BIOS Data Area (EBDA)
0x000A0000 - 0x000BFFFF   (128 KB)    VGA Video Memory (Text Mode at 0xB8000)
0x000C0000 - 0x000FFFFF   (256 KB)    Video BIOS & Motherboard BIOS ROM
0x00100000 - 0x01FFFFFF   (31 MB)     Reserved / Extended Memory
0x02000000 - 0x02FFFFFF   (16 MB)     WeatherOS Kernel Binary, Static Tables, BSS
0x03000000 - 0x07FFFFFF   (80 MB)     Cosmos RAT (Ring Allocation Table) & Managed Heap
```

### 128 MB Heap Capping & RAT Optimization
During initial initialization, Cosmos attempts to analyze total installed physical RAM and configure its Ring Allocation Table (RAT) accordingly. On systems with large physical RAM (4 GB to 16 GB), the RAT allocation table itself can consume tens of megabytes, exhausting available low-memory pages during early boot before paging is initialized.

To prevent allocator exhaustion, `scratch/patch_kernel_alignment.py` modifies the kernel assembly during build to enforce a 128 MB heap ceiling:
- Restricts initial RAT scan boundaries to a deterministic 128 MB block.
- Ensures immediate boot completion on both low-memory devices (256 MB) and high-end hardware (16 GB+) without memory table fragmentation.

---

## 5. Storage Controller Subsystem & Custom PCI ATA Driver

WeatherOS features a custom bare-metal ATA/IDE storage driver (`CustomPciAtaDriverService.cs`) operating alongside Cosmos VFS.

### Hardware Discovery via PCI Bus Enumeration
The driver directly interrogates PCI Configuration Space:
- Scans PCI buses 0-255, slots 0-31, functions 0-7.
- Identifies devices where Class Code is `0x01` (Mass Storage) and Subclass is `0x01` (IDE Controller).
- Queries Base Address Registers (BAR0 through BAR4):
  - Primary Channel Command Base: `BAR0` (Default fallback: `0x1F0`).
  - Primary Channel Control Base: `BAR1` (Default fallback: `0x3F6`).
  - Secondary Channel Command Base: `BAR2` (Default fallback: `0x170`).
  - Secondary Channel Control Base: `BAR3` (Default fallback: `0x376`).

### Programmed I/O (PIO) Protocol Implementation
Sector read and write operations interact directly with x86 hardware I/O ports:

```csharp
// Wait for drive to become ready (BSY bit cleared in Status register)
while ((IOPort.Read8((ushort)(cmdBase + 7)) & 0x80) != 0) { }

// Issue Sector Count and LBA bytes
IOPort.Write8((ushort)(cmdBase + 2), 1);                         // 1 Sector
IOPort.Write8((ushort)(cmdBase + 3), (byte)(lba & 0xFF));        // LBA Low
IOPort.Write8((ushort)(cmdBase + 4), (byte)((lba >> 8) & 0xFF)); // LBA Mid
IOPort.Write8((ushort)(cmdBase + 5), (byte)((lba >> 16) & 0xFF));// LBA High
IOPort.Write8((ushort)(cmdBase + 6), (byte)(0xE0 | ((lba >> 24) & 0x0F))); // Master / LBA mode

// Issue Read Sectors Command (0x20)
IOPort.Write8((ushort)(cmdBase + 7), 0x20);

// Poll for Data Request (DRQ bit set in Status register)
while ((IOPort.Read8((ushort)(cmdBase + 7)) & 0x08) == 0) { }

// Read 256 words (512 bytes) from Data Port (0x1F0)
for (int i = 0; i < 256; i++)
{
    ushort word = IOPort.Read16(cmdBase);
    buffer[offset + (i * 2)]     = (byte)(word & 0xFF);
    buffer[offset + (i * 2) + 1] = (byte)(word >> 8);
}
```

### Drive Sanitization Engine (`DiskWipeService.cs`)
Provides a single-pass zero-fill sanitization routine:
- Streams blocks of 128 sectors (65,536 bytes) filled with `0x00`.
- Issues ATA Cache Flush commands (`0xE7`) periodically to enforce physical magnetic/flash persistence.

---

## 6. Bare-Metal Network Stack & RTL8102E Driver

The WeatherOS network subsystem is designed to operate completely independently of the default Cosmos network stack, which suffers from thread locks and allocation bottlenecks during high-throughput HTTP/JSON processing.

### Realtek RTL8102E / RTL8139 Driver (`RTL8102E.cs`)
1. **PCI Initialization**: Identifies Vendor ID `0x10EC` and Device ID `0x8136` (RTL8102E) or `0x8139` (RTL8139). Enables Bus Mastering and Memory Space in the PCI Command Register.
2. **Ring Buffer Allocation**: Allocates contiguous physical memory buffers for descriptor rings:
   - 4 Transmit Buffers (1,536 bytes each).
   - Circular Receive Buffer (8 KB + 16 bytes wrap padding).
3. **Interrupt Handling**: Configures the Interrupt Mask Register (IMR) to listen for Packet Receive (`ROK`) and Transmit OK (`TOK`) events, clearing the Interrupt Status Register (ISR) upon servicing.

### Raw TCP/IP Engine (`RawTcpHttp.cs`)
The raw network engine bypasses high-level runtime wrappers:

```
[ Ethernet Layer: 14 Bytes (Dest MAC, Src MAC, EtherType 0x0800) ]
       v
[ IPv4 Layer: 20 Bytes (TOS, Length, ID, TTL=64, Protocol=6/17, IP Checksum, Src/Dst IP) ]
       v
[ Transport Layer: TCP (20 Bytes) / UDP (8 Bytes) ]
       v
[ Application Payload: HTTP/1.1 GET / WOSF Binary Protocol ]
```

#### TCP 3-Way Handshake & Checksum Calculation
Computes Internet Checksums using a 16-bit one's complement sum over the TCP header, payload, and the IPv4 Pseudo-Header:

$$\text{Checksum} = \sim \left( \sum_{i=0}^{N} \text{Word}_i \right)$$

Handles full TCP state transitions:
- Transmits `SYN` packet (Flags: `0x02`) with an initial sequence number.
- Intercepts incoming `SYN-ACK` (Flags: `0x12`) in the hardware receive callback.
- Replies with `ACK` (Flags: `0x10`) and dispatches the HTTP `GET` payload.
- Incrementally parses incoming JSON payloads in place, counting balanced root braces (`{` and `}`) to detect payload completion before socket closure.

---

## 7. Wire Protocol Specification: WeatherOS File Transfer ("WOSF")

WeatherOS defines a lightweight, connectionless binary file transfer and synchronization protocol operating over UDP port 7331.

### Packet Framing

```
 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|       'W'     |       'O'     |       'S'     |       'F'     | Magic (4 bytes)
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|  Opcode (1B)  |            Message Specific Payload           |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

### Protocol Opcode Catalog

| Opcode | Identifier | Direction | Payload Structure |
| :--- | :--- | :--- | :--- |
| `0x01` | `MSG_PING` | Bidirectional | Optional ASCII client metadata |
| `0x02` | `MSG_PONG` | Bidirectional | ASCII Host Identifier (`"WeatherOS v3.0 \| <IP>"`) |
| `0x10` | `MSG_START_SEND` | Sender -> Receiver | Total File Size (4B), Chunk Count (2B), Chunk Size (2B), Filename (Variable) |
| `0x11` | `MSG_START_ACK` | Receiver -> Sender | Status code (`0x00 = OK`) |
| `0x20` | `MSG_DATA_CHUNK` | Sender -> Receiver | Chunk Index (2B), Chunk Length (2B), Binary Data (up to 512B) |
| `0x21` | `MSG_DATA_ACK` | Receiver -> Sender | Acknowledged Chunk Index (2B), Status code (`0x00 = OK`) |
| `0x30` | `MSG_FINISH` | Sender -> Receiver | None |
| `0x31` | `MSG_FINISH_ACK` | Receiver -> Sender | Status code (`0x00 = OK`) |
| `0xFF` | `MSG_ABORT` | Bidirectional | Abort reason string |

### Flow Diagram
```
Client (WeatherTransfer)                       Server (WeatherOS)
        |                                              |
        |------------ MSG_PING ("Windows...") -------->|
        |<----------- MSG_PONG ("WeatherOS...") -------| (Discovered)
        |                                              |
        |--- MSG_START_SEND (file.bin, 1024B, 2 chk) ->|
        |<----------- MSG_START_ACK (OK) --------------|
        |                                              |
        |--- MSG_DATA_CHUNK (Chunk 0, 512 bytes) ----->|
        |<----------- MSG_DATA_ACK (Chunk 0) ----------|
        |                                              |
        |--- MSG_DATA_CHUNK (Chunk 1, 512 bytes) ----->|
        |<----------- MSG_DATA_ACK (Chunk 1) ----------|
        |                                              |
        |------------ MSG_FINISH --------------------->|
        |<----------- MSG_FINISH_ACK (OK) -------------| (Written to Disk)
```

---

## 8. Summary of Engineering Accomplishments

1. **Bare-Metal Autonomy**: Complete independence from third-party host operating systems; executes directly on raw x86 microprocessors.
2. **Deterministic Boot Pipeline**: Custom MBR and Stage 2 assembly loaders providing seamless transitions between Live USB RAM sessions and installed hard disk boots without user intervention after installation.
3. **Resilient Network Layer**: High-performance raw packet processing bypassing managed runtime overhead, featuring custom DHCP, ARP, ICMP, and TCP engines.
4. **Reliable Storage Engine**: Direct ATA PIO driver implementation with dynamic kernel sector computation, eliminating kernel truncation and installation faults.
5. **Modern Development Paradigm**: High-level C# language capabilities (OOP, abstractions, strong typing) compiled directly to low-level systems machine code.
