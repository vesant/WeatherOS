import os
import sys

def patch_asm_file(asm_path):
    if not os.path.exists(asm_path):
        return False
    with open(asm_path, "r", encoding="latin-1") as f:
        content = f.read()

    # Determine newline style
    nl = "\r\n" if "\r\n" in content else "\n"

    # 1. Align Kernel_Start to 16 bytes and ensure Multiboot2 mInitialized is cleanly reset to 0
    if "align 16" + nl + "global Kernel_Start" not in content:
        target1 = "global Kernel_Start" + nl + "Kernel_Start:"
        repl1 = "align 16" + nl + "global Kernel_Start" + nl + "Kernel_Start:"
        if target1 in content:
            content = content.replace(target1, repl1, 1)
            print(f"Patched 'align 16' before Kernel_Start in {asm_path}")

    # Reset Multiboot2 mInitialized flag before Call SystemVoidCosmosCoreMultiboot2Init
    mb2_call_target = (
        "\t\t\tmov dword [MultiBootInfo_Structure], EBX" + nl +
        "\t\t\tCall SystemVoidCosmosCoreMultiboot2Init"
    )
    mb2_call_repl = (
        "\t\t\tmov byte [static_field__Cosmos_Core_Multiboot2_mInitialized], 0" + nl +
        "\t\t\tmov dword [MultiBootInfo_Structure], EBX" + nl +
        "\t\t\tCall SystemVoidCosmosCoreMultiboot2Init"
    )
    if mb2_call_target in content and "\t\t\tmov byte [static_field__Cosmos_Core_Multiboot2_mInitialized], 0" not in content:
        content = content.replace(mb2_call_target, mb2_call_repl, 1)
        print("Patched Multiboot2Init call with clean mInitialized reset!")

    # 2. Enhanced SystemExceptionOccurred to print fault details to VGA row 1
    ex_target = "SystemExceptionOccurred:" + nl + "\tret"
    ex_repl = (
        "SystemExceptionOccurred:" + nl +
        "\tpushad" + nl +
        "\tmov edi, 0x000B80A0" + nl +
        "\tmov word [edi], 0x4F45" + nl +       # 'E'
        "\tmov word [edi + 2], 0x4F58" + nl +   # 'X'
        "\tmov word [edi + 4], 0x4F3A" + nl +   # ':'
        "\tmov word [edi + 6], 0x4F20" + nl +   # ' '
        "\tadd edi, 8" + nl +
        "\tmov edx, [INTs_LastKnownAddress]" + nl +
        "\tmov ecx, 8" + nl +
        ".hex_loop:" + nl +
        "\trol edx, 4" + nl +
        "\tmov al, dl" + nl +
        "\tand al, 0x0F" + nl +
        "\tadd al, '0'" + nl +
        "\tcmp al, '9'" + nl +
        "\tjbe .hex_char" + nl +
        "\tadd al, 7" + nl +
        ".hex_char:" + nl +
        "\tmov ah, 0x4F" + nl +
        "\tmov [edi], ax" + nl +
        "\tadd edi, 2" + nl +
        "\tdec ecx" + nl +
        "\tjnz .hex_loop" + nl +
        "\tpopad" + nl +
        "\tret"
    )
    if ex_target in content:
        content = content.replace(ex_target, ex_repl, 1)
        print("Injected VGA exception printer in SystemExceptionOccurred!")

    # 3. Comprehensive visual VGA breadcrumbs along row 0
    # GD (0x0C) and PI (0x10) are placed in Kernel_Start by make_isohybrid.py
    breadcrumbs = [
        ("SystemVoidCosmosCoreGCImplementationInit:" + nl, "SystemVoidCosmosCoreGCImplementationInit:" + nl + "\tmov dword [0x000B8014], 0x0F300F47" + nl), # 'G','0'
        ("SystemVoidCosmosCoreGCImplementationInit.IL_0010:" + nl, "SystemVoidCosmosCoreGCImplementationInit.IL_0010:" + nl + "\tmov dword [0x000B8018], 0x0F310F47" + nl), # 'G','1'
        ("SystemVoidCosmosCoreGCImplementationInit.IL_0016:" + nl, "SystemVoidCosmosCoreGCImplementationInit.IL_0016:" + nl + "\tmov dword [0x000B801C], 0x0F320F47" + nl), # 'G','2'
        ("SystemVoidCosmosCoreGCImplementationInit.IL_001B:" + nl, "SystemVoidCosmosCoreGCImplementationInit.IL_001B:" + nl + "\tmov dword [0x000B8020], 0x0F330F47" + nl), # 'G','3'
        ("SystemVoidCosmosCoreGCImplementationInit.IL_010E:" + nl, "SystemVoidCosmosCoreGCImplementationInit.IL_010E:" + nl + "\tmov dword [0x000B8024], 0x0F340F47" + nl), # 'G','4'
        ("SystemVoidCosmosCoreMemoryRATInitSystemBytepointerSystemUInt32:" + nl, "SystemVoidCosmosCoreMemoryRATInitSystemBytepointerSystemUInt32:" + nl + "\tmov dword [0x000B8028], 0x0F300F52" + nl), # 'R','0'
        ("SystemVoidCosmosCoreMemoryRATInitSystemBytepointerSystemUInt32.IL_0116:" + nl, "SystemVoidCosmosCoreMemoryRATInitSystemBytepointerSystemUInt32.IL_0116:" + nl + "\tmov dword [0x000B802C], 0x0F310F52" + nl), # 'R','1'
        ("SystemVoidCosmosCoreMemoryHeapInit:" + nl, "SystemVoidCosmosCoreMemoryHeapInit:" + nl + "\tmov dword [0x000B8030], 0x0F300F48" + nl), # 'H','0'
        ("SystemVoidCosmosCoreMemoryHeapInit.IL_000C:" + nl, "SystemVoidCosmosCoreMemoryHeapInit.IL_000C:" + nl + "\tmov dword [0x000B8034], 0x0F310F48" + nl), # 'H','1'
        ("SystemVoidCosmosCoreMemoryHeapInit.IL_0012:" + nl, "SystemVoidCosmosCoreMemoryHeapInit.IL_0012:" + nl + "\tmov dword [0x000B8038], 0x0F320F48" + nl), # 'H','2'
        ("SystemVoidCosmosCoreMemoryHeapInit.IL_0017:" + nl, "SystemVoidCosmosCoreMemoryHeapInit.IL_0017:" + nl + "\tmov dword [0x000B803C], 0x0F330F48" + nl), # 'H','3'
        ("SystemVoidCosmosCoreMemoryRATInitSystemBytepointerSystemUInt32.IL_011B:" + nl, "SystemVoidCosmosCoreMemoryRATInitSystemBytepointerSystemUInt32.IL_011B:" + nl + "\tmov dword [0x000B8040], 0x0F320F52" + nl), # 'R','2'
        ("SystemVoidCosmosCoreGCImplementationInit.IL_0114:" + nl, "SystemVoidCosmosCoreGCImplementationInit.IL_0114:" + nl + "\tmov dword [0x000B8044], 0x0F350F47" + nl), # 'G','5'
        ("__ENGINE_ENTRYPOINT__:" + nl, "__ENGINE_ENTRYPOINT__:" + nl + "\tmov dword [0x000B8048], 0x0F300F45" + nl), # 'E','0'
        ("___INIT__VMT__CODE____:" + nl, "___INIT__VMT__CODE____:" + nl + "\tmov dword [0x000B804C], 0x0F300F56" + nl), # 'V','0'
        ("_END_OF____INIT__VMT__CODE____:" + nl, "_END_OF____INIT__VMT__CODE____:" + nl + "\tmov dword [0x000B8050], 0x0F4D0F56" + nl), # 'V','M'
        ("___INIT__STRINGS_TYPE_ID_S___:" + nl, "___INIT__STRINGS_TYPE_ID_S___:" + nl + "\tmov dword [0x000B8054], 0x0F540F53" + nl), # 'S','T'
        ("__ENGINE_ENTRYPOINT__.CreateEntrypoint.AfterAlloc:" + nl, "__ENGINE_ENTRYPOINT__.CreateEntrypoint.AfterAlloc:" + nl + "\tmov dword [0x000B8058], 0x0F4C0F41" + nl), # 'A','L'
        ("__ENGINE_ENTRYPOINT__.CallStart:" + nl, "__ENGINE_ENTRYPOINT__.CallStart:" + nl + "\tmov dword [0x000B805C], 0x0F430F4B" + nl), # 'K','C'
        ("SystemVoidCosmosSystemKernelStart:" + nl, "SystemVoidCosmosSystemKernelStart:" + nl + "\tmov dword [0x000B8060], 0x0F530F4B" + nl), # 'K','S'
        ("SystemVoidCosmosHALBootstrapInit:" + nl, "SystemVoidCosmosHALBootstrapInit:" + nl + "\tmov dword [0x000B8064], 0x0F420F48" + nl), # 'H','B'
        ("SystemVoidWeatherOSKernelOnBoot:" + nl, "SystemVoidWeatherOSKernelOnBoot:" + nl + "\tmov dword [0x000B8068], 0x0F420F4F" + nl), # 'O','B'
        ("SystemVoidWeatherOSKernelOnBoot.IL_000F:" + nl, "SystemVoidWeatherOSKernelOnBoot.IL_000F:" + nl + "\tmov dword [0x000B806C], 0x0F490F47" + nl), # 'G','I'
        ("SystemVoidWeatherOSKernelBeforeRun:" + nl, "SystemVoidWeatherOSKernelBeforeRun:" + nl + "\tmov dword [0x000B8070], 0x0F520F42" + nl), # 'B','R'
    ]

    for target, repl in breadcrumbs:
        if target in content and repl not in content:
            content = content.replace(target, repl, 1)

    # 4. Patch RAT.AllocPages to zero-initialize every allocated page with null pointer safety
    rat_target = (
        "\t\tSystemVoidpointerCosmosCoreMemoryRATAllocPagesCosmosCoreMemoryRATPageTypeSystemUInt32.IL_00C6:" + nl +
        "\t\t\t; Stloc" + nl +
        "\t\t\t; Local type = System.Byte*" + nl +
        "\t\t\t; Local EBP offset = 56" + nl +
        "\t\t\t; Local size = 4" + nl +
        "\t\t\tpop dword EAX" + nl +
        "\t\t\tmov dword [EBP - 56], EAX"
    )
    rat_repl = (
        rat_target + nl +
        "\t\t\tpushad" + nl +
        "\t\t\tmov edi, dword [EBP - 56]" + nl +
        "\t\t\ttest edi, edi" + nl +
        "\t\t\tjz .skip_zero_page" + nl +
        "\t\t\tmov ecx, dword [EBP + 8]" + nl +
        "\t\t\tshl ecx, 10" + nl +
        "\t\t\txor eax, eax" + nl +
        "\t\t\tcld" + nl +
        "\t\t\trep stosd" + nl +
        ".skip_zero_page:" + nl +
        "\t\t\tpopad"
    )
    if rat_target in content and ".skip_zero_page:" not in content:
        # If older version without null check was present, replace it
        old_rat_repl = (
            rat_target + nl +
            "\t\t\tpushad" + nl +
            "\t\t\tmov edi, dword [EBP - 56]" + nl +
            "\t\t\tmov ecx, dword [EBP + 8]" + nl +
            "\t\t\tshl ecx, 10" + nl +
            "\t\t\txor eax, eax" + nl +
            "\t\t\tcld" + nl +
            "\t\t\trep stosd" + nl +
            "\t\t\tpopad"
        )
        if old_rat_repl in content:
            content = content.replace(old_rat_repl, rat_repl, 1)
        else:
            content = content.replace(rat_target, rat_repl, 1)
        print("Patched RAT.AllocPages with safe zero-initialization of allocated pages!")

    # 5. Patch CPU.GetLargestMemoryBlock to correctly point to start of entries (map + 16)
    cpu_start_marker = "\t\tCosmosCoreRawMemoryMapBlockpointerCosmosCoreCPUGetLargestMemoryBlock.IL_001E:"
    cpu_end_marker = "mov dword [EBP - 4], EAX"
    idx1 = content.find(cpu_start_marker)
    if idx1 != -1:
        idx2 = content.find(cpu_end_marker, idx1)
        if idx2 != -1 and idx2 - idx1 < 2000:
            old_block = content[idx1 : idx2 + len(cpu_end_marker)]
            new_block = (
                "\t\tCosmosCoreRawMemoryMapBlockpointerCosmosCoreCPUGetLargestMemoryBlock.IL_001E:" + nl +
                "\t\t\tpop dword EAX" + nl +
                "\t\t\tadd EAX, 16" + nl +
                "\t\t\tmov dword [EBP - 4], EAX"
            )
            content = content.replace(old_block, new_block, 1)
            print("Patched CPU.GetLargestMemoryBlock entries offset (map + 16)!")

    # 5.5. Patch GCImplementation.Init IL_0001 to ensure memory manager and static constructor state
    # are completely sanitized and memory discovery runs unconditionally on cold/warm boot!
    smm_repl = (
        "\t\tSystemVoidCosmosCoreGCImplementationInit.IL_0001.AfterCCTorExceptionCheck:" + nl +
        "\t\t\tmov byte [static_field__Cosmos_Core_GCImplementation_StartedMemoryManager], 0" + nl +
        "\t\t\tmov dword [static_field__Cosmos_Core_Memory_RAT_mRAT], 0" + nl +
        "\t\t\tmov byte [CCTOR_CALLED__Cosmos_Core_GCImplementation], 0" + nl +
        "\t\t\tmov byte [CCTOR_CALLED__Cosmos_Core_VTablesImpl], 0" + nl +
        "\t\t\tmov byte [static_field__Cosmos_Core_Multiboot2_mInitialized], 0" + nl +
        "\t\t\tmov EAX, 0x0" + nl +
        "\t\t\tpush dword EAX"
    )
    smm_check_orig = (
        "\t\tSystemVoidCosmosCoreGCImplementationInit.IL_0001.AfterCCTorExceptionCheck:" + nl +
        "\t\t\tmov EAX, 0x0" + nl +
        "\t\t\tmov AL, byte [static_field__Cosmos_Core_GCImplementation_StartedMemoryManager]" + nl +
        "\t\t\tpush dword EAX"
    )
    smm_check_prev = (
        "\t\tSystemVoidCosmosCoreGCImplementationInit.IL_0001.AfterCCTorExceptionCheck:" + nl +
        "\t\t\tmov EAX, 0x0" + nl +
        "\t\t\tcmp dword [static_field__Cosmos_Core_Memory_RAT_mRAT], 0" + nl +
        "\t\t\tje .force_init_mem" + nl +
        "\t\t\tmov AL, byte [static_field__Cosmos_Core_GCImplementation_StartedMemoryManager]" + nl +
        ".force_init_mem:" + nl +
        "\t\t\tpush dword EAX"
    )
    if smm_check_orig in content:
        content = content.replace(smm_check_orig, smm_repl, 1)
        print("Patched GCImplementation.Init IL_0001 with unconditional memory state sanitization!")
    elif smm_check_prev in content:
        content = content.replace(smm_check_prev, smm_repl, 1)
        print("Upgraded GCImplementation.Init IL_0001 with unconditional memory state sanitization!")

    # 6. Patch GCImplementation.Init to cap memLength to 128 MB (0x08000000)
    gc_target = (
        "\t\tSystemVoidCosmosCoreGCImplementationInit.IL_010D:" + nl +
        "\t\t\t; Conv_U4" + nl +
        "\t\t\tpop dword EAX" + nl +
        "\t\t\tadd ESP, 0x4" + nl +
        "\t\t\tpush dword EAX" + nl +
        nl +
        "\t\tSystemVoidCosmosCoreGCImplementationInit.IL_010E:" + nl +
        "\t\t\t; Call" + nl +
        "\t\t\tCall SystemVoidCosmosCoreMemoryRATInitSystemBytepointerSystemUInt32"
    )
    gc_repl = (
        "\t\tSystemVoidCosmosCoreGCImplementationInit.IL_010D:" + nl +
        "\t\t\t; Conv_U4" + nl +
        "\t\t\tpop dword EAX" + nl +
        "\t\t\tadd ESP, 0x4" + nl +
        "\t\t\tcmp EAX, 0x08000000" + nl +
        "\t\t\tjbe .safe_mem_len" + nl +
        "\t\t\tmov EAX, 0x08000000" + nl +
        "\t\t\tmov dword [static_field__Cosmos_Core_GCImplementation_memLength], EAX" + nl +
        "\t\t\tmov dword [static_field__Cosmos_Core_GCImplementation_memLength + 4], 0" + nl +
        ".safe_mem_len:" + nl +
        "\t\t\tpush dword EAX" + nl +
        nl +
        "\t\tSystemVoidCosmosCoreGCImplementationInit.IL_010E:" + nl +
        "\t\t\t; Call" + nl +
        "\t\t\tCall SystemVoidCosmosCoreMemoryRATInitSystemBytepointerSystemUInt32"
    )
    if gc_target in content:
        content = content.replace(gc_target, gc_repl, 1)
        print("Patched GCImplementation.Init with 128 MB safe heap cap!")

    # 7. Patch HeapSmall.Alloc segment reload and null-pointer check at IL_0046
    hs_target = (
        "\t\tSystemBytepointerCosmosCoreMemoryHeapSmallAllocSystemUInt16.IL_0046:" + nl +
        "\t\t\t; Ldfld" + nl +
        "\t\t\t; Field: System.Byte* Cosmos.Core.Memory.SMTBlock.PagePtr" + nl +
        "\t\t\t; Type: System.Byte*" + nl +
        "\t\t\t; Size: 4" + nl +
        "\t\t\t; DeclaringType: Cosmos.Core.Memory.SMTBlock" + nl +
        "\t\t\t; TypeOnStack: Cosmos.Core.Memory.SMTBlock*" + nl +
        "\t\t\t; Offset: 0 (includes object header)" + nl +
        "\t\t\tpop dword ECX" + nl +
        "\t\t\tadd ECX, 0x0" + nl +
        "\t\t\tmov EAX, dword [ECX]"
    )
    hs_repl = (
        "\t\tSystemBytepointerCosmosCoreMemoryHeapSmallAllocSystemUInt16.IL_0046:" + nl +
        "\t\t\t; Ldfld" + nl +
        "\t\t\t; Field: System.Byte* Cosmos.Core.Memory.SMTBlock.PagePtr" + nl +
        "\t\t\t; Type: System.Byte*" + nl +
        "\t\t\t; Size: 4" + nl +
        "\t\t\t; DeclaringType: Cosmos.Core.Memory.SMTBlock" + nl +
        "\t\t\t; TypeOnStack: Cosmos.Core.Memory.SMTBlock*" + nl +
        "\t\t\t; Offset: 0 (includes object header)" + nl +
        "\t\t\tpop dword ECX" + nl +
        "\t\t\tadd ECX, 0x0" + nl +
        "\t\t\tmov ax, 0x10" + nl +
        "\t\t\tmov ds, ax" + nl +
        "\t\t\tmov es, ax" + nl +
        "\t\t\ttest ECX, ECX" + nl +
        "\t\t\tjnz .smt_valid_alloc" + nl +
        "\t\t\tmov edi, 0x000B8140" + nl +
        "\t\t\tmov dword [edi], 0x4F4D4F4F" + nl +      # 'O','O'
        "\t\t\tmov dword [edi + 4], 0x4F3A4F4D" + nl +  # 'M',':'
        "\t\t\tmov dword [edi + 8], 0x4F534F48" + nl +  # 'H','S'
        "\t\t\tcli" + nl +
        "\t\t\thlt" + nl +
        ".smt_valid_alloc:" + nl +
        "\t\t\tmov EAX, dword [ECX]"
    )
    if hs_target in content:
        content = content.replace(hs_target, hs_repl, 1)
        print("Patched HeapSmall.Alloc with segment reload and null check safety!")
    elif "mov ax, 0x10" not in content and ".smt_valid_alloc:" in content:
        old_hs = "add ECX, 0x0" + nl + "\t\t\ttest ECX, ECX"
        new_hs = "add ECX, 0x0" + nl + "\t\t\tmov ax, 0x10" + nl + "\t\t\tmov ds, ax" + nl + "\t\t\tmov es, ax" + nl + "\t\t\ttest ECX, ECX"
        content = content.replace(old_hs, new_hs, 1)
        print("Upgraded HeapSmall.Alloc with segment reload!")

    with open(asm_path, "w", encoding="latin-1") as f:
        f.write(content)
    print(f"Completed diagnostic patching for {asm_path}")
    return True

if __name__ == "__main__":
    if len(sys.argv) > 1:
        patch_asm_file(sys.argv[1])
    else:
        candidates = [
            r"c:\Users\admin\Documents\personal projects\WeatherOS\WeatherOS\WeatherOS\bin\cosmos\Debug\net6.0\WeatherOS.asm",
            r"c:\Users\admin\Documents\personal projects\WeatherOS\WeatherOS\bin\cosmos\Debug\net6.0\WeatherOS.asm",
        ]
        for c in candidates:
            if os.path.exists(c):
                patch_asm_file(c)
