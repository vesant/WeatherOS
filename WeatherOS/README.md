# WeatherOS 🌦️

WeatherOS is a dedicated x86 Operating System built on top of the **Cosmos** (C# Open Source Managed Operating System) framework, specialized in meteorological telemetry acquisition, processing, and visualization.

---

## 🏗️ Architecture Overview

The system is structured into modular subsystems adhering to Cosmos kernel design patterns and strict Garbage Collector (GC) safety:

- **`Kernel.cs`**: The operating system entry point. Handles `BeforeRun()` hardware discovery (VFS, DHCP network stack) and executes the main `Run()` loop managing state transition between CLI Terminal and Graphical Dashboard.
- **`Graphics/WeatherGuiRenderer.cs`**: High-performance VBE Canvas subsystem (800x600x32). Pre-allocates all GDI primitives (`Pen`, `Color`, `Mode`, `Font`) to ensure zero heap allocations during the render loop.
- **`Services/WeatherService.cs`**: Telemetry state manager providing meteorological readings (Temperature, Humidity, Pressure, Wind). Prepared for TCP/UDP ingestion.
- **`Services/SerialSensorService.cs`**: Low-level hardware serial driver wrapping `Cosmos.HAL.SerialPort` (COM1-COM4) to read telemetry packets from external microcontrollers (e.g. Arduino Uno Q).

---

## ⌨️ Shell Commands (Terminal Mode)

- `help`: Lists all available commands with usage descriptions.
- `clear`: Clears the screen and re-prints the WeatherOS banner and subsystem statuses.
- `ipconfig`: Displays the current IPv4 network configuration and DHCP status.
- `weather`: Displays real-time meteorological observations in text mode.
- `serial-test`: Initializes COM1 (9600 baud) and listens for incoming frames from the Arduino Uno Q.
- `gui`: Switches into the VBE Graphical Dashboard.
- `reboot`: Restarts the operating system.
- `shutdown`: Gracefully powers off the machine using ACPI/APM.

---

## 🖥️ Graphical Dashboard (GUI Mode)

Entering `gui` shifts the display into an 800x600 32-bit canvas featuring:
- Visual weather condition card with a procedural sun primitive (yellow circle and rays).
- Real-time telemetry gauge bars for Temperature and Humidity.
- Barometric pressure and wind vectors.
- Exit mechanism: Press **`[ESC]`** or **`[Q]`** to safely restore text mode.

---

## 🔌 Hardware & QEMU Integration

### 1. Serial Port with Arduino Uno Q
- In QEMU, forward the serial port to a host pseudo-terminal (PTY) or real USB-Serial device:
  ```bash
  -serial /dev/ttyUSB0
  # or on Windows:
  -serial COM3
  ```
- **Packet Protocol**: Arduino sends newline-terminated ASCII frames:
  ```text
  TEMP:21.5,HUM:58.0,PRES:1014.2,WIND:12.8,DIR:NNW\n
  ```

### 2. Network Stack (QEMU TAP / User Mode)
- QEMU default user network (`-net nic,model=rtl8139 -net user`) supports DHCP automatically.
- WeatherOS initializes the network via `Cosmos.System.Network.IPv4.Config.Enable(true)`.
