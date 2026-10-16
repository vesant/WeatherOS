[BITS 16]
[ORG 0x7C00]
section .text
start:
    cli
    xor ax, ax
    mov ds, ax
    mov es, ax
    mov ss, ax
    mov sp, 0x7C00
    sti

    mov [boot_drive], dl

    ; Check for INT 13h Extensions
    mov ah, 0x41
    mov bx, 0x55AA
    int 0x13
    jc err_disk
    cmp bx, 0xAA55
    jne err_disk

    ; Load 4 sectors of Stage 2 from LBA 1 into 0x0000:0x8000
    mov si, dap
    mov ah, 0x42
    mov dl, [boot_drive]
    int 0x13
    jc err_disk

    ; Jump to Stage 2 passing boot_drive in DL
    mov dl, [boot_drive]
    jmp 0x0000:0x8000

err_disk:
    mov si, msg_err
.loop:
    lodsb
    or al, al
    jz .halt
    mov ah, 0x0E
    mov bx, 0x0007
    int 0x10
    jmp .loop
.halt:
    cli
    hlt
    jmp .halt

msg_err db 'WeatherOS MBR: Disk read error!', 13, 10, 0
boot_drive db 0

align 4
dap:
    db 0x10, 0
    dw 8
    dw 0x8000, 0x0000
    dq 1

pad: times 446 - ($ - start) db 0

; Partition 1: Active boot partition covering kernel
db 0x80             ; Bootable flag
db 0x00, 0x01, 0x00 ; Starting CHS (0, 0, 1)
db 0x83             ; Type: Linux native / WeatherOS
db 0xFE, 0xFF, 0xFF ; Ending CHS
dd 64               ; Starting LBA
dd 16541            ; Sector count

times 510 - ($ - start) db 0
dw 0xAA55