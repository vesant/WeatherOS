using System;
using Cosmos.System;

namespace WeatherOS.Graphics
{
    public enum BootMode
    {
        LiveOS,
        InstallToDisk
    }

    public static class BootMenu
    {
        public static BootMode Show()
        {
            int selectedIndex = 0; // 0 = Live OS, 1 = Install to Disk
            bool menuRunning = true;

            System.Console.CursorVisible = false;

            while (menuRunning)
            {
                DrawMenu(selectedIndex);

                var keyEvent = KeyboardManager.ReadKey();

                if (keyEvent.Key == ConsoleKeyEx.UpArrow || keyEvent.Key == ConsoleKeyEx.DownArrow || keyEvent.Key == ConsoleKeyEx.Tab)
                {
                    selectedIndex = (selectedIndex == 0) ? 1 : 0;
                }
                else if (keyEvent.Key == ConsoleKeyEx.D1 || keyEvent.Key == ConsoleKeyEx.Num1 || keyEvent.KeyChar == '1')
                {
                    selectedIndex = 0;
                    DrawMenu(selectedIndex);
                    menuRunning = false;
                }
                else if (keyEvent.Key == ConsoleKeyEx.D2 || keyEvent.Key == ConsoleKeyEx.Num2 || keyEvent.KeyChar == '2')
                {
                    selectedIndex = 1;
                    DrawMenu(selectedIndex);
                    ShowInstallerNoticeDialog();
                }
                else if (keyEvent.Key == ConsoleKeyEx.Enter)
                {
                    if (selectedIndex == 0)
                    {
                        menuRunning = false;
                    }
                    else
                    {
                        ShowInstallerNoticeDialog();
                    }
                }
            }

            System.Console.Clear();
            System.Console.CursorVisible = true;
            return BootMode.LiveOS;
        }

        private static void DrawMenu(int selectedIndex)
        {
            System.Console.BackgroundColor = ConsoleColor.Black;
            System.Console.Clear();

            // 1. Top Header Bar (Row 0)
            string topBar = " WeatherOS Boot Selection Manager v3.0                 [x86 Bare-Metal Edition] ";
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
            string sub = "Dedicated Meteorological Operating System Bootloader";
            int subX = (Screen.Width - sub.Length) / 2;
            Screen.WriteClipped(subX, 8, sub, Screen.Width - subX, ConsoleColor.DarkGray, ConsoleColor.Black);

            // 4. Selection Frame (Rows 10 to 20, columns 5 to 74)
            int left = 5;
            int top = 10;
            int width = 70;
            int height = 11;

            Screen.DrawBox(left, top, width, height, "BOOT TARGET SELECTION", ConsoleColor.Black, ConsoleColor.DarkCyan, ConsoleColor.Yellow);

            // Option 1: Live OS
            bool sel0 = (selectedIndex == 0);
            DrawOptionRow(left, width, top + 2, "1. Try WeatherOS  (Live OS / RAM Mode)", sel0);

            // Option 2: Install to Disk
            bool sel1 = (selectedIndex == 1);
            DrawOptionRow(left, width, top + 4, "2. Install WeatherOS to Hard Disk  (Setup Wizard)", sel1);

            // Horizontal separator inside box
            Screen.WriteInsideWindow(left, width, top + 6, 0, new string('-', width - 4), ConsoleColor.DarkCyan, ConsoleColor.Black);

            // Dynamic Description Panel based on selection
            if (selectedIndex == 0)
            {
                Screen.WriteInsideWindow(left, width, top + 7, 2, "Description: Boot into memory. Hard drives remain untouched.", ConsoleColor.White, ConsoleColor.Black);
                Screen.WriteInsideWindow(left, width, top + 8, 2, "Full environment with desktop, live weather, network & diagnostics.", ConsoleColor.Gray, ConsoleColor.Black);
            }
            else
            {
                Screen.WriteInsideWindow(left, width, top + 7, 2, "Description: Partition hard drive and copy WeatherOS system files.", ConsoleColor.White, ConsoleColor.Black);
                Screen.WriteInsideWindow(left, width, top + 8, 2, "[Notice: Hard disk installer is currently under active development]", ConsoleColor.DarkYellow, ConsoleColor.Black);
            }

            // 5. Instruction line (Row 22)
            string instruct = "[Up/Dn / Tab] Select Option   |   [1 / 2] Direct Key   |   [ENTER] Boot";
            int insX = (Screen.Width - instruct.Length) / 2;
            if (insX < 0) insX = 0;
            Screen.WriteClipped(insX, 22, instruct, Screen.Width - insX, ConsoleColor.Yellow, ConsoleColor.Black);

            // 6. Bottom Status Bar (Row 24)
            string btm = " WeatherOS Kernel 3.0  |  Hardware Target: x86 PC  |  Default: Live OS ";
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

        private static void ShowInstallerNoticeDialog()
        {
            int left, top, width, height;
            Screen.CalculateWindow(62, 11, out left, out top, out width, out height);

            Screen.DrawBox(left, top, width, height, "HARD DISK INSTALLER - NOTICE", ConsoleColor.DarkBlue, ConsoleColor.White, ConsoleColor.Yellow);

            Screen.WriteInsideWindow(left, width, top + 2, 2, "[!] Notice: Hard Disk Installation Wizard", ConsoleColor.Yellow, ConsoleColor.DarkBlue);
            Screen.WriteInsideWindow(left, width, top + 4, 2, "The permanent disk installation wizard and partitioner are", ConsoleColor.White, ConsoleColor.DarkBlue);
            Screen.WriteInsideWindow(left, width, top + 5, 2, "currently being finalized for bare-metal targets.", ConsoleColor.White, ConsoleColor.DarkBlue);
            Screen.WriteInsideWindow(left, width, top + 7, 2, "Please select [1. Live OS] to test and run WeatherOS in RAM.", ConsoleColor.Cyan, ConsoleColor.DarkBlue);
            Screen.WriteInsideWindow(left, width, top + 9, 2, "         >> Press ANY KEY to return to Boot Menu <<", ConsoleColor.Green, ConsoleColor.DarkBlue);

            System.Console.ResetColor();
            KeyboardManager.ReadKey();
        }
    }
}
