import os
import sys
import struct
import subprocess

def build_native_bootable_image():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    root_dir = os.path.abspath(os.path.join(script_dir, ".."))

    # 1. Locate WeatherOS.bin
    bin_candidates = [
        os.path.join(script_dir, "WeatherOS", "bin", "Debug", "net6.0", "WeatherOS.bin"),
        os.path.join(script_dir, "WeatherOS", "bin", "cosmos", "Debug", "net6.0", "WeatherOS.bin"),
        os.path.join(script_dir, "bin", "Debug", "net6.0", "WeatherOS.bin"),
        os.path.join(script_dir, "bin", "cosmos", "Debug", "net6.0", "WeatherOS.bin"),
    ]
    kernel_bin_path = None
    for c in bin_candidates:
        if os.path.exists(c):
            kernel_bin_path = c
            break

    if not kernel_bin_path:
        print("Error: Could not locate WeatherOS.bin!")
        return False

    print(f"Reading kernel from: {kernel_bin_path}")
    with open(kernel_bin_path, "rb") as f:
        elf_data = f.read()

    # 2. Parse ELF32 header
    e_entry = struct.unpack("<I", elf_data[24:28])[0]
    e_phoff = struct.unpack("<I", elf_data[28:32])[0]
    e_phnum = struct.unpack("<H", elf_data[44:46])[0]

    p_offset = None
    p_filesz = None
    for i in range(e_phnum):
        ph = elf_data[e_phoff + i * 32 : e_phoff + (i + 1) * 32]
        p_type, poff, pvaddr, ppaddr, pfilesz, pmemsz, pflags, palign = struct.unpack("<IIIIIIII", ph)
        if p_type == 1: # PT_LOAD
            p_offset = poff
            p_filesz = pfilesz
            break

    if p_offset is None or p_filesz is None:
        print("Error: Could not find PT_LOAD segment in WeatherOS.bin!")
        return False

    rem_sectors = (p_filesz + 511) // 512
    print(f"Kernel Entry: {hex(e_entry)}, Segment Offset: {p_offset}, Size: {p_filesz} bytes ({rem_sectors} sectors)")

    # 3. Locate YASM compiler
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
        import shutil
        yasm_path = shutil.which("yasm")
    if not yasm_path or not os.path.exists(yasm_path):
        print("Error: yasm.exe not found!")
        return False

    # 4. Assemble custom_mbr.asm and custom_stage2.asm
    mbr_asm = os.path.join(root_dir, "custom_mbr.asm")
    stage2_asm = os.path.join(root_dir, "custom_stage2.asm")

    # Update patchable parameters in custom_stage2.asm
    with open(stage2_asm, "r", encoding="utf-8") as f:
        s2_src = f.read()

    import re
    s2_src = re.sub(r'rem_sectors dd \d+', f'rem_sectors dd {rem_sectors}', s2_src)
    s2_src = re.sub(r'entry_point dd 0x[0-9a-fA-F]+', f'entry_point dd {hex(e_entry)}', s2_src)

    with open(stage2_asm, "w", encoding="utf-8") as f:
        f.write(s2_src)

    mbr_bin = os.path.join(root_dir, "custom_mbr.bin")
    stage2_bin = os.path.join(root_dir, "custom_stage2.bin")

    subprocess.run([yasm_path, "-f", "bin", "-o", mbr_bin, mbr_asm], check=True)
    subprocess.run([yasm_path, "-f", "bin", "-o", stage2_bin, stage2_asm], check=True)

    with open(mbr_bin, "rb") as f:
        mbr_bytes = bytearray(f.read())
    with open(stage2_bin, "rb") as f:
        stage2_bytes = bytearray(f.read())

    # Ensure Stage 2 is padded to exactly 4 sectors (2048 bytes)
    if len(stage2_bytes) < 2048:
        stage2_bytes.extend(b'\x00' * (2048 - len(stage2_bytes)))
    stage2_bytes = stage2_bytes[:2048]

    # Patch partition table entry 1 in MBR to point to LBA 64 and size rem_sectors
    # Offset 446 + 8 = 454 (LBA start), offset 446 + 12 = 458 (LBA count)
    struct.pack_into("<I", mbr_bytes, 454, 64)
    struct.pack_into("<I", mbr_bytes, 458, rem_sectors)

    # Update C# payload for installer
    payload_py = os.path.join(root_dir, "scratch", "update_payload.py")
    if os.path.exists(payload_py):
        subprocess.run([sys.executable, payload_py], check=True)

    # 5. Extract kernel loadable segment and pad to 512-byte boundary
    kernel_segment = bytearray(elf_data[p_offset : p_offset + p_filesz])

    # Apply x86 Bare-Metal Hardware Compatibility Patches:

    # Patch A: Premature STI in DoEnableInterrupts
    # In Cosmos, Heap.Alloc calls CPU.EnableInterrupts() before UpdateIDT has run,
    # enabling interrupts while IDTR is still 0 and PIC is uninitialized,
    # causing a fatal Triple Fault when PIC.Remap writes 0x11 to port 0xA0.
    do_enable_int_pattern = bytes.fromhex("5589e5fbc705fd8d0502")
    pos = kernel_segment.find(do_enable_int_pattern)
    if pos != -1:
        kernel_segment[pos + 3] = 0x90 # Replace 'sti' (0xFB) with 'nop' (0x90)
        print(f"Patched premature STI in DoEnableInterrupts at segment offset {hex(pos)}!")
    else:
        print("Warning: DoEnableInterrupts pattern not found in kernel segment!")

    # Patch B: Destructive CR0 ET clobber in InitSSE
    # In Cosmos.Core_Asm.CPUImpl.InitSSE:
    # IL2CPU generates:
    #   mov eax, cr0; and eax, 0xfffffffd; mov cr0, eax
    #   mov eax, cr0; and eax, 0x1; mov cr0, eax   <-- BUG! Clears CR0.ET (Extension Type bit 4 = 1).
    # Clearing bit 4 in CR0 on real Intel x86/Atom processors causes #GP(0) -> Triple Fault -> Instant Reboot!
    # We replace 'and eax, 1' (83 e0 01) and 'mov cr0, eax' (0f 22 c0) with 6 NOPs (0x90).
    init_sse_pattern = bytes.fromhex("0f20c083e0010f22c0")
    pos_sse = kernel_segment.find(init_sse_pattern)
    if pos_sse != -1:
        kernel_segment[pos_sse + 3 : pos_sse + 9] = b"\x90" * 6
        print(f"Patched destructive CR0 clobber in InitSSE at segment offset {hex(pos_sse)}!")
    else:
        print("Warning: InitSSE CR0 pattern not found in kernel segment!")

    # Patch C: Early STI in UpdateIDT
    # UpdateIDT(true) runs lidt [EAX] and immediately does 'sti' (0xFB).
    # On physical hardware (Atom N270), unmasked timer/PIC interrupts during unaligned stack
    # or early execution cause fxsave #GP(0) -> Triple Fault.
    # Replace 'sti' with 'nop' (0x90) so interrupts remain controlled until kernel main loop.
    update_idt_pattern = bytes.fromhex("0f0118fb")
    pos_idt = kernel_segment.find(update_idt_pattern)
    if pos_idt != -1:
        kernel_segment[pos_idt + 3] = 0x90 # Replace 'sti' with 'nop'
        print(f"Patched early STI in UpdateIDT at segment offset {hex(pos_idt)}!")
    else:
        print("Warning: UpdateIDT STI pattern not found in kernel segment!")

    rem_bytes = (512 - (len(kernel_segment) % 512)) % 512
    if rem_bytes > 0:
        kernel_segment.extend(b'\x00' * rem_bytes)

    # 6. Assemble complete bare-metal disk image:
    # Sector 0: MBR (512 bytes)
    # Sectors 1..4: Stage 2 (2048 bytes)
    # Sectors 5..63: Zero padding (29,696 bytes)
    # Sectors 64..: Kernel loadable segment
    full_image = bytearray()
    full_image.extend(mbr_bytes)
    full_image.extend(stage2_bytes)
    full_image.extend(b'\x00' * (59 * 512)) # Sectors 5..63
    full_image.extend(kernel_segment)

    total_sectors = len(full_image) // 512
    total_mb = len(full_image) / (1024 * 1024)
    print(f"Generated Direct Native Boot Image: {len(full_image)} bytes ({total_sectors} sectors, {total_mb:.2f} MB)")

    # 7. Write to target formats (.iso, .img, .raw)
    targets = [
        os.path.join(script_dir, "WeatherOS.iso"),
        os.path.join(script_dir, "WeatherOS.img"),
        os.path.join(script_dir, "WeatherOS.raw"),
        os.path.join(script_dir, "WeatherOS", "bin", "cosmos", "Debug", "net6.0", "WeatherOS.iso"),
        os.path.join(script_dir, "WeatherOS", "bin", "cosmos", "Debug", "net6.0", "WeatherOS.img"),
        os.path.join(script_dir, "WeatherOS", "bin", "cosmos", "Debug", "net6.0", "WeatherOS.raw"),
        os.path.join(script_dir, "WeatherOS", "bin", "Debug", "net6.0", "WeatherOS.iso"),
        os.path.join(script_dir, "WeatherOS", "bin", "Debug", "net6.0", "WeatherOS.img"),
        os.path.join(script_dir, "WeatherOS", "bin", "Debug", "net6.0", "WeatherOS.raw"),
        os.path.join(root_dir, "WeatherOS.iso"),
        os.path.join(root_dir, "WeatherOS.img"),
        os.path.join(root_dir, "WeatherOS.raw"),
    ]

    for t in targets:
        t_dir = os.path.dirname(t)
        if os.path.exists(t_dir):
            with open(t, "wb") as f:
                f.write(full_image)
            print(f"Wrote image: {t} ({os.path.getsize(t)} bytes)")

    print("\nSUCCESS: Native bare-metal bootloader image successfully created!")
    return True

if __name__ == "__main__":
    build_native_bootable_image()
