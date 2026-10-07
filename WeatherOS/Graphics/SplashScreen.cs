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

            string[] icons = { "sun", "cloud", "rain", "snow" };
            
            // Animation loop (Approx 3 seconds total)
            // 4 icons, each shown for 500ms = 2.0s
            // Then the final icon stays while OS name types out = 1.0s
            
            for (int i = 0; i < icons.Length; i++)
            {
                System.Console.Clear();
                DrawBigIcon(icons[i], 32, 8);
                Delay(500);
            }

            // Keep the last icon (snow) and type the OS name from left to right
            string osName = "W E A T H E R   O S";
            int startX = 40 - (osName.Length / 2);
            int startY = 15;

            System.Console.SetCursorPosition(startX, startY);
            System.Console.ForegroundColor = ConsoleColor.Cyan;

            for (int i = 0; i < osName.Length; i++)
            {
                System.Console.Write(osName[i]);
                Delay(50); // Typing effect
            }
            
            System.Console.ResetColor();
            Delay(1000); // Hold for 1 second
            
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
