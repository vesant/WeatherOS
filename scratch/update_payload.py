import os
import sys
import struct
import subprocess
import shutil

root_dir = r"c:\Users\admin\Documents\personal projects\WeatherOS"
weatheros_dir = os.path.join(root_dir, "WeatherOS")
mbr_asm = os.path.join(weatheros_dir, "custom_mbr.asm") if os.path.exists(os.path.join(weatheros_dir, "custom_mbr.asm")) else os.path.join(root_dir, "custom_mbr.asm")
stage2_asm = os.path.join(weatheros_dir, "custom_stage2.asm") if os.path.exists(os.path.join(weatheros_dir, "custom_stage2.asm")) else os.path.join(root_dir, "custom_stage2.asm")
mbr_bin = os.path.join(root_dir, "custom_mbr.bin")
stage2_bin = os.path.join(root_dir, "custom_stage2.bin")
payload_cs = os.path.join(weatheros_dir, "WeatherOS", "Services", "NativeBootloaderPayload.cs")

# Locate YASM compiler
yasm_candidates = [
    r"C:\Users\admin\AppData\Roaming\Cosmos User Kit\Build\Tools\Yasm\yasm.exe",
    r"C:\Users\admin\.nuget\packages\cosmos.build\0.1.0-localbuild20221121055958\tools\yasm\win\yasm.exe",
]
yasm_path = None
for yc in yasm_candidates:
    if os.path.exists(yc):
        yasm_path = yc
        break
if not yasm_path:
    yasm_path = shutil.which("yasm")

if yasm_path and os.path.exists(yasm_path):
    # Check if we can patch rem_sectors and entry_point from latest WeatherOS.bin
    bin_candidates = [
        os.path.join(weatheros_dir, "WeatherOS", "bin", "cosmos", "Debug", "net6.0", "WeatherOS.bin"),
        os.path.join(weatheros_dir, "WeatherOS", "bin", "Debug", "net6.0", "WeatherOS.bin"),
        os.path.join(weatheros_dir, "bin", "cosmos", "Debug", "net6.0", "WeatherOS.bin"),
        os.path.join(weatheros_dir, "bin", "Debug", "net6.0", "WeatherOS.bin"),
    ]
    existing_candidates = [c for c in bin_candidates if os.path.exists(c)]
    if existing_candidates:
        existing_candidates.sort(key=lambda x: os.path.getmtime(x), reverse=True)
        with open(existing_candidates[0], "rb") as f:
            elf_data = f.read()
        e_entry = struct.unpack("<I", elf_data[24:28])[0]
        e_phoff = struct.unpack("<I", elf_data[28:32])[0]
        e_phnum = struct.unpack("<H", elf_data[44:46])[0]
        for i in range(e_phnum):
            ph = elf_data[e_phoff + i * 32 : e_phoff + (i + 1) * 32]
            p_type, poff, pvaddr, ppaddr, pfilesz, pmemsz, pflags, palign = struct.unpack("<IIIIIIII", ph)
            if p_type == 1:
                rem_sectors = (pfilesz + 511) // 512
                # Patch custom_stage2.asm
                for s2_file in [stage2_asm, os.path.join(root_dir, "custom_stage2.asm")]:
                    if os.path.exists(s2_file):
                        with open(s2_file, "r", encoding="utf-8") as sf:
                            s2_content = sf.read()
                        import re
                        s2_content = re.sub(r'rem_sectors dd \d+', f'rem_sectors dd {rem_sectors}', s2_content)
                        s2_content = re.sub(r'entry_point dd 0x[0-9a-fA-F]+', f'entry_point dd {hex(e_entry)}', s2_content)
                        with open(s2_file, "w", encoding="utf-8") as sf:
                            sf.write(s2_content)
                break

    subprocess.run([yasm_path, "-f", "bin", "-o", mbr_bin, mbr_asm], check=True)
    subprocess.run([yasm_path, "-f", "bin", "-o", stage2_bin, stage2_asm], check=True)

with open(mbr_bin, "rb") as f:
    mbr_bytes = bytearray(f.read())

mbr_bytes[450] = 0x83 # Linux / OS native raw partition
if 'rem_sectors' in locals() and rem_sectors:
    struct.pack_into("<I", mbr_bytes, 458, rem_sectors)

with open(stage2_bin, "rb") as f:
    stage2_bytes = bytearray(f.read())

if len(stage2_bytes) < 4096:
    stage2_bytes.extend(b'\x00' * (4096 - len(stage2_bytes)))
stage2_bytes = stage2_bytes[:4096]

def format_csharp_bytes(byte_data, var_name, indent=12):
    lines = [f"{' ' * indent}public static readonly byte[] {var_name} = new byte[]\n{' ' * indent}{{"]
    for i in range(0, len(byte_data), 16):
        chunk = byte_data[i:i+16]
        hex_str = ", ".join(f"0x{b:02X}" for b in chunk)
        lines.append(f"{' ' * (indent + 4)}{hex_str},")
    lines.append(f"{' ' * indent}}};")
    return "\n".join(lines)

cs_mbr = format_csharp_bytes(mbr_bytes, "Mbr", 8)
cs_s2 = format_csharp_bytes(stage2_bytes, "Stage2", 8)

content = f"""using System;

namespace WeatherOS.Services
{{
    /// <summary>
    /// Custom Bare-Metal Bootloader Payload (MBR + Stage 2 Direct Kernel Loader).
    /// Bypasses GRUB completely to eliminate filesystem/module errors and boots WeatherOS directly in 0.2s.
    /// </summary>
    public static class NativeBootloaderPayload
    {{
{cs_mbr}

{cs_s2}
    }}
}}
"""

with open(payload_cs, "w", encoding="utf-8") as f:
    f.write(content)

print(f"Updated {payload_cs} with latest MBR ({len(mbr_bytes)} bytes) and Stage2 ({len(stage2_bytes)} bytes).")
