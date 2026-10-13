using System;

namespace WeatherOS.Graphics
{
    /// <summary>
    /// Centralized Screen and TUI Layout Manager.
    /// Computes display boundaries, enforces box constraints, and prevents console wraps/overflows.
    /// </summary>
    public static class Screen
    {
        // Standard VGA text mode dimensions
        public const int DefaultWidth = 80;
        public const int DefaultHeight = 25;

        /// <summary>
        /// Total usable screen columns (always 80 in standard Cosmos VGA text mode).
        /// </summary>
        public static int Width
        {
            get
            {
                try
                {
                    int w = System.Console.WindowWidth;
                    if (w >= 40 && w <= 200) return w;
                }
                catch { }
                return DefaultWidth;
            }
        }

        /// <summary>
        /// Total usable screen rows (always 25 in standard Cosmos VGA text mode).
        /// </summary>
        public static int Height
        {
            get
            {
                try
                {
                    int h = System.Console.WindowHeight;
                    if (h >= 20 && h <= 100) return h;
                }
                catch { }
                return DefaultHeight;
            }
        }

        /// <summary>
        /// Computes dynamic window dimensions based on semantic sizing presets.
        /// Scales relative to the physical screen (Small: ~60%, Medium: ~78%, Large: ~90%, FullScreen: ~96%).
        /// </summary>
        public static void CalculatePresetDimensions(WindowSize size, out int width, out int height)
        {
            switch (size)
            {
                case WindowSize.Small:
                    width = 48;
                    height = 11;
                    break;
                case WindowSize.Medium:
                    width = 62;
                    height = 15;
                    break;
                case WindowSize.Large:
                    width = 72;
                    height = 19;
                    break;
                case WindowSize.FullScreen:
                default:
                    width = 76;
                    height = 22;
                    break;
            }
        }

        /// <summary>
        /// Automatically calculates safe, centered window boundaries fitting entirely inside the display.
        /// </summary>
        public static void CalculateWindow(int requestedWidth, int requestedHeight, out int left, out int top, out int width, out int height)
        {
            int maxW = Width - 2;   // leave 1 column on left and right borders
            int maxH = Height - 2;  // leave row 0 (top bar) and row 24 (bottom bar)

            width = requestedWidth;
            if (width > maxW) width = maxW;
            if (width < 20) width = Math.Min(20, maxW);

            height = requestedHeight;
            if (height > maxH) height = maxH;
            if (height < 4) height = Math.Min(4, maxH);

            left = (Width - width) / 2;
            if (left < 1) left = 1;

            top = 1 + (maxH - height) / 2;
            if (top < 1) top = 1;
        }

        /// <summary>
        /// Replaces multi-byte Unicode characters that fail in BIOS CP437 VGA text hardware with clean ASCII.
        /// Prevents question marks ('?') and layout displacement.
        /// </summary>
        public static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            return text
                .Replace("▲", "^")
                .Replace("▼", "v")
                .Replace("◄", "<")
                .Replace("►", ">")
                .Replace("█", "#")
                .Replace("─", "-")
                .Replace("│", "|")
                .Replace("┌", "+")
                .Replace("┐", "+")
                .Replace("└", "+")
                .Replace("┘", "+");
        }

        /// <summary>
        /// Writes text strictly clipped to maxAllowedWidth and screen boundaries.
        /// NEVER wraps to the next line and NEVER exceeds maxAllowedWidth.
        /// </summary>
        public static void WriteClipped(int x, int y, string text, int maxAllowedWidth, ConsoleColor? fg = null, ConsoleColor? bg = null, bool padToWidth = false)
        {
            if (y < 0 || y >= Height) return;
            if (x < 0 || x >= Width) return;

            string safe = Sanitize(text);

            // Maximum columns physically available on screen without triggering a line-wrap
            int screenRemaining = (Width - 1) - x;
            int limit = Math.Min(maxAllowedWidth, screenRemaining);
            if (limit <= 0) return;

            if (safe.Length > limit)
            {
                safe = safe.Substring(0, limit);
            }

            if (padToWidth && safe.Length < limit)
            {
                safe = safe.PadRight(limit);
            }

            if (fg.HasValue) System.Console.ForegroundColor = fg.Value;
            if (bg.HasValue) System.Console.BackgroundColor = bg.Value;

            System.Console.SetCursorPosition(x, y);
            System.Console.Write(safe);
        }

        /// <summary>
        /// Writes content strictly INSIDE a window box without touching or overlapping the side borders.
        /// Guarantees that highlighted selection bars or long texts will never bleed out of the window.
        /// </summary>
        public static void WriteInsideWindow(int winLeft, int winWidth, int y, int padLeft, string text, ConsoleColor? fg = null, ConsoleColor? bg = null, bool fillRemaining = false)
        {
            int innerStart = winLeft + 1 + padLeft;
            // The available interior span between left border and right border:
            // winLeft + 1 to winLeft + winWidth - 2
            int innerSpan = winWidth - 2 - padLeft - 1; // 1 space margin before right border '|'
            if (innerSpan <= 0) return;

            WriteClipped(innerStart, y, text, innerSpan, fg, bg, fillRemaining);
        }

        /// <summary>
        /// Clears an entire line across the screen safely.
        /// </summary>
        public static void ClearLine(int y, ConsoleColor bg)
        {
            if (y < 0 || y >= Height) return;

            System.Console.BackgroundColor = bg;
            System.Console.SetCursorPosition(0, y);

            // Write 79 spaces then the 80th character without wrapping
            if (y == Height - 1)
            {
                System.Console.Write(new string(' ', Width - 1));
            }
            else
            {
                // For lines 0 to 23, write full width
                System.Console.Write(new string(' ', Width - 1));
                System.Console.Write(' ');
            }
        }

        /// <summary>
        /// Draws a perfectly constrained ASCII window with top, bottom, and side borders.
        /// </summary>
        public static void DrawBox(int left, int top, int width, int height, string title, ConsoleColor winBg, ConsoleColor borderCol, ConsoleColor titleCol)
        {
            // Boundary checks
            if (left < 0) left = 0;
            if (top < 0) top = 0;
            if (left + width > Width) width = Width - left;
            if (top + height > Height) height = Height - top;
            if (width < 4 || height < 3) return;

            System.Console.BackgroundColor = winBg;
            System.Console.ForegroundColor = borderCol;

            // Top border
            System.Console.SetCursorPosition(left, top);
            System.Console.Write("+");
            System.Console.Write(new string('-', width - 2));
            System.Console.Write("+");

            // Title safely fitted within top border
            if (!string.IsNullOrEmpty(title))
            {
                string safeTitle = " " + Sanitize(title).Trim() + " ";
                int maxTitleLen = width - 4;
                if (safeTitle.Length > maxTitleLen)
                {
                    safeTitle = safeTitle.Substring(0, maxTitleLen);
                }
                int titleX = left + 2;
                System.Console.SetCursorPosition(titleX, top);
                System.Console.ForegroundColor = titleCol;
                System.Console.Write(safeTitle);
            }

            // Interior rows and side borders
            string innerSpaces = new string(' ', width - 2);
            for (int y = top + 1; y < top + height - 1; y++)
            {
                System.Console.SetCursorPosition(left, y);
                System.Console.ForegroundColor = borderCol;
                System.Console.Write("|");
                System.Console.Write(innerSpaces);
                System.Console.Write("|");
            }

            // Bottom border
            System.Console.SetCursorPosition(left, top + height - 1);
            System.Console.ForegroundColor = borderCol;
            System.Console.Write("+");
            System.Console.Write(new string('-', width - 2));
            System.Console.Write("+");
        }
    }
}
