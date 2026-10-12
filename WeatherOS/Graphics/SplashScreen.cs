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

            int iconLeft = (80 - 16) / 2; // Exactly 32
            int iconTop = 6;

            // 1. Cycle stormy weather first: Snow -> Rain -> Cloud
            string[] icons = { "snow", "rain", "cloud" };
            for (int i = 0; i < icons.Length; i++)
            {
                System.Console.Clear();
                DrawBigIcon(icons[i], iconLeft, iconTop);
                Delay(450);
            }

            // 2. Clear icon area and type perfectly centered title & subtitle
            System.Console.Clear();

            string title = "W E A T H E R   O S";
            int startX = (80 - title.Length) / 2; // Exactly 30 (80 columns centered)
            int startY = 13;

            System.Console.SetCursorPosition(startX, startY);
            System.Console.ForegroundColor = ConsoleColor.Cyan;

            for (int i = 0; i < title.Length; i++)
            {
                System.Console.Write(title[i]);
                Delay(35);
            }

            string subTitle = "METEOROLOGICAL OPERATING SYSTEM";
            int startXSub = (80 - subTitle.Length) / 2; // Exactly 24 (80 columns centered)
            int startYSub = 15;

            System.Console.SetCursorPosition(startXSub, startYSub);
            System.Console.ForegroundColor = ConsoleColor.DarkGray;
            System.Console.Write(subTitle);

            Delay(300);

            // 3. SUN APPEARS LAST: The golden sun shines down above the title
            DrawBigIcon("sun", iconLeft, iconTop);

            System.Console.ResetColor();
            Delay(1500); // Admire the sun before transitioning to desktop

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
