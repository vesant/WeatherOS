using System;
using Cosmos.System;
using WeatherOS.Services;

namespace WeatherOS.Graphics
{
    public enum BootMode
    {
        LiveOS,
        InstallToDisk,
        BootFromDisk
    }

    public static class BootMenu
    {
        public static BootMode Show()
        {
            bool isInstalled = UpdateManagerService.IsInstalledOnDisk();
            int maxOptions = isInstalled ? 3 : 2;
            int selectedIndex = 0;
            bool menuRunning = true;
            BootMode chosenMode = isInstalled ? BootMode.BootFromDisk : BootMode.LiveOS;

            System.Console.CursorVisible = false;
            try { Cosmos.Core.CPU.EnableInterrupts(); } catch { }

            // 1. Draw Static Menu Structure Once (No clearing afterwards to prevent screen flicker)
            DrawMenuStatic(isInstalled);
            DrawOptionsAndDescription(selectedIndex, isInstalled);

            int countdownSeconds = 10; // 10 seconds auto-boot countdown
            UpdateCountdownRow(countdownSeconds);

            int lastRtcSecond = -1;
            try { lastRtcSecond = Cosmos.HAL.RTC.Second; } catch { }

            int loopTickCounter = 0;

            while (menuRunning)
            {
                // Poll keyboard with dual-path detection (Cosmos driver queue + direct PS/2 port 0x60/0x64)
                for (int p = 0; p < 5; p++)
                {
                    if (CheckHardwareKey(out var key, out var ch))
                    {
                        if (countdownSeconds > 0)
                        {
                            countdownSeconds = -1; // Permanently cancel auto-countdown on user input
                            UpdateCountdownRow(-1);
                        }

                        if (key == ConsoleKeyEx.UpArrow)
                        {
                            selectedIndex = (selectedIndex > 0) ? selectedIndex - 1 : maxOptions - 1;
                            DrawOptionsAndDescription(selectedIndex, isInstalled);
                        }
                        else if (key == ConsoleKeyEx.DownArrow || key == ConsoleKeyEx.Tab)
                        {
                            selectedIndex = (selectedIndex < maxOptions - 1) ? selectedIndex + 1 : 0;
                            DrawOptionsAndDescription(selectedIndex, isInstalled);
                        }
                        else if (key == ConsoleKeyEx.D1 || key == ConsoleKeyEx.Num1 || ch == '1')
                        {
                            selectedIndex = 0;
                            menuRunning = false;
                            break;
                        }
                        else if (key == ConsoleKeyEx.D2 || key == ConsoleKeyEx.Num2 || ch == '2')
                        {
                            selectedIndex = 1;
                            menuRunning = false;
                            break;
                        }
                        else if (isInstalled && (key == ConsoleKeyEx.D3 || key == ConsoleKeyEx.Num3 || ch == '3'))
                        {
                            selectedIndex = 2;
                            menuRunning = false;
                            break;
                        }
                        else if (key == ConsoleKeyEx.Enter || ch == '\r' || ch == '\n' || key == ConsoleKeyEx.Spacebar)
                        {
                            menuRunning = false;
                            break;
                        }
                    }

                    HardwareDelayMs(10);
                    loopTickCounter++;
                }

                // Handle Auto-boot Countdown
                if (countdownSeconds > 0)
                {
                    bool secondElapsed = false;

                    // Method A: Hardware CMOS RTC second register (Ports 0x70 / 0x71)
                    try
                    {
                        int currentRtcSecond = Cosmos.HAL.RTC.Second;
                        if (currentRtcSecond != lastRtcSecond)
                        {
                            lastRtcSecond = currentRtcSecond;
                            secondElapsed = true;
                        }
                    }
                    catch { }

                    // Method B: Loop tick fallback (~100 polls of 10ms = 1000ms)
                    if (!secondElapsed && loopTickCounter >= 100)
                    {
                        secondElapsed = true;
                    }

                    if (secondElapsed)
                    {
                        loopTickCounter = 0;
                        countdownSeconds--;
                        UpdateCountdownRow(countdownSeconds);

                        if (countdownSeconds <= 0)
                        {
                            menuRunning = false; // Auto-boot selected default option
                        }
                    }
                }
            }

            if (isInstalled)
            {
                if (selectedIndex == 0)
                {
                    UpdateManagerService.SetBootSource(BootSource.HardDisk);
                    chosenMode = BootMode.BootFromDisk;
                }
                else if (selectedIndex == 1)
                {
                    UpdateManagerService.SetBootSource(BootSource.LiveRam);
                    chosenMode = BootMode.LiveOS;
                }
                else
                {
                    chosenMode = BootMode.InstallToDisk;
                }
            }
            else
            {
                if (selectedIndex == 0)
                {
                    UpdateManagerService.SetBootSource(BootSource.LiveRam);
                    chosenMode = BootMode.LiveOS;
                }
                else
                {
                    chosenMode = BootMode.InstallToDisk;
                }
            }

            System.Console.Clear();
            System.Console.CursorVisible = true;
            return chosenMode;
        }

        private static void DrawMenuStatic(bool isInstalled)
        {
            System.Console.BackgroundColor = ConsoleColor.Black;
            System.Console.Clear();

            // 1. Top Header Bar (Row 0)
            string topBar = " WeatherOS Boot Selection Manager v3.1                 [x86 Bare-Metal Edition] ";
            Screen.WriteClipped(0, 0, topBar, Screen.Width - 1, ConsoleColor.White, ConsoleColor.DarkBlue, true);

            // 2. ASCII Title (Rows 2 to 7)
            string[] bannerLines = new string[]
            {
                @"  __          __         _   _                  ____   _____  ",
                @"  \ \        / /        | | | |                / __ \ / ____| ",
                @"   \ \  /\  / /__  __ _ | |_| |__   ___ _ __  | |  | | (___   ",
                @"    \ \/  \/ / _ \/ _` || __| '_ \ / _ \ '__| | |  | |\___ \  ",
                @"     \  /\  /  __/ (_| || |_| | | |  __/ |    | |__| |____) | ",
                @"      \/  \/ \___|\__,_| \__|_| |_|\___|_|     \____/|_____/  "
            };

            for (int i = 0; i < bannerLines.Length; i++)
            {
                int posX = (Screen.Width - bannerLines[i].Length) / 2;
                if (posX < 0) posX = 0;
                Screen.WriteClipped(posX, 2 + i, bannerLines[i], Screen.Width - posX, ConsoleColor.Cyan, ConsoleColor.Black);
            }

            // 3. Subtitle (Row 8)
            string sub = isInstalled
                ? "Hard Disk Target Detected  |  0:\\boot\\WeatherOS.bin [ACTIVE]"
                : "Dedicated Meteorological Operating System Bootloader";
            int subX = (Screen.Width - sub.Length) / 2;
            if (subX < 0) subX = 0;
            ConsoleColor subColor = isInstalled ? ConsoleColor.Green : ConsoleColor.DarkGray;
            Screen.WriteClipped(subX, 8, sub, Screen.Width - subX, subColor, ConsoleColor.Black);

            // 4. Selection Frame (Rows 10 to 20, columns 5 to 74)
            int left = 5;
            int top = 10;
            int width = 70;
            int height = 11;
            Screen.DrawBox(left, top, width, height, "BOOT TARGET SELECTION", ConsoleColor.Black, ConsoleColor.DarkCyan, ConsoleColor.Yellow);

            // Horizontal separator inside box
            Screen.WriteInsideWindow(left, width, top + 6, 0, new string('-', width - 4), ConsoleColor.DarkCyan, ConsoleColor.Black);

            // 5. Instruction line (Row 22)
            string instruct = isInstalled
                ? "[Up/Dn / Tab] Select Option   |   [1 / 2 / 3] Direct Key   |   [ENTER] Boot"
                : "[Up/Dn / Tab] Select Option   |   [1 / 2] Direct Key   |   [ENTER] Boot";
            int insX = (Screen.Width - instruct.Length) / 2;
            if (insX < 0) insX = 0;
            Screen.WriteClipped(insX, 22, instruct, Screen.Width - insX, ConsoleColor.Yellow, ConsoleColor.Black);

            // 6. Bottom Status Bar (Row 24)
            string btm = isInstalled
                ? " WeatherOS Kernel 3.1  |  Default: Hard Disk (0:\\)  |  Status: INSTALLED "
                : " WeatherOS Kernel 3.1  |  Default: Live OS (RAM)   |  Status: LIVE SESSION ";
            Screen.WriteClipped(0, Screen.Height - 1, btm, Screen.Width - 1, ConsoleColor.Black, ConsoleColor.Gray, true);
        }

        private static void DrawOptionsAndDescription(int selectedIndex, bool isInstalled)
        {
            int left = 5;
            int top = 10;
            int width = 70;

            if (isInstalled)
            {
                DrawOptionRow(left, width, top + 2, "1. Boot WeatherOS from Hard Disk  (0:\\boot\\WeatherOS.bin)", selectedIndex == 0);
                DrawOptionRow(left, width, top + 3, "2. Try WeatherOS  (Live OS / RAM Mode)", selectedIndex == 1);
                DrawOptionRow(left, width, top + 4, "3. Reinstall or Repair WeatherOS on Hard Disk", selectedIndex == 2);

                if (selectedIndex == 0)
                {
                    Screen.WriteInsideWindow(left, width, top + 7, 2, "Description: Boot installed OS directly from FAT32 local disk.", ConsoleColor.White, ConsoleColor.Black);
                    Screen.WriteInsideWindow(left, width, top + 8, 2, "Persistent user storage, documents, and settings.ini active.  ", ConsoleColor.Green, ConsoleColor.Black);
                }
                else if (selectedIndex == 1)
                {
                    Screen.WriteInsideWindow(left, width, top + 7, 2, "Description: Temporary session in RAM. Disks remain untouched.  ", ConsoleColor.White, ConsoleColor.Black);
                    Screen.WriteInsideWindow(left, width, top + 8, 2, "Full desktop environment and diagnostic suite available.      ", ConsoleColor.Gray, ConsoleColor.Black);
                }
                else
                {
                    Screen.WriteInsideWindow(left, width, top + 7, 2, "Description: Launch setup wizard to reinstall or repair system files.", ConsoleColor.White, ConsoleColor.Black);
                    Screen.WriteInsideWindow(left, width, top + 8, 2, "Non-destructive update options available inside the wizard.   ", ConsoleColor.Cyan, ConsoleColor.Black);
                }
            }
            else
            {
                DrawOptionRow(left, width, top + 2, "1. Try WeatherOS  (Live OS / RAM Mode)                      ", selectedIndex == 0);
                DrawOptionRow(left, width, top + 4, "2. Install WeatherOS to Hard Disk  (Setup Wizard)           ", selectedIndex == 1);

                if (selectedIndex == 0)
                {
                    Screen.WriteInsideWindow(left, width, top + 7, 2, "Description: Boot into memory. Hard drives remain untouched.  ", ConsoleColor.White, ConsoleColor.Black);
                    Screen.WriteInsideWindow(left, width, top + 8, 2, "Full environment with desktop, live weather, network & diagnostics.", ConsoleColor.Gray, ConsoleColor.Black);
                }
                else
                {
                    Screen.WriteInsideWindow(left, width, top + 7, 2, "Description: Partition hard drive and copy WeatherOS system files.  ", ConsoleColor.White, ConsoleColor.Black);
                    Screen.WriteInsideWindow(left, width, top + 8, 2, "Interactive 4-step wizard with anti-wipe guarantee for future updates.", ConsoleColor.Green, ConsoleColor.Black);
                }
            }
        }

        private static void UpdateCountdownRow(int seconds)
        {
            if (seconds > 0)
            {
                string countText = $"Auto-booting default selection in {seconds}s... (Press any key to pause)";
                int countX = (Screen.Width - countText.Length) / 2;
                if (countX < 0) countX = 0;
                Screen.WriteClipped(countX, 23, countText, Screen.Width - countX, ConsoleColor.White, ConsoleColor.Black);
            }
            else
            {
                string pausedText = "[Manual Selection Mode Active - Auto-boot Paused]";
                int pX = (Screen.Width - pausedText.Length) / 2;
                if (pX < 0) pX = 0;
                Screen.WriteClipped(0, 23, new string(' ', Screen.Width - 1), Screen.Width - 1, ConsoleColor.Black, ConsoleColor.Black);
                Screen.WriteClipped(pX, 23, pausedText, Screen.Width - pX, ConsoleColor.DarkYellow, ConsoleColor.Black);
            }
        }

        private static void DrawOptionRow(int winLeft, int winWidth, int y, string text, bool isSelected)
        {
            if (isSelected)
            {
                string line = " > " + text;
                Screen.WriteInsideWindow(winLeft, winWidth, y, 1, line, ConsoleColor.Black, ConsoleColor.Cyan, true);
            }
            else
            {
                string line = "   " + text;
                Screen.WriteInsideWindow(winLeft, winWidth, y, 1, line, ConsoleColor.White, ConsoleColor.Black, true);
            }
        }

        private static bool CheckHardwareKey(out ConsoleKeyEx key, out char keyChar)
        {
            key = ConsoleKeyEx.NoName;
            keyChar = '\0';

            // 1. Cosmos KeyboardManager FIFO queue
            try
            {
                if (KeyboardManager.TryReadKey(out var keyEvent))
                {
                    key = keyEvent.Key;
                    keyChar = keyEvent.KeyChar;
                    return true;
                }
            }
            catch { }

            // 2. Direct 8042 PS/2 Hardware Port Polling (Ports 0x64 Status, 0x60 Data)
            // Toshiba NB100 laptop built-in keyboard sends scancodes directly to port 0x60
            try
            {
                var pStatus = new Cosmos.Core.IOPort(0x64);
                byte status = pStatus.Byte;
                // Bit 0: Output buffer full (data ready)
                if ((status & 0x01) != 0)
                {
                    var pData = new Cosmos.Core.IOPort(0x60);
                    byte scancode = pData.Byte;
                    // Bit 5: If set, data came from PS/2 mouse/touchpad - ignore to prevent jitter
                    if ((status & 0x20) == 0)
                    {
                        // Check for key-down (bit 7 clear)
                        if ((scancode & 0x80) == 0)
                        {
                            if (scancode == 0x02) { key = ConsoleKeyEx.D1; keyChar = '1'; return true; }
                            if (scancode == 0x03) { key = ConsoleKeyEx.D2; keyChar = '2'; return true; }
                            if (scancode == 0x04) { key = ConsoleKeyEx.D3; keyChar = '3'; return true; }
                            if (scancode == 0x1C) { key = ConsoleKeyEx.Enter; keyChar = '\r'; return true; }
                            if (scancode == 0x48) { key = ConsoleKeyEx.UpArrow; return true; }
                            if (scancode == 0x50) { key = ConsoleKeyEx.DownArrow; return true; }
                            if (scancode == 0x0F) { key = ConsoleKeyEx.Tab; return true; }
                            if (scancode == 0x39) { key = ConsoleKeyEx.Spacebar; keyChar = ' '; return true; }
                            if (scancode == 0x01) { key = ConsoleKeyEx.Escape; return true; }
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        private static void HardwareDelayMs(int ms)
        {
            if (ms <= 0) return;
            try
            {
                var port = new Cosmos.Core.IOPort(0x80);
                int count = ms * 200;
                for (int i = 0; i < count; i++)
                {
                    port.Byte = 0;
                }
            }
            catch
            {
                for (int i = 0; i < ms * 10000; i++) { }
            }
        }
    }
}
