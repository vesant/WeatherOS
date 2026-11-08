# [Update: Fullscreen World Weather Map Layers & Doppler Radar Suite](https://github.com/vesant/WeatherOS/releases/tag/32xUpdate_03)
[vesant](https://github.com/vesant) released this  
[32xUpdate_03](https://github.com/vesant/WeatherOS/tree/32xUpdate_03)  
[b9475e4](https://github.com/vesant/WeatherOS/commit/b9475e4f9b8c028a47470f1a9657b545dbd4e68e)

WeatherOS Cumulative System Update v3.4.0 (Release Package)  
**Package File**: `WeatherOS-Update-MapLayers-v3.4.0.pkg`  
**Target Architecture**: x86 (IA-32, 32-bit Bare-Metal Protected Mode)  
**Binary Size**: 9,038,848 bytes (~8.62 MB)  
**Checksum (CRC32)**: `0x7E3F1A92`  
**Checksum (SHA256)**: `8A1C50E934F89D771343B8018DA3129B8544AC1E2479207865181E99908F0024`  
**Release Date**: November 8, 2026  
**Target Slot**: LBA 64 (`0:\boot\WeatherOS.bin`) & Boot Sectors LBA 0–8  

---

### 1. Overview & Scope
The **v3.4.0 Cumulative Update** introduces the dedicated multi-layer synoptic visualization engine to the Fullscreen World Weather Map, featuring an animated 16-frame looping High-Resolution Doppler Weather Radar, refined thermal isotherm heatmaps, and enhanced precipitation echo modeling. 

Additionally, this release integrates a complete bare-metal IEEE 802.11i WPA2-CCMP (AES-128 CTR/CBC-MAC) software cryptographic pipeline, standardizes the UI navigation modal (`[F1]` Commands Popup), and replaces synthetic telemetry fallback substitutions with truthful network reachability diagnostics.

---

### 2. Key Features & Subsystem Upgrades

#### 2.1. Multi-Layer Synoptic Meteorological Engine (`worldmap`)
- **Layer 1: Standard Continental Topography:** High-fidelity ASCII/ANSI coastal geometries and national boundaries for Europe, the Americas, and global sectors, eliminating coarse text truncation.
- **Layer 2: Thermal Heatmap Isotherms:** Continuous temperature field representation rendering realistic temperature gradients:
  - Blue (`[:] <= 10°C` Cold)
  - Yellow (`[#] 11–18°C` Mild)
  - Orange (`[=] 19–25°C` Warm)
  - Red (`[*] >= 26°C` Hot)
- **Layer 3: Precipitation Radar (Doppler Cloud Cover & Rain Echoes):** Dynamically scales from clear conditions through scattered cloud cover (`.`), stratified rain echoes (`/`), to intense convective cores (`*`).
- **Layer 4: Atmospheric Pressure & Isobars:** High/Low barometric pressure systems mapped with millibar gradations.
- **Layer 5: Animated Doppler Weather Radar (Looping Reflectivity):**
  - High-resolution, multi-level radar reflectivity modeled after NEXRAD Level-II base reflectivity products.
  - Realistic convective squall line and mesocyclone hook echo rotation with dBZ gradations:
    - `[ 120dBZ ]` Light Green
    - `[ 132dBZ ]` Dark Green
    - `[ 142dBZ ]` Amber / Yellow
    - `[ 152dBZ ]` Crimson / Red Core
    - `[ 160dBZ+ ]` Magenta Convective Center
  - 16-frame continuous loop running with 0 heap allocations per tick, fully protecting low-memory x86 bare-metal heaps from garbage collection latency.

#### 2.2. Clean Navigation & Centralized Shortcut Modal
- **Dynamic Help Popup (`[F1]`):** Consolidated all shortcuts into a unified on-screen modal window, keeping the map footer uncluttered and standardized across all sectors:
  - `[Tab]` / `[C]`: Switch Continental Sector
  - `[L]`: Cycle Synoptic Map Layer (1–5)
  - `[<]` / `[>]`: Select Weather Station
  - `[Enter]`: Launch Station in Detailed Dashboard
  - `[R]`: Force Telemetry Refresh
  - `[ESC]`: Exit to Desktop Workspace

---

### 3. Engineering Analysis: Bare-Metal WPA2-CCMP & Telemetry Integrity

#### 3.1. IEEE 802.11i Software CCMP Engine (`PureCcmp` & `PureAes128`)
Physical testing on netbook hardware (Toshiba NB100 / Intel Atom N270) identified that modern Wi-Fi routers drop unencrypted 802.11 data frames when connected to WPA2-PSK networks. Furthermore, Atheros AR2425 silicon suffers from documented hardware key table lookup faults on legacy PCI chipsets, corrupting RX descriptors when hardware decryption is engaged.

```text
+-----------------------------------------------------------------------------------+
|               WEATHEROS v3.4.0 SOFTWARE WPA2-CCMP CRYPTO PIPELINE                 |
+-----------------------------------------------------------------------------------+
| [1/4] Derivation of 16-byte Temporal Key (TK) via 4-Way EAPOL Handshake           |
| [2/4] Transmit: IEEE 802.11 Encapsulation -> CCMP Header -> AES-CTR -> CBC-MAC MIC |
| [3/4] Receive: Atheros `nohwcrypt` Mode -> PureCcmp Software Verification & Strip  |
| [4/4] Decapsulation: 802.11 / QoS Data -> 802.3 Ethernet Frame -> Cosmos Stack    |
+-----------------------------------------------------------------------------------+
```

- **FIPS 197 AES-128 Implementation:** Implemented `PureAes128` using 1D flat byte arrays to maintain strict compatibility with Cosmos IL2CPU bare-metal runtime restrictions.
- **Software CCMP Engine:** Injected `PureCcmp.Encrypt` on TX and `PureCcmp.Decrypt` on RX for both standard 802.11 data frames and 802.11e QoS data frames.
- **Hardware Crypto Bypass (`ath5k nohwcrypt`):** Reset hardware key cache entries (`ResetHardwareKey(0)`) and disabled hardware crypto descriptor error filtering (`stat1 & 0x04` PHY CRC check only), eliminating dropped packets on inbound router downlink traffic.

#### 3.2. Truthful Meteorological Telemetry Reporting
Previously, when remote API queries timed out, the system silently substituted hardcoded synoptic baselines (e.g. `Setubal: 16 C, Clear`), producing false "All clear" statuses even during active storm conditions.

- **Non-Deceptive Error Signaling:** On telemetry fetch timeouts or network unreachability, `WeatherService` and `FetchCommand` now explicitly display `[!] TELEMETRY NOTICE: Station unreachable (Network Timeout)` and flag the status in yellow/red, preventing stale or synthetic data from masking real-world weather emergencies.

---

### 4. Package Deployment Instructions

#### Method A: Over-The-Air (OTA) via WeatherTransfer
1. Boot WeatherOS on the target machine and press `[F7]` on the desktop to enter the **OTA Update Receiver**.
2. On your host PC, run `WeatherTransfer.exe`:
   ```cmd
   WeatherTransfer.exe final\Updates\WeatherOS-Update-MapLayers-v3.4.0.pkg
   ```
3. After the transmission completes, press `[ENTER]` on the target machine to flash LBAs 0–17,654.
4. The system will sync platters and reboot cleanly into **WeatherOS v3.4.0**.

#### Method B: Live USB Clean Installation
1. Write `final/WeatherOS.img` or `final/WeatherOS.iso` to a USB drive using Rufus in **DD Image Mode** or `dd`.
2. Boot into the target x86 netbook or PC.
3. Select `[2] Install WeatherOS to Hard Disk` from the main menu.
