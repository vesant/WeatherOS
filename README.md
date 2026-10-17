# WeatherOS

WeatherOS is an independent, dedicated 32-bit x86 operating system developed in C# that boots directly on bare-metal PC hardware. It provides a specialized meteorological workstation environment complete with real-time weather telemetry, lunar tracking, system diagnostics, and local area network synchronization.

Instead of running inside a host operating system, WeatherOS executes directly on x86 processors using the Cosmos kernel framework and an Ahead-Of-Time (AOT) compiler (IL2CPU) that emits pure 32-bit assembly code.

---

## Features

- **Text User Interface (TUI) Desktop Environment**: A clean 80x25 console interface featuring modular windows, status taskbars, real-time weather condition rendering, multi-day forecasts, and dynamic lunar phase calculations.
- **Bare-Metal Network Stack**: A dedicated raw Ethernet and TCP/IP stack that bypasses high-level runtime overhead to communicate directly with PCI network controllers (Realtek RTL8102E, RTL8139, RTL8168), auto-negotiating IP addresses via DHCP and querying meteorological REST APIs.
- **Dual Boot Architecture**: Boots instantly from USB flash drives into a volatile Live RAM session, or can be installed permanently onto an internal hard disk (IDE/SATA) via an integrated partitioning and bootloader installation utility.
- **Custom Stage 1 & Stage 2 Bootloaders**: Written in x86 assembly to transition from 16-bit real mode into 32-bit protected mode, prepare Multiboot compliant memory headers, and execute the C# kernel at a fixed physical memory address.
- **Local Network File Transfer Companion**: Includes `WeatherTransfer`, a Windows command-line and interactive companion utility that auto-discovers WeatherOS on the local network over UDP port 7331 to send and receive configuration files, telemetry logs, and software updates.
- **Integrated System Utilities**: Includes a terminal command shell, Notepad text editor with FAT32 storage persistence, disk diagnostics and zero-fill drive sanitization, hardware battery telemetry, and a scientific calculator.

---

## Deliverables in the "final" Folder

Ready-to-use release binaries are available in the `final/` directory:

- `WeatherOS.iso`: Hybrid bootable ISO image compatible with CD/DVD media, virtual machines (VirtualBox, VMware, QEMU), and USB pen drives.
- `WeatherOS.img`: Raw disk image ready for direct block-level writing (`dd`) to flash drives.
- `WeatherTransfer.exe`: Standalone Windows companion tool for local network file transfers and remote diagnostics.
- `RTL8102E.cs`: The bare-metal Realtek Fast Ethernet PCI controller driver source code.

---

## Quick Start

### 1. Requirements

- **Processor**: Any x86 (IA-32) or x86-64 compatible CPU (Intel Pentium II or newer, AMD Athlon or newer).
- **Memory**: 512 MB RAM recommended (minimum 256 MB).
- **Network**: Wired Ethernet controller (Realtek RTL8102E, RTL8139, or standard virtualized adapters in QEMU/VirtualBox).
- **Storage**: Optional internal IDE/SATA hard drive for permanent installation (minimum 200 MB free space).

### 2. Creating a Bootable USB Flash Drive

#### Using Rufus (Windows)
1. Insert a USB flash drive (minimum 64 MB).
2. Open Rufus and select your target USB drive.
3. Choose `WeatherOS.iso` as the boot selection.
4. Keep partition scheme as **MBR** and target system as **BIOS (or UEFI-CSM)**.
5. Click **START**. If prompted between ISO mode and DD image mode, select **DD Image mode**.

#### Using `dd` (Linux / macOS)
```bash
sudo dd if=WeatherOS.iso of=/dev/sdX bs=4M status=progress conv=fsync
```
*(Replace `/dev/sdX` with your target USB device path).*

### 3. Booting WeatherOS

1. Connect the target PC to your local network using an Ethernet cable.
2. Insert the USB drive and power on the computer.
3. Enter your BIOS/Boot Menu (typically `F12`, `F8`, `F11`, or `Delete`) and select the USB drive.
4. The WeatherOS Boot Menu will appear:
   - **[1] Live OS (RAM Session)**: Boots directly into memory without modifying the internal drive.
   - **[2] Install WeatherOS to Hard Disk**: Launches the guided installer to partition and install the OS permanently onto your internal hard drive.

---

## Desktop Navigation & Keyboard Shortcuts

- **F1**: Help and shortcut reference.
- **F2**: Application launcher menu.
- **F3**: Refresh meteorological telemetry from the network.
- **F4**: Change default city location.
- **F5**: Open 7-day extended weather forecast.
- **F6**: Lunar observation and phase tracker.
- **F7**: File Transfer & OTA synchronizer.
- **F8**: Storage manager and file explorer.
- **F9**: System resources and live memory monitor.
- **F10**: Hardware battery diagnostics.
- **ESC**: Close the active window or return to the desktop.
- **Tab / Arrow Keys**: Cycle between interactive elements and buttons.

---

## Terminal Shell Commands

WeatherOS includes an interactive command interpreter accessible alongside the TUI:

| Command | Description |
| :--- | :--- |
| `help` | Lists all available console commands and registered applications |
| `gui` | Launches or returns to the meteorological graphical desktop |
| `weather` | Prints current weather conditions directly in the console |
| `fetch` | Triggers an immediate HTTP API query to update weather data |
| `setcity <city>` | Sets the target city for weather telemetry |
| `transfer` | Starts the UDP file transfer and network synchronization service |
| `disks` | Scans and lists all detected physical storage devices and partitions |
| `diskdiag` | Performs read tests and sector diagnostics on storage drives |
| `ping <ip>` | Tests network reachability using ICMP echo requests |
| `format` | Formats a target partition with a FAT32 filesystem |
| `wipe` | Securely wipes a physical disk with single-pass zero fills |
| `install` | Launches the interactive hard disk installer |
| `reboot` | Restarts the computer via CPU reset |
| `poweroff` | Shuts down the system via ACPI power management |

---

## License

This project is licensed under the MIT License. See `LICENSE` for details.
