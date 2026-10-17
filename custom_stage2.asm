[BITS 16]
[ORG 0x8000]

section .text
boot_manager_start:
    ; 1. Segment registers and stack initialization
    cli
    cld
    xor ax, ax
    mov ds, ax
    mov es, ax
    mov fs, ax
    mov gs, ax
    mov ss, ax
    mov sp, 0x8000
    sti

    mov [boot_drive], dl

    ; 2. Initialize Video Mode to standard 80x25 16-color text mode (Mode 03h)
    ; This cleans the Toshiba graphical boot splash, sets cursor to (0,0),
    ; selects display page 0, and loads the standard VGA font.
    mov ax, 0x0003
    int 0x10

    ; 3. Ensure PC speaker hardware gate is strictly muted
    in al, 0x61
    and al, 0xFC
    out 0x61, al

    ; 4. Print Boot Manager Welcome Banner
    mov si, msg_banner
    call print_str

    ; 5. Enable A20 Address Line safely
    call enable_a20

    ; 6. Clean segment registers for Real Mode disk loading
    xor ax, ax
    mov ds, ax
    mov es, ax
    mov fs, ax
    mov gs, ax
    mov ss, ax
    mov sp, 0x8000
    sti

    ; 7. Load WeatherOS Monolithic Kernel into Physical RAM (0x02000000)
    mov si, msg_loading
    call print_str

    mov dword [cur_lba], 64
    mov dword [dest_phys], 0x02000000
    mov word [chunk_index], 0

load_loop:
    cmp dword [rem_sectors], 0
    jbe load_done

    mov eax, [rem_sectors]
    cmp eax, 64                    ; 64 sectors = 32 KB per chunk
    jbe .set_chunk
    mov eax, 64
.set_chunk:
    mov [chunk_count], ax
    mov [dap_count], ax

    mov eax, [cur_lba]
    mov [dap_lba], eax
    mov dword [dap_lba + 4], 0

    ; Step A: Read chunk into conventional RAM buffer at 0x1000:0x0000 (0x00010000)
    ; Real Mode BIOS INT 13h (never leaves Real Mode!)
    xor ax, ax
    mov ds, ax
    mov si, dap_read
    mov ah, 0x42
    mov dl, [boot_drive]
    int 0x13
    jc disk_fail
    test ah, ah
    jnz disk_fail

    ; Step B: Direct 32-bit copy into Physical RAM in true Protected Mode
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
    mov esi, 0x00010000            ; Low RAM source buffer
    mov edi, [dest_phys]           ; Physical RAM destination (32MB+)
    movzx ecx, word [chunk_count]
    shl ecx, 7                     ; count * 128 dwords (512 bytes per sector)
    cld
    rep movsd                      ; Hardware 32-bit memory copy!
    mov [dest_phys], edi

    ; Return to 16-bit Real Mode cleanly
    jmp dword 0x18:.pm16_leave

[BITS 16]
.pm16_leave:
    mov ax, 0x20
    mov ds, ax
    mov es, ax
    mov fs, ax
    mov gs, ax
    mov ss, ax
    mov eax, cr0
    and al, 0xFE
    mov cr0, eax
    jmp 0x0000:.rm_leave

.rm_leave:
    xor ax, ax
    mov ds, ax
    mov es, ax
    mov fs, ax
    mov gs, ax
    mov ss, ax
    mov sp, 0x8000
    sti

    ; Step C: Update pointers and show progress
    movzx eax, word [chunk_count]
    add [cur_lba], eax
    sub [rem_sectors], eax

    inc word [chunk_index]
    test word [chunk_index], 0x0F  ; Dot every 512 KB
    jnz .no_dot
    mov al, '.'
    mov ah, 0x0E
    mov bx, 0x0007
    int 0x10
.no_dot:
    jmp load_loop

load_done:
    mov si, msg_ok
    call print_str

    mov si, msg_starting
    call print_str

    ; 8. Build Multiboot 2 information structure at 0x00010000 (ES = 0x1000)
    mov ax, 0x1000
    mov es, ax
    xor ax, ax
    xor di, di
    mov cx, 128
    cld
    rep stosw

    ; Tag 4: Basic Memory Information (type=4, size=16) at byte 8
    mov dword [es:8], 4
    mov dword [es:12], 16
    mov dword [es:16], 640
    mov dword [es:20], 1048576        ; 1GB upper limit

    ; Query BIOS INT 15h, AX=E820h for memory map
    mov di, 40
    xor ebx, ebx
    xor bp, bp

.e820_loop:
    mov eax, 0xE820
    mov edx, 0x534D4150                ; 'SMAP'
    mov ecx, 24
    mov dword [es:di + 20], 1
    int 0x15
    jc .e820_done
    cmp eax, 0x534D4150
    jne .e820_done

    cmp ecx, 20
    jb .e820_skip
    mov dword [es:di + 20], 1
    inc bp
    add di, 24
    cmp bp, 30
    jae .e820_done

.e820_skip:
    test ebx, ebx
    jnz .e820_loop

.e820_done:
    test bp, bp
    jnz .e820_finalize

    ; Fallback Entry 0: Low RAM (0 .. 639 KB)
    mov dword [es:40], 0x00000000
    mov dword [es:44], 0x00000000
    mov dword [es:48], 0x0009FC00
    mov dword [es:52], 0x00000000
    mov dword [es:56], 1
    mov dword [es:60], 0

    ; Fallback Entry 1: Safe High RAM (1 MB .. 896 MB)
    mov dword [es:64], 0x00100000
    mov dword [es:68], 0x00000000
    mov dword [es:72], 0x37F00000
    mov dword [es:76], 0x00000000
    mov dword [es:80], 1
    mov dword [es:84], 0

    mov bp, 2
    mov di, 40 + (2 * 24)

.e820_finalize:
    mov ax, bp
    mov cx, 24
    mul cx
    add ax, 16                         ; Tag 6 total size
    mov dword [es:24], 6               ; Tag type = 6 (Memory Map)
    movzx eax, ax
    mov [es:28], eax
    mov dword [es:32], 24
    mov dword [es:36], 0

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

    ; 9. Switch to 32-bit Protected Mode PERMANENTLY and launch Kernel
    cli
    lgdt [gdt_prot_ptr]
    mov eax, cr0
    or al, 1
    mov cr0, eax
    jmp dword 0x08:prot_start

[BITS 32]
prot_start:
    ; Reload all 32-bit flat data selectors
    mov ax, 0x10
    mov ds, ax
    mov es, ax
    mov fs, ax
    mov gs, ax
    mov ss, ax

    ; Install emergency 32-bit IDT (256 vectors)
    mov edi, 0x00018000
    mov esi, isr_stub_table
    mov ecx, 32

.setup_idt_loop:
    mov edx, [esi]
    mov eax, edx
    mov word [edi], ax
    mov word [edi + 2], 0x08
    mov byte [edi + 4], 0
    mov byte [edi + 5], 0x8E
    shr eax, 16
    mov word [edi + 6], ax
    add edi, 8
    add esi, 4
    dec ecx
    jnz .setup_idt_loop

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

    ; Launch Multiboot 2:
    mov eax, 0x36d76289
    mov ebx, 0x00010000

    mov esp, 0x02050040
    mov dword [0x02050040], ebx

    ; Verify Multiboot 2 Header in physical RAM (0x02000000)
    ; Header must begin with magic 0xE85250D6 (bytes: D6 50 52 E8)
    cmp dword [0x02000000], 0xE85250D6
    jne bad_kernel_signature

    ; Verify Entry Point code: mov esp, 0x02050040 (0xBC 0x40 0x00 0x05 -> 0x050040BC)
    mov edx, [entry_point]
    cmp dword [edx], 0x050040BC
    jne bad_kernel_entry

    ; Zero-initialize BSS / heap padding (64 KB immediately following kernel load)
    mov edi, [dest_phys]
    mov ecx, 16384                     ; 16384 dwords = 64 KB
    xor eax, eax
    cld
    rep stosd

    ; Restore Multiboot 2 magic register contract
    mov eax, 0x36d76289
    mov ebx, 0x00010000

    ; Jump directly to WeatherOS Monolithic Kernel entry point
    jmp edx

bad_kernel_entry:
    mov edi, 0x000B8000
    mov esi, msg_bad_entry
.bke_loop:
    lodsb
    or al, al
    jz .bke_halt
    mov ah, 0x4F                       ; White text on red background
    stosw
    jmp .bke_loop
.bke_halt:
    cli
    hlt
    jmp .bke_halt

msg_bad_entry db 'WeatherOS Boot Manager: Kernel entry point validation failed!', 0

bad_kernel_signature:
    mov edi, 0x000B8000
    mov esi, msg_bad_kernel
.bks_loop:
    lodsb
    or al, al
    jz .bks_halt
    mov ah, 0x4F                       ; White text on red background
    stosw
    jmp .bks_loop
.bks_halt:
    cli
    hlt
    jmp .bks_halt

msg_bad_kernel db 'WeatherOS Boot Manager: Kernel payload verification failed at 0x02000000!', 0

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
    ; Emergency Exception Diagnostic Screen (white text on red background)
    ; Row 1: FAULT: XX  ERR: XXXXXXXX  EIP: XXXXXXXX  ESP: XXXXXXXX
    mov edi, 0x000B80A0

    ; "FAULT: "
    mov dword [edi], 0x4F414F46       ; 'F', 'A'
    mov dword [edi + 4], 0x4F4C4F55   ; 'U', 'L'
    mov dword [edi + 8], 0x4F3A4F54   ; 'T', ':'
    mov word [edi + 12], 0x4F20       ; ' '
    add edi, 14

    ; Hex vector (2 digits)
    mov edx, [esp]
    mov al, dl
    shr al, 4
    and al, 0x0F
    call hex_nibble_to_vga
    mov al, dl
    and al, 0x0F
    call hex_nibble_to_vga

    ; "  ERR: "
    mov word [edi], 0x4F20
    mov word [edi + 2], 0x4F20
    mov dword [edi + 4], 0x4F524F45   ; 'E', 'R'
    mov dword [edi + 8], 0x4F3A4F52   ; 'R', ':'
    mov word [edi + 12], 0x4F20
    add edi, 14
    mov edx, [esp + 4]                 ; Error Code
    call print_hex_dword

    ; "  EIP: "
    mov word [edi], 0x4F20
    mov word [edi + 2], 0x4F20
    mov dword [edi + 4], 0x4F494F45   ; 'E', 'I'
    mov dword [edi + 8], 0x4F3A4F50   ; 'P', ':'
    mov word [edi + 12], 0x4F20
    add edi, 14
    mov edx, [esp + 8]                 ; Fault EIP
    call print_hex_dword

    ; "  ESP: "
    mov word [edi], 0x4F20
    mov word [edi + 2], 0x4F20
    mov dword [edi + 4], 0x4F534F45   ; 'E', 'S'
    mov dword [edi + 8], 0x4F3A4F50   ; 'P', ':'
    mov word [edi + 12], 0x4F20
    add edi, 14
    lea edx, [esp + 20]                ; Fault ESP
    call print_hex_dword

    ; Row 2 (0x000B8140): CS: XXXX  EFL: XXXXXXXX  CR0: XXXXXXXX
    mov edi, 0x000B8140

    ; "CS: "
    mov dword [edi], 0x4F534F43       ; 'C', 'S'
    mov dword [edi + 4], 0x4F204F3A   ; ':', ' '
    add edi, 8
    mov edx, [esp + 12]                ; CS
    call print_hex_dword

    ; "  EFL: "
    mov word [edi], 0x4F20
    mov word [edi + 2], 0x4F20
    mov dword [edi + 4], 0x4F464F45   ; 'E', 'F'
    mov dword [edi + 8], 0x4F3A4F4C   ; 'L', ':'
    mov word [edi + 12], 0x4F20
    add edi, 14
    mov edx, [esp + 16]                ; EFLAGS
    call print_hex_dword

    ; "  CR0: "
    mov word [edi], 0x4F20
    mov word [edi + 2], 0x4F20
    mov dword [edi + 4], 0x4F524F43   ; 'C', 'R'
    mov dword [edi + 8], 0x4F3A4F30   ; '0', ':'
    mov word [edi + 12], 0x4F20
    add edi, 14
    mov edx, cr0
    call print_hex_dword

    ; Row 3 (0x000B81E0): STK: [esp+20] [esp+24] [esp+28] [esp+32]
    mov edi, 0x000B81E0
    mov dword [edi], 0x4F544F53       ; 'S', 'T'
    mov dword [edi + 4], 0x4F204F4B   ; 'K', ' '
    add edi, 8

    mov ebp, 0
.next_stk_dword:
    mov edx, [esp + 20 + ebp*4]
    call print_hex_dword
    mov word [edi], 0x4F20
    add edi, 2
    inc ebp
    cmp ebp, 4
    jb .next_stk_dword

    ; Row 4 (0x000B8280): CODE: [EIP-4] [EIP] [EIP+4] [EIP+8]
    mov edi, 0x000B8280
    mov dword [edi], 0x4F4F4F43       ; 'C', 'O'
    mov dword [edi + 4], 0x4F454F44   ; 'D', 'E'
    mov dword [edi + 8], 0x4F204F3A   ; ':', ' '
    add edi, 12

    mov esi, [esp + 8]                 ; Fault EIP
    sub esi, 4
    mov ebp, 0
.next_code_dword:
    mov edx, [esi + ebp*4]
    call print_hex_dword
    mov word [edi], 0x4F20
    add edi, 2
    inc ebp
    cmp ebp, 4
    jb .next_code_dword

    ; Row 5 (0x000B8320): STK+: [esp+36] [esp+40] [esp+44] [esp+48]
    mov edi, 0x000B8320
    mov dword [edi], 0x4F544F53       ; 'S', 'T'
    mov dword [edi + 4], 0x4F2B4F4B   ; 'K', '+'
    add edi, 8
    mov ebp, 4
.next_stk_dword2:
    mov edx, [esp + 20 + ebp*4]
    call print_hex_dword
    mov word [edi], 0x4F20
    add edi, 2
    inc ebp
    cmp ebp, 8
    jb .next_stk_dword2

    ; Row 6 (0x000B83C0): STK2: [esp+52] [esp+56] [esp+60] [esp+64]
    mov edi, 0x000B83C0
    mov dword [edi], 0x4F544F53       ; 'S', 'T'
    mov dword [edi + 4], 0x4F324F4B   ; 'K', '2'
    add edi, 8
    mov ebp, 8
.next_stk_dword3:
    mov edx, [esp + 20 + ebp*4]
    call print_hex_dword
    mov word [edi], 0x4F20
    add edi, 2
    inc ebp
    cmp ebp, 12
    jb .next_stk_dword3

    ; Row 7 (0x000B8460): LAST: [0x02058e5b]
    mov edi, 0x000B8460
    mov dword [edi], 0x4F414F4C       ; 'L', 'A'
    mov dword [edi + 4], 0x4F544F53   ; 'S', 'T'
    mov word [edi + 8], 0x4F3A        ; ':'
    mov word [edi + 10], 0x4F20       ; ' '
    add edi, 12
    mov edx, [0x02058e5b]
    call print_hex_dword

    ; Row 8 (0x000B8500): RAM: [0x02770000] [0x02770004] [0x02770008] [0x0277000C]
    mov edi, 0x000B8500
    mov dword [edi], 0x4F414F52       ; 'R', 'A'
    mov dword [edi + 4], 0x4F3A4F4D   ; 'M', ':'
    mov word [edi + 8], 0x4F20        ; ' '
    add edi, 10
    mov ebp, 0
.next_ram_dword:
    mov edx, [0x02770000 + ebp*4]
    call print_hex_dword
    mov word [edi], 0x4F20
    add edi, 2
    inc ebp
    cmp ebp, 4
    jb .next_ram_dword

.hang_fault:
    cli
    hlt
    jmp .hang_fault

print_hex_dword:
    push ecx
    mov ecx, 8
.phd_loop:
    rol edx, 4
    mov al, dl
    and al, 0x0F
    call hex_nibble_to_vga
    dec ecx
    jnz .phd_loop
    pop ecx
    ret

hex_nibble_to_vga:
    cmp al, 10
    jb .h_dec
    add al, 'A' - 10
    jmp .h_put
.h_dec:
    add al, '0'
.h_put:
    mov [edi], al
    mov byte [edi + 1], 0x4F
    add edi, 2
    ret

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

; Standard, Safe A20 gate enable (BIOS -> Fast A20)
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
    test al, 0x02
    jnz .done                      ; Already enabled
    or al, 0x02
    and al, 0xFE                   ; Ensure bit 0 (Fast Reset) is NEVER set
    out 0x92, al
    call check_a20

.done:
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

msg_banner db 'WeatherOS Boot Manager v3.1 (x86 32-bit)', 13, 10, 0
msg_loading db 'Loading WeatherOS Kernel into physical RAM... ', 0
msg_ok db ' [OK]', 13, 10, 0
msg_starting db 'Starting WeatherOS Monolithic Kernel...', 13, 10, 0
msg_err db 13, 10, 'WeatherOS Boot Error: Storage read failure.', 13, 10, 0

boot_drive db 0
cur_lba dd 64
dest_phys dd 0x02000000
chunk_count dw 0
chunk_index dw 0

; Patchable variables (updated by make_isohybrid.py)
rem_sectors dd 16449
entry_point dd 0x2410b00

align 4
dap_read:
    db 0x10, 0
dap_count dw 0
    dw 0x0000, 0x1000
dap_lba dq 0

align 8
; GDT for Protected Mode and High Memory Block Transfers
gdt_prot:
    dq 0x0000000000000000     ; 0x00: Null descriptor
    dq 0x00CF9A000000FFFF     ; 0x08: 32-bit Code (Base 0, Limit 4GB, RX, 32-bit)
    dq 0x00CF92000000FFFF     ; 0x10: 32-bit Data (Base 0, Limit 4GB, RW, 32-bit)
    dq 0x00009A000000FFFF     ; 0x18: 16-bit Code (Base 0, Limit 64KB, RX, 16-bit)
    dq 0x000092000000FFFF     ; 0x20: 16-bit Data (Base 0, Limit 64KB, RW, 16-bit)

gdt_prot_ptr:
    dw 39                     ; (5 * 8) - 1 = 39
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