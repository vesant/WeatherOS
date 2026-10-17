import os
import sys

def patch_asm_file(asm_path):
    if not os.path.exists(asm_path):
        return False
    with open(asm_path, "r", encoding="latin-1") as f:
        content = f.read()

    # Determine newline style
    nl = "\r\n" if "\r\n" in content else "\n"

    # 1. Align Kernel_Start to 16 bytes
    if "align 16" + nl + "global Kernel_Start" not in content:
        target1 = "global Kernel_Start" + nl + "Kernel_Start:"
        repl1 = "align 16" + nl + "global Kernel_Start" + nl + "Kernel_Start:"
        if target1 in content:
            content = content.replace(target1, repl1, 1)
            print(f"Patched 'align 16' before Kernel_Start in {asm_path}")

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

    # 3. Inject visual VGA breadcrumbs along row 0
    breadcrumbs = [
        ("SystemVoidCosmosCoreGCImplementationInit:" + nl, "SystemVoidCosmosCoreGCImplementationInit:" + nl + "\tmov dword [0x000B8014], 0x0F430F47" + nl), # 'G','C'
        ("_END_OF____INIT__VMT__CODE____:" + nl, "_END_OF____INIT__VMT__CODE____:" + nl + "\tmov dword [0x000B8018], 0x0F4D0F56" + nl),                 # 'V','M'
        ("___INIT__STRINGS_TYPE_ID_S___:" + nl, "___INIT__STRINGS_TYPE_ID_S___:" + nl + "\tmov dword [0x000B801C], 0x0F540F53" + nl),                   # 'S','T'
        ("__ENGINE_ENTRYPOINT__.CreateEntrypoint.AfterAlloc:" + nl, "__ENGINE_ENTRYPOINT__.CreateEntrypoint.AfterAlloc:" + nl + "\tmov dword [0x000B8020], 0x0F4C0F41" + nl), # 'A','L'
        ("__ENGINE_ENTRYPOINT__.CallStart:" + nl, "__ENGINE_ENTRYPOINT__.CallStart:" + nl + "\tmov dword [0x000B8024], 0x0F430F4B" + nl),               # 'K','C'
        ("SystemVoidCosmosSystemKernelStart:" + nl, "SystemVoidCosmosSystemKernelStart:" + nl + "\tmov dword [0x000B8028], 0x0F530F4B" + nl),           # 'K','S'
        ("SystemVoidCosmosHALBootstrapInit:" + nl, "SystemVoidCosmosHALBootstrapInit:" + nl + "\tmov dword [0x000B802C], 0x0F420F48" + nl),             # 'H','B'
        ("SystemVoidWeatherOSKernelOnBoot:" + nl, "SystemVoidWeatherOSKernelOnBoot:" + nl + "\tmov dword [0x000B8030], 0x0F420F4F" + nl),               # 'O','B'
        ("SystemVoidWeatherOSKernelOnBoot.IL_000F:" + nl, "SystemVoidWeatherOSKernelOnBoot.IL_000F:" + nl + "\tmov dword [0x000B8034], 0x0F490F47" + nl), # 'G','I'
        ("SystemVoidWeatherOSKernelBeforeRun:" + nl, "SystemVoidWeatherOSKernelBeforeRun:" + nl + "\tmov dword [0x000B8038], 0x0F520F42" + nl),         # 'B','R'
    ]

    for target, repl in breadcrumbs:
        if target in content and repl not in content:
            content = content.replace(target, repl, 1)

    # 4. Patch RAT.AllocPages to zero-initialize every allocated page
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
        "\t\t\tmov ecx, dword [EBP + 8]" + nl +
        "\t\t\tshl ecx, 10" + nl +
        "\t\t\txor eax, eax" + nl +
        "\t\t\tcld" + nl +
        "\t\t\trep stosd" + nl +
        "\t\t\tpopad"
    )
    if rat_target in content and "\t\t\tshl ecx, 10" not in content:
        content = content.replace(rat_target, rat_repl, 1)
        print("Patched RAT.AllocPages with zero-initialization of allocated pages!")

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

    # 6. Patch HeapSmall.Alloc null-pointer check at IL_0046
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
        print("Patched HeapSmall.Alloc with null check safety!")

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
