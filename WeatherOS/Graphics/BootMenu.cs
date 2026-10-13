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

            while (menuRunning)
            {
                DrawMenu(selectedIndex, isInstalled);

                var keyEvent = KeyboardManager.ReadKey();

                if (keyEvent.Key == ConsoleKeyEx.UpArrow)
                {
                    selectedIndex = (selectedIndex > 0) ? selectedIndex - 1 : maxOptions - 1;
                }
                else if (keyEvent.Key == ConsoleKeyEx.DownArrow || keyEvent.Key == ConsoleKeyEx.Tab)
                {
                    selectedIndex = (selectedIndex < maxOptions - 1) ? selectedIndex + 1 : 0;
                }
                else if (keyEvent.Key == ConsoleKeyEx.D1 || keyEvent.Key == ConsoleKeyEx.Num1 || keyEvent.KeyChar == '1')
                {
                    selectedIndex = 0;
                    menuRunning = false;
                }
                else if (keyEvent.Key == ConsoleKeyEx.D2 || keyEvent.Key == ConsoleKeyEx.Num2 || keyEvent.KeyChar == '2')
                {
                    selectedIndex = 1;
                    menuRunning = false;
                }
                else if (isInstalled && (keyEvent.Key == ConsoleKeyEx.D3 || keyEvent.Key == ConsoleKeyEx.Num3 || keyEvent.KeyChar == '3'))
                {
                    selectedIndex = 2;
                    menuRunning = false;
                }
                else if (keyEvent.Key == ConsoleKeyEx.Enter)
                {
                    menuRunning = false;
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

        private static void DrawMenu(int selectedIndex, bool isInstalled)
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

            if (isInstalled)
            {
                DrawOptionRow(left, width, top + 2, "1. Boot WeatherOS from Hard Disk  (0:\\boot\\WeatherOS.bin)", selectedIndex == 0);
                DrawOptionRow(left, width, top + 3, "2. Try WeatherOS  (Live OS / RAM Mode)", selectedIndex == 1);
                DrawOptionRow(left, width, top + 4, "3. Reinstall or Repair WeatherOS on Hard Disk", selectedIndex == 2);
            }
            else
            {
                DrawOptionRow(left, width, top + 2, "1. Try WeatherOS  (Live OS / RAM Mode)", selectedIndex == 0);
                DrawOptionRow(left, width, top + 4, "2. Install WeatherOS to Hard Disk  (Setup Wizard)", selectedIndex == 1);
            }

            // Horizontal separator inside box
            Screen.WriteInsideWindow(left, width, top + 6, 0, new string('-', width - 4), ConsoleColor.DarkCyan, ConsoleColor.Black);

            // Dynamic Description Panel based on selection
            if (isInstalled)
            {
                if (selectedIndex == 0)
                {
                    Screen.WriteInsideWindow(left, width, top + 7, 2, "Description: Boot installed OS directly from FAT32 local disk.", ConsoleColor.White, ConsoleColor.Black);
                    Screen.WriteInsideWindow(left, width, top + 8, 2, "Persistent user storage, documents, and settings.ini active.", ConsoleColor.Green, ConsoleColor.Black);
                }
                else if (selectedIndex == 1)
                {
                    Screen.WriteInsideWindow(left, width, top + 7, 2, "Description: Temporary session in RAM. Disks remain untouched.", ConsoleColor.White, ConsoleColor.Black);
                    Screen.WriteInsideWindow(left, width, top + 8, 2, "Full desktop environment and diagnostic suite available.", ConsoleColor.Gray, ConsoleColor.Black);
                }
                else
                {
                    Screen.WriteInsideWindow(left, width, top + 7, 2, "Description: Launch setup wizard to reinstall or repair system files.", ConsoleColor.White, ConsoleColor.Black);
                    Screen.WriteInsideWindow(left, width, top + 8, 2, "Non-destructive update options available inside the wizard.", ConsoleColor.Cyan, ConsoleColor.Black);
                }
            }
            else
            {
                if (selectedIndex == 0)
                {
                    Screen.WriteInsideWindow(left, width, top + 7, 2, "Description: Boot into memory. Hard drives remain untouched.", ConsoleColor.White, ConsoleColor.Black);
                    Screen.WriteInsideWindow(left, width, top + 8, 2, "Full environment with desktop, live weather, network & diagnostics.", ConsoleColor.Gray, ConsoleColor.Black);
                }
                else
                {
                    Screen.WriteInsideWindow(left, width, top + 7, 2, "Description: Partition hard drive and copy WeatherOS system files.", ConsoleColor.White, ConsoleColor.Black);
                    Screen.WriteInsideWindow(left, width, top + 8, 2, "Interactive 4-step wizard with anti-wipe guarantee for future updates.", ConsoleColor.Green, ConsoleColor.Black);
                }
            }

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
            System.Console.ResetColor();
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
    }
}
