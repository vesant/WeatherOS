[BITS 16]
[ORG 0x8000]

section .text
stage2_start:
    cli
    xor ax, ax
    mov ds, ax
    mov es, ax
    mov fs, ax
    mov gs, ax
    mov ss, ax
    mov sp, 0x8000
    sti

    mov [boot_drive], dl

    ; 1. Print banner
    mov si, msg_booting
    call print_str

    ; 2. Robust A20 Gate Enable
    call enable_a20
    mov si, msg_a20_ok
    call print_str

    mov si, msg_loading
    call print_str

    ; 3. Setup pointers for loading
    mov dword [cur_lba], 64
    mov dword [dest_phys], 0x02000000
    mov word [chunk_index], 0
    mov byte [mb_counter], 0

load_loop:
    cmp dword [rem_sectors], 0
    jbe load_done

    mov eax, [rem_sectors]
    cmp eax, 64                    ; Safe 64-sector chunk (32 KB) for 100% BIOS USB compatibility
    jbe set_chunk
    mov eax, 64
set_chunk:
    mov [chunk_count], ax
    mov [dap_count], ax

    mov eax, [cur_lba]
    mov [dap_lba], eax
    mov dword [dap_lba + 4], 0

    ; Step A: Read chunk into 0x00010000 using standard BIOS INT 13h in 100% Real Mode
    xor ax, ax
    mov ds, ax
    mov si, dap_read
    mov ah, 0x42
    mov dl, [boot_drive]
    int 0x13
    jc disk_fail

    ; Step B: Switch to 32-bit Protected Mode to copy chunk safely into 32MB physical RAM
    cli
    lgdt [gdt_prot_ptr]
    mov eax, cr0
    or al, 1
    mov cr0, eax
    jmp dword 0x08:.pm_copy

[BITS 32]
.pm_copy:
    mov ax, 0x10
    mov ds, ax
    mov es, ax
    mov fs, ax
    mov gs, ax
    mov ss, ax

    mov esi, 0x00010000            ; Low RAM chunk buffer (64 KB)
    mov edi, [dest_phys]           ; Physical RAM destination (starts at 32 MB)
    movzx ecx, word [chunk_count]
    shl ecx, 7                     ; count * 128 dwords (512 bytes per sector)

    cld
    rep movsd
    mov [dest_phys], edi           ; Advance destination pointer in physical RAM

    ; Step C: Transition cleanly back to Real Mode for the next BIOS call
    jmp 0x18:.pm_exit16

[BITS 16]
.pm_exit16:
    mov ax, 0x20                   ; 16-bit real-mode compatible data descriptor
    mov ds, ax
    mov es, ax
    mov fs, ax
    mov gs, ax
    mov ss, ax

    mov eax, cr0
    and al, 0xFE
    mov cr0, eax
    jmp 0x00:.rm_back

.rm_back:
    xor ax, ax
    mov ds, ax
    mov es, ax
    mov fs, ax
    mov gs, ax
    mov ss, ax
    mov sp, 0x8000
    sti

    ; Advance LBA and remaining sectors
    movzx eax, word [chunk_count]
    add [cur_lba], eax
    sub [rem_sectors], eax

    ; Print progress: dot every 16 chunks (512 KB), MB count every 32 chunks (1 MB)
    inc word [chunk_index]
    test word [chunk_index], 0x0F
    jnz .no_dot
    mov al, '.'
    mov ah, 0x0E
    mov bx, 0x0007
    int 0x10

    test word [chunk_index], 0x1F
    jnz .no_dot
    inc byte [mb_counter]
    mov al, ' '
    call print_char
    mov al, '['
    call print_char
    mov al, [mb_counter]
    add al, '0'
    call print_char
    mov al, 'M'
    call print_char
    mov al, 'B'
    call print_char
    mov al, ']'
    call print_char
    mov al, ' '
    call print_char
.no_dot:

    jmp load_loop

load_done:
    ; 4. Print success
    mov si, msg_ok
    call print_str

    mov si, msg_jumping
    call print_str

    ; 6. Build Multiboot 2 information structure at 0x00010000 (ES = 0x1000)
    mov ax, 0x1000
    mov es, ax
    xor ax, ax
    xor di, di
    mov cx, 128
    cld
    rep stosw

    ; Tag 4: Basic Memory Information (type=4, size=16) at byte 8
    ; Tag starts at offset 8 (bytes 0..7 reserved for header)
    mov dword [es:8], 4
    mov dword [es:12], 16
    mov dword [es:16], 640
    mov dword [es:20], 1048576        ; Default 1GB upper limit

    ; Query BIOS INT 15h, AX=E820h to obtain TRUE hardware memory map from Toshiba NB100 BIOS
    ; Destination in MB2 structure:
    ; Tag 6 header: offset 24 (type=6, size=?, entry_size=24, entry_version=0)
    ; Entries start at offset 40 (di = 40)
    mov di, 40
    xor ebx, ebx                       ; Continuation value (must be 0 to start)
    xor bp, bp                         ; bp counts valid entries

.e820_loop:
    mov eax, 0xE820
    mov edx, 0x534D4150                ; 'SMAP'
    mov ecx, 24                        ; Request 24 bytes
    mov dword [es:di + 20], 1           ; ACPI 3.0 extended attribute (ignored if not written)
    int 0x15
    jc .e820_done                      ; Carry set -> end of map or unsupported
    cmp eax, 0x534D4150                ; Verify 'SMAP' signature
    jne .e820_done

    ; Validate entry length (must be >= 20 bytes)
    cmp ecx, 20
    jb .e820_skip
    ; Force entry size to 24 bytes and clear reserved/acpi field
    mov dword [es:di + 20], 0
    inc bp
    add di, 24
    cmp bp, 30                         ; Cap at 30 entries
    jae .e820_done

.e820_skip:
    test ebx, ebx                      ; ebx = 0 indicates end of list
    jnz .e820_loop

.e820_done:
    ; If BIOS E820 failed (bp == 0), provide safe fallback entries (0..640KB and 1MB..896MB)
    test bp, bp
    jnz .e820_finalize

    ; Fallback Entry 0: Low RAM (0 .. 639 KB, type=1)
    mov dword [es:40], 0x00000000
    mov dword [es:44], 0x00000000
    mov dword [es:48], 0x0009FC00
    mov dword [es:52], 0x00000000
    mov dword [es:56], 1
    mov dword [es:60], 0

    ; Fallback Entry 1: Safe High RAM (1 MB .. 896 MB, leaving 128MB for GMA 950 stolen VRAM)
    mov dword [es:64], 0x00100000
    mov dword [es:68], 0x00000000
    mov dword [es:72], 0x37F00000      ; 895 MB (safe TOLUD for Atom N270)
    mov dword [es:76], 0x00000000
    mov dword [es:80], 1
    mov dword [es:84], 0

    mov bp, 2
    mov di, 40 + (2 * 24)

.e820_finalize:
    ; Calculate Tag 6 total size = 16 (tag header) + (bp * 24)
    mov ax, bp
    mov cx, 24
    mul cx
    add ax, 16                         ; AX = Tag 6 size

    mov dword [es:24], 6               ; Tag type = 6 (Memory Map)
    movzx eax, ax
    mov [es:28], eax                   ; Tag size
    mov dword [es:32], 24              ; Entry size = 24
    mov dword [es:36], 0               ; Entry version = 0

    ; Align di to 8 bytes for End Tag
    add di, 7
    and di, 0xFFF8

    ; Tag 0: End Tag (type=0, size=8)
    mov dword [es:di], 0
    mov dword [es:di + 4], 8
    add di, 8

    ; Set Multiboot 2 Header total_size at es:0
    movzx eax, di
    mov [es:0], eax
    mov dword [es:4], 0

    ; 7. Enter 32-bit Protected Mode permanently
    cli
    lgdt [gdt_prot_ptr]
    mov eax, cr0
    or al, 1
    mov cr0, eax
    jmp dword 0x08:prot_start

[BITS 32]
prot_start:
    ; Reload all data segments with 32-bit flat data descriptor (0x10)
    mov ax, 0x10
    mov ds, ax
    mov es, ax
    mov fs, ax
    mov gs, ax
    mov ss, ax

    ; Install emergency 32-bit IDT (256 vectors) so any early hardware fault
    ; (#GP, #UD, #PF, IRQ) prints the exact vector number and halts!
    mov edi, 0x00018000                ; IDT table at 0x18000
    mov esi, isr_stub_table
    mov ecx, 32                        ; First 32 CPU exceptions

.setup_idt_loop:
    mov edx, [esi]
    mov eax, edx
    mov word [edi], ax                 ; Offset low (15..0)
    mov word [edi + 2], 0x08           ; Selector (0x08 = code segment)
    mov byte [edi + 4], 0              ; Reserved
    mov byte [edi + 5], 0x8E           ; Present, Ring 0, 32-bit Interrupt Gate
    shr eax, 16
    mov word [edi + 6], ax             ; Offset high (31..16)
    add edi, 8
    add esi, 4
    dec ecx
    jnz .setup_idt_loop

    ; Vectors 32..255: point to default handler (vector 0x20)
    mov ecx, 224
    mov edx, isr_stub_20
.setup_irq_loop:
    mov eax, edx
    mov word [edi], ax
    mov word [edi + 2], 0x08
    mov byte [edi + 4], 0
    mov byte [edi + 5], 0x8E
    shr eax, 16
    mov word [edi + 6], ax
    add edi, 8
    dec ecx
    jnz .setup_irq_loop

    lidt [idt_emergency_ptr]

    ; Visual marker: write 'W','O','S','3','2' to top-left of VGA text screen
    mov dword [0x000B8000], 0x0F4F0F57 ; 'W', 'O'
    mov dword [0x000B8004], 0x0F320F53 ; 'S', '2'

    ; Row 0: Print 'K','R','N','=' to 0x000B8008 and 8 bytes at 0x0240fc20
    mov dword [0x000B8008], 0x0E520E4B ; 'K', 'R'
    mov dword [0x000B800C], 0x0E3D0E4E ; 'N', '='

    mov esi, [entry_point]
    mov edi, 0x000B8010
    mov ecx, 8
.dump_kentry:
    lodsb
    mov ah, al
    shr al, 4
    and al, 0x0F
    cmp al, 10
    jb .k1
    add al, 'A' - 10
    jmp .k2
.k1: add al, '0'
.k2:
    mov [edi], al
    mov byte [edi + 1], 0x0E
    mov al, ah
    and al, 0x0F
    cmp al, 10
    jb .k3
    add al, 'A' - 10
    jmp .k4
.k3: add al, '0'
.k4:
    mov [edi + 2], al
    mov byte [edi + 3], 0x0E
    add edi, 4
    dec ecx
    jnz .dump_kentry

    ; Row 2 (0x000B8140): Print 'M','B','2','=' and 8 bytes at 0x02000000
    mov dword [0x000B8140], 0x0A420A4D ; 'M', 'B' (green on black)
    mov dword [0x000B8144], 0x0A3D0A32 ; '2', '='
    mov esi, 0x02000000
    mov edi, 0x000B8148
    mov ecx, 8
.dump_mb2:
    lodsb
    mov ah, al
    shr al, 4
    and al, 0x0F
    cmp al, 10
    jb .m1
    add al, 'A' - 10
    jmp .m2
.m1: add al, '0'
.m2:
    mov [edi], al
    mov byte [edi + 1], 0x0A
    mov al, ah
    and al, 0x0F
    cmp al, 10
    jb .m3
    add al, 'A' - 10
    jmp .m4
.m3: add al, '0'
.m4:
    mov [edi + 2], al
    mov byte [edi + 3], 0x0A
    add edi, 4
    dec ecx
    jnz .dump_mb2

    ; Row 3 (0x000B81E0): Search for MB2 magic 0xE85250D6 in physical RAM 0x00100000 .. 0x08000000
    mov dword [0x000B81E0], 0x0B4E0B46 ; 'F', 'N' (cyan on black)
    mov dword [0x000B81E4], 0x0B3A0B44 ; 'D', ':'
    mov edi, 0x00100000
    mov ecx, (64 * 1024 * 1024) / 4    ; Search 64 MB
    mov eax, 0xE85250D6
    cld
    repne scasd
    jnz .not_found

    ; Found! Address is edi - 4
    sub edi, 4
    mov edx, edi
    mov edi, 0x000B81E8
    mov ecx, 8
.print_fnd_loop:
    rol edx, 4
    mov al, dl
    and al, 0x0F
    cmp al, 10
    jb .f_dec
    add al, 'A' - 10
    jmp .f_store
.f_dec: add al, '0'
.f_store:
    mov [edi], al
    mov byte [edi + 1], 0x0B
    add edi, 2
    dec ecx
    jnz .print_fnd_loop
    jmp .search_done

.not_found:
    mov dword [0x000B81E8], 0x0C4F0C4E ; 'N', 'O'
    mov dword [0x000B81EC], 0x0C450C4E ; 'N', 'E'

.search_done:
    ; Multiboot 2 protocol register requirements:
    ; EAX = 0x36d76289 (Magic)
    ; EBX = physical address of Multiboot 2 info structure (0x00010000)
    mov eax, 0x36d76289
    mov ebx, 0x00010000

    ; Set initial stack pointer and save Multiboot pointer
    mov esp, 0x02050040
    mov dword [0x02050040], ebx

    ; Jump directly to Cosmos Kernel entry point (immediate, no memory dereference)!
    mov edx, [entry_point]
    jmp edx

%macro ISR_NOERR 1
isr_stub_%1:
    push dword 0                       ; Dummy error code
    push dword %1                      ; Vector number
    jmp common_isr_handler
%endmacro

%macro ISR_ERR 1
isr_stub_%1:
    push dword %1                      ; Vector number (error code already pushed by CPU)
    jmp common_isr_handler
%endmacro

ISR_NOERR 0
ISR_NOERR 1
ISR_NOERR 2
ISR_NOERR 3
ISR_NOERR 4
ISR_NOERR 5
ISR_NOERR 6
ISR_NOERR 7
ISR_ERR   8
ISR_NOERR 9
ISR_ERR   10
ISR_ERR   11
ISR_ERR   12
ISR_ERR   13
ISR_ERR   14
ISR_NOERR 15
ISR_NOERR 16
ISR_ERR   17
ISR_NOERR 18
ISR_NOERR 19
ISR_NOERR 20
ISR_NOERR 21
ISR_NOERR 22
ISR_NOERR 23
ISR_NOERR 24
ISR_NOERR 25
ISR_NOERR 26
ISR_NOERR 27
ISR_NOERR 28
ISR_NOERR 29
ISR_NOERR 30
ISR_NOERR 31

common_isr_handler:
    ; Emergency Exception Catcher: display red 'FAULT: ' + hex vector on VGA
    mov dword [0x000B80A0], 0x4F414F46 ; 'F', 'A' (white on red)
    mov dword [0x000B80A4], 0x4F4C4F55 ; 'U', 'L'
    mov dword [0x000B80A8], 0x4F3A4F54 ; 'T', ':'
    mov dword [0x000B80AC], 0x4F204F20 ; ' ', ' '

    ; Vector number is at [esp], err_code at [esp + 4], faulting EIP is at [esp + 8]
    mov eax, [esp]
    mov ebx, eax
    shr eax, 4
    and al, 0x0F
    cmp al, 10
    jb .dec1
    add al, 'A' - 10
    jmp .store1
.dec1:
    add al, '0'
.store1:
    mov byte [0x000B80AC], al
    mov byte [0x000B80AD], 0x4F

    mov eax, ebx
    and al, 0x0F
    cmp al, 10
    jb .dec2
    add al, 'A' - 10
    jmp .store2
.dec2:
    add al, '0'
.store2:
    mov byte [0x000B80AE], al
    mov byte [0x000B80AF], 0x4F

    ; Print " EIP: " at 0x000B80B2
    mov dword [0x000B80B2], 0x4F454F20 ; ' ', 'E'
    mov dword [0x000B80B6], 0x4F504F49 ; 'I', 'P'
    mov dword [0x000B80BA], 0x4F204F3A ; ':', ' '

    ; Faulting EIP is at [esp + 8]
    mov edx, [esp + 8]
    mov edi, 0x000B80BE
    mov ecx, 8                         ; 8 hex nibbles

.print_eip_loop:
    rol edx, 4
    mov al, dl
    and al, 0x0F
    cmp al, 10
    jb .eip_dec
    add al, 'A' - 10
    jmp .eip_store
.eip_dec:
    add al, '0'
.eip_store:
    mov [edi], al
    mov byte [edi + 1], 0x4F
    add edi, 2
    dec ecx
    jnz .print_eip_loop

.hang_fault:
    cli
    hlt
    jmp .hang_fault

[BITS 16]
disk_fail:
    mov si, msg_err
    call print_str
halt:
    cli
    hlt
    jmp halt

print_str:
    lodsb
    or al, al
    jz .done
    mov ah, 0x0E
    mov bx, 0x0007
    int 0x10
    jmp print_str
.done:
    ret

print_char:
    mov ah, 0x0E
    mov bx, 0x0007
    int 0x10
    ret

; Robust A20 gate check and enable (BIOS -> Fast A20 -> KBC 8042)
enable_a20:
    call check_a20
    jnz .done                      ; ZF=0 means already enabled!

    ; 1. Try BIOS INT 15h AX=2401h
    mov ax, 0x2401
    int 0x15
    call check_a20
    jnz .done

    ; 2. Try Fast A20 (port 0x92)
    in al, 0x92
    or al, 0x02
    and al, 0xFE                   ; Never set bit 0 (fast reset)
    out 0x92, al
    call check_a20
    jnz .done

    ; 3. Try Keyboard Controller (8042)
    cli
    call a20_wait_input
    mov al, 0xAD                  ; Disable keyboard
    out 0x64, al

    call a20_wait_input
    mov al, 0xD0                  ; Read output port
    out 0x64, al

    call a20_wait_output
    in al, 0x60
    push ax

    call a20_wait_input
    mov al, 0xD1                  ; Write output port
    out 0x64, al

    call a20_wait_input
    pop ax
    or al, 2                      ; Enable A20 bit
    out 0x60, al

    call a20_wait_input
    mov al, 0xAE                  ; Enable keyboard
    out 0x64, al

    call a20_wait_input
    sti

    call check_a20
.done:
    ret

a20_wait_input:
    in al, 0x64
    test al, 2
    jnz a20_wait_input
    ret

a20_wait_output:
    in al, 0x64
    test al, 1
    jz a20_wait_output
    ret

; Returns: AX = 1 (ZF=0) if A20 enabled, AX = 0 (ZF=1) if A20 disabled
check_a20:
    push ds
    push es
    push di
    push si

    xor ax, ax
    mov es, ax
    mov di, 0x7DFE                 ; 0000:7DFE

    mov ax, 0xFFFF
    mov ds, ax
    mov si, 0x7E0E                 ; FFFF:7E0E = physical 0x00107DFE (1MB + 0x7DFE)

    mov al, [es:di]
    push ax
    mov al, [ds:si]
    push ax

    mov byte [es:di], 0x00
    mov byte [ds:si], 0xFF

    out 0xEB, al                   ; Small I/O bus delay

    cmp byte [es:di], 0xFF         ; If [es:di] became 0xFF, memory wrapped (A20 disabled)

    pop ax
    mov [ds:si], al
    pop ax
    mov [es:di], al

    pop si
    pop di
    pop es
    pop ds

    je .a20_disabled              ; If [es:di] == 0xFF, ZF=1 -> disabled
    mov ax, 1
    test ax, ax                   ; Set ZF = 0 (enabled)
    ret

.a20_disabled:
    xor ax, ax                    ; Set ZF = 1 (disabled)
    ret

msg_booting db 13, 10, '=== WeatherOS Native Bare-Metal Bootloader ===', 13, 10, 0
msg_a20_ok db 'A20 Gate: Enabled', 13, 10, 0
msg_loading db 'Loading kernel into physical RAM (32 MB): ', 13, 10, 0
msg_ok db 13, 10, ' [OK - 100% LOADED]', 13, 10, 0
msg_press_enter db 'Kernel loaded successfully.', 13, 10, 'Press ENTER to launch WeatherOS...', 13, 10, 0
msg_jumping db 13, 10, 'Entering 32-bit Protected Mode and launching kernel...', 13, 10, 0
msg_err db 13, 10, 'DISK READ ERROR!', 13, 10, 0

boot_drive db 0
cur_lba dd 64
dest_phys dd 0x02000000
chunk_count dw 0
chunk_index dw 0
mb_counter db 0
save_cnt db 0

; Patchable variables (updated by make_isohybrid.py)
rem_sectors dd 16396
entry_point dd 0x240fc20

align 4
dap_read:
    db 0x10, 0
dap_count dw 0
    dw 0x0000, 0x1000
dap_lba dq 0

align 8
gdt_prot:
    dq 0x0000000000000000     ; 0x00: Null descriptor
    dq 0x00CF9A000000FFFF     ; 0x08: 32-bit Code (4GB, base 0, RX, 32-bit)
    dq 0x00CF92000000FFFF     ; 0x10: 32-bit Data (4GB, base 0, RW, 32-bit)
    dq 0x00009A000000FFFF     ; 0x18: 16-bit Code (64KB, base 0, RX, 16-bit)
    dq 0x000092000000FFFF     ; 0x20: 16-bit Data (64KB, base 0, RW, 16-bit)

gdt_prot_ptr:
    dw 39
    dd gdt_prot

align 8
idt_emergency_ptr:
    dw (256 * 8) - 1
    dd 0x00018000

align 4
isr_stub_table:
    dd isr_stub_0,  isr_stub_1,  isr_stub_2,  isr_stub_3
    dd isr_stub_4,  isr_stub_5,  isr_stub_6,  isr_stub_7
    dd isr_stub_8,  isr_stub_9,  isr_stub_10, isr_stub_11
    dd isr_stub_12, isr_stub_13, isr_stub_14, isr_stub_15
    dd isr_stub_16, isr_stub_17, isr_stub_18, isr_stub_19
    dd isr_stub_20, isr_stub_21, isr_stub_22, isr_stub_23
    dd isr_stub_24, isr_stub_25, isr_stub_26, isr_stub_27
    dd isr_stub_28, isr_stub_29, isr_stub_30, isr_stub_31