# WeatherOS 🌦️

WeatherOS is an autonomous, dedicated x86 Operating System built on top of the **Cosmos** (C# Open Source Managed Operating System) framework, specialized in meteorological calculations, environmental telemetry simulation, and visualization.

It is designed to run directly on standard PC hardware (bare-metal) and inside QEMU, functioning as an independent workstation without requiring any external microcontroller to operate.

---

## 🏗️ Architecture Overview

The system is structured into modular subsystems adhering to Cosmos kernel design patterns and strict Garbage Collector (GC) safety:

- **`Kernel.cs`**: The operating system entry point. Handles `BeforeRun()` PC hardware discovery (Cosmos VFS on storage disk, DHCP network stack via standard PC NIC) and executes the main `Run()` loop managing state transition between CLI Terminal and Graphical Dashboard.
- **`Graphics/WeatherGuiRenderer.cs`**: High-performance VBE Canvas subsystem (800x600x32). Pre-allocates all GDI primitives (`Pen`, `Color`, `Mode`, `Font`) to ensure zero heap allocations during the render loop.
- **`Services/WeatherService.cs`**: Standalone meteorological observation and calculation engine. Computes environmental metrics natively on the PC CPU (temperature, humidity, atmospheric pressure, dew point, wind vectors).
- **`Services/SerialSensorService.cs`**: Non-blocking hardware serial diagnostic driver wrapping `Cosmos.HAL.SerialPort` (COM1-COM4). Serves as a future expansion slot for when external sensors or microcontrollers (like an Arduino Uno Q) are introduced.

---

## ⌨️ Shell Commands (Terminal Mode)

- `help`: Lists all available commands with usage descriptions.
- `clear`: Clears the screen and re-prints the WeatherOS banner and PC subsystem statuses.
- `ipconfig`: Displays the current IPv4 network configuration and DHCP status.
- `weather`: Displays real-time meteorological observations and calculations in text mode.
- `serial-test`: Diagnostic check for the PC's COM1 UART port (optional future sensor interface).
- `gui`: Switches into the VBE Graphical Dashboard.
- `reboot`: Restarts the computer via ACPI/APM.
- `shutdown`: Gracefully powers off the machine.

---

## 🖥️ Graphical Dashboard (GUI Mode)

Entering `gui` shifts the display into an 800x600 32-bit canvas featuring:
- Visual weather condition card with a procedural sun primitive (yellow circle and rays).
- Real-time telemetry gauge bars for Temperature and Humidity.
- Barometric pressure and wind vectors.
- Exit mechanism: Press **`[ESC]`** or **`[Q]`** to safely restore text mode.

---

## 💻 Standard PC & QEMU Execution

### Standard PC Execution in QEMU:
WeatherOS runs as a standard standalone PC operating system:
```bash
qemu-system-x86_64 -m 512M -cdrom WeatherOS.iso -vga std -net nic,model=rtl8139 -net user
```

### Future Sensor Expansion (Optional):
If in the future an Arduino Uno Q or RS-232 telemetry feed is connected to the PC:
```bash
# Optional serial redirection to hardware COM port:
qemu-system-x86_64 -m 512M -cdrom WeatherOS.iso -serial /dev/ttyUSB0
```
