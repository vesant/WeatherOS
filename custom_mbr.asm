[BITS 16]
[ORG 0x7C00]
section .text
start:
    ; 0. Immediate hardware visual & audio markers (proves BIOS executed Sector 0)
    mov ax, 0xB800
    mov es, ax
    mov word [es:0], 0x1F4D    ; Display 'M' (White on Blue) at top-left
    in al, 0x61
    or al, 3
    out 0x61, al               ; Click PC speaker

    cli
    xor ax, ax
    mov ds, ax
    mov es, ax
    mov ss, ax
    mov sp, 0x7C00
    sti

    mov [boot_drive], dl

    ; 1. Try INT 13h Extensions (LBA)
    mov dl, [boot_drive]
    mov ah, 0x41
    mov bx, 0x55AA
    int 0x13
    jc .try_chs
    cmp bx, 0xAA55
    jne .try_chs

    ; Load 8 sectors of Stage 2 from LBA 1 into 0x0000:0x8000 using LBA
    mov dl, [boot_drive]
    mov si, dap
    mov ah, 0x42
    int 0x13
    jnc .jump_stage2

.try_chs:
    ; 2. CHS Fallback: Sectors 1..8 are Cylinder 0, Head 0, Sectors 2..9 (1-based sector numbering)
    ; AH=0x02, AL=8 (sectors), CH=0 (cyl 0), CL=2 (sector 2), DH=0 (head 0), DL=[boot_drive]
    ; ES:BX = 0x0000:0x8000
    xor ax, ax
    mov es, ax
    mov bx, 0x8000
    mov dl, [boot_drive]
    mov ah, 0x02
    mov al, 8
    mov ch, 0
    mov cl, 2
    mov dh, 0
    int 0x13
    jc err_disk

.jump_stage2:
    mov dl, [boot_drive]
    jmp 0x0000:0x8000

err_disk:
    ; Direct write to VGA text memory 0xB8000 (red on black) for 100% visibility
    mov ax, 0xB800
    mov es, ax
    mov di, 0
    mov si, msg_err
.vga_loop:
    lodsb
    or al, al
    jz .halt
    mov ah, 0x4F    ; White on red
    stosw
    jmp .vga_loop

.halt:
    cli
    hlt
    jmp .halt

msg_err db 'WeatherOS MBR: Disk read error!', 0
boot_drive db 0

align 4
dap:
    db 0x10, 0
    dw 8
    dw 0x8000, 0x0000
    dq 1

pad: times 440 - ($ - start) db 0

; Windows / BIOS Disk Signature (UID)
dd 0x57454154       ; "WEAT"
dw 0x0000           ; Reserved

; Partition 1: Active boot partition covering kernel (FAT32 LBA - 100% BIOS compatible)
db 0x80             ; Bootable flag
db 0x01, 0x02, 0x00 ; Starting CHS (Head 1, Sector 2, Cyl 0 = LBA 64)
db 0x0C             ; Type: FAT32 LBA (0x0C)
db 0xFE, 0xFF, 0xFF ; Ending CHS
dd 64               ; Starting LBA
dd 16541            ; Sector count

times 510 - ($ - start) db 0
dw 0xAA55