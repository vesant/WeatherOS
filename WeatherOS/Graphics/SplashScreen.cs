using System;

namespace WeatherOS.Graphics
{
    public class SplashScreen
    {
        // 100% Customizable Splash Screen logic
        public void Run()
        {
            System.Console.Clear();
            System.Console.CursorVisible = false;

            // Snow -> Rain -> Cloud -> Sun (Sun is the final icon)
            string[] icons = { "snow", "rain", "cloud", "sun" };
            int iconLeft = (80 - 16) / 2; // Exactly 32 (80 cols centered)
            int iconTop = 6;
            
            // Animation loop
            for (int i = 0; i < icons.Length; i++)
            {
                System.Console.Clear();
                DrawBigIcon(icons[i], iconLeft, iconTop);
                Delay(500);
            }

            // The Sun remains shining on screen as the final icon while OS title types out
            string line1 = "W E A T H E R";
            int startX1 = (80 - line1.Length) / 2; // Exactly 33
            int startY1 = 13;

            System.Console.SetCursorPosition(startX1, startY1);
            System.Console.ForegroundColor = ConsoleColor.Cyan;

            for (int i = 0; i < line1.Length; i++)
            {
                System.Console.Write(line1[i]);
                Delay(40); // Typing effect
            }

            string line2 = "O  S";
            int startX2 = (80 - line2.Length) / 2; // Exactly 38
            int startY2 = 15; // Placed neatly underneath line 1

            System.Console.SetCursorPosition(startX2, startY2);
            System.Console.ForegroundColor = ConsoleColor.Yellow;

            for (int i = 0; i < line2.Length; i++)
            {
                System.Console.Write(line2[i]);
                Delay(60); // Typing effect
            }

            string subTitle = "METEOROLOGICAL OPERATING SYSTEM";
            int startXSub = (80 - subTitle.Length) / 2; // Exactly 24
            int startYSub = 17;

            System.Console.SetCursorPosition(startXSub, startYSub);
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.Write(subTitle);
            
            System.Console.ResetColor();
            Delay(1200); // Hold for 1.2s to admire the final sun splash
            
            System.Console.Clear();
            System.Console.CursorVisible = true;
        }

        private void DrawBigIcon(string type, int left, int top)
        {
            if (type == "cloud")
            {
                System.Console.ForegroundColor = ConsoleColor.DarkGray;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("      .--.      ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("   .-(    ).    ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("  (___.__)__)   ");
            }
            else if (type == "rain")
            {
                System.Console.ForegroundColor = ConsoleColor.Blue;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("   .-^^---.     ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("  (________)    ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("   /  /  /      ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("  /  /  /       ");
            }
            else if (type == "snow")
            {
                System.Console.ForegroundColor = ConsoleColor.White;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("   .-^^---.     ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("  (________)    ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("   *  *  *      ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("  *  *  *       ");
            }
            else if (type == "sun")
            {
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.SetCursorPosition(left, top);     System.Console.Write("    \\   /      ");
                System.Console.SetCursorPosition(left, top + 1); System.Console.Write("     .-.        ");
                System.Console.SetCursorPosition(left, top + 2); System.Console.Write("  --(   )--     ");
                System.Console.SetCursorPosition(left, top + 3); System.Console.Write("     `-'        ");
                System.Console.SetCursorPosition(left, top + 4); System.Console.Write("    /   \\      ");
            }
            System.Console.ResetColor();
        }

        // Safe delay for 32-bit Bare-Metal
        private void Delay(int ms)
        {
            System.Threading.Thread.Sleep(ms);
        }
    }
}
