using System;

namespace WeatherOS.Graphics
{
    /// <summary>
    /// Semantic window sizing presets computed dynamically against the physical screen.
    /// </summary>
    public enum WindowSize
    {
        Small,      // Compact dialogs (About, Clock, Input prompts) ~48x11
        Medium,     // Standard control panels (Settings, Debug, Network Config) ~62x15
        Large,      // Rich workspaces (System Resources, Dashboard, Notepad) ~72x19
        FullScreen  // Maximum viewport (Launcher, 7-Day Forecast, Storage Explorer) ~76x22
    }

    /// <summary>
    /// Global TUI Window & Layout Engine.
    /// Eliminates hardcoded coordinates across apps by computing all dimensions, margins,
    /// relative rows, and boundary clippings centrally.
    /// </summary>
    public class TuiWindow
    {
        public string Title { get; set; }

        public int Left { get; private set; }
        public int Top { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }

        public int InnerLeft => Left + 1;
        public int InnerTop => Top + 1;
        public int InnerWidth => Math.Max(0, Width - 2);
        public int InnerHeight => Math.Max(0, Height - 2);

        /// <summary>
        /// Absolute console line reserved for bottom navigation and shortcut prompts.
        /// </summary>
        public int FooterRow => Top + Height - 2;

        public ConsoleColor Background { get; set; }
        public ConsoleColor BorderColor { get; set; }
        public ConsoleColor TitleColor { get; set; }

        /// <summary>
        /// Creates a window positioned and sized dynamically using semantic screen calculations.
        /// </summary>
        public TuiWindow(string title, WindowSize size, ConsoleColor bg, ConsoleColor borderCol = ConsoleColor.DarkCyan, ConsoleColor titleCol = ConsoleColor.Yellow)
        {
            Title = title;
            Background = bg;
            BorderColor = borderCol;
            TitleColor = titleCol;

            int reqW, reqH;
            Screen.CalculatePresetDimensions(size, out reqW, out reqH);

            int left, top, width, height;
            Screen.CalculateWindow(reqW, reqH, out left, out top, out width, out height);

            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        /// <summary>
        /// Creates a window with preferred dimensions that are automatically constrained to safe screen boundaries.
        /// </summary>
        public TuiWindow(string title, int preferredWidth, int preferredHeight, ConsoleColor bg, ConsoleColor borderCol = ConsoleColor.DarkCyan, ConsoleColor titleCol = ConsoleColor.Yellow)
        {
            Title = title;
            Background = bg;
            BorderColor = borderCol;
            TitleColor = titleCol;

            int left, top, width, height;
            Screen.CalculateWindow(preferredWidth, preferredHeight, out left, out top, out width, out height);

            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        /// <summary>
        /// Draws the frame, title, side borders, and clears the interior area.
        /// </summary>
        public void DrawFrame()
        {
            Screen.DrawBox(Left, Top, Width, Height, Title, Background, BorderColor, TitleColor);
        }

        /// <summary>
        /// Writes text on a relative interior row (relY = 0 is the first line inside the box).
        /// Automatically clips text so it never touches or exceeds the right border.
        /// </summary>
        public void WriteRow(int relY, string text, ConsoleColor fg, int padLeft = 1, ConsoleColor? bg = null, bool fillRemaining = false)
        {
            int y = InnerTop + relY;
            if (y > FooterRow) return;

            Screen.WriteInsideWindow(Left, Width, y, padLeft, text, fg, bg ?? Background, fillRemaining);
        }

        /// <summary>
        /// Writes an interactive menu or selection row that spans the full interior width with highlighting.
        /// It is mathematically impossible for the selection bar to bleed past the right window border.
        /// </summary>
        public void WriteSelectionRow(int relY, string text, bool isSelected, int padLeft = 1, ConsoleColor? unselectedFg = null)
        {
            int y = InnerTop + relY;
            if (y > FooterRow) return;

            ConsoleColor fg = isSelected ? ConsoleColor.Black : (unselectedFg ?? ConsoleColor.White);
            ConsoleColor bg = isSelected ? ConsoleColor.White : Background;
            string prefix = isSelected ? " > " : "   ";

            Screen.WriteInsideWindow(Left, Width, y, padLeft, prefix + text, fg, bg, true);
        }

        /// <summary>
        /// Draws an interior horizontal divider across the window.
        /// </summary>
        public void WriteSeparator(int relY)
        {
            int y = InnerTop + relY;
            if (y > FooterRow) return;

            string sep = new string('-', InnerWidth - 2);
            Screen.WriteInsideWindow(Left, Width, y, 1, sep, BorderColor, Background, false);
        }

        /// <summary>
        /// Writes keybind hints on the bottom reserved footer row.
        /// </summary>
        public void WriteFooter(string shortcutHints, ConsoleColor fg = ConsoleColor.Yellow)
        {
            Screen.WriteInsideWindow(Left, Width, FooterRow, 1, shortcutHints, fg, Background, false);
        }

        /// <summary>
        /// Writes text at a relative (X, Y) coordinate within the window interior with boundary enforcement.
        /// </summary>
        public void WriteAt(int relX, int relY, string text, ConsoleColor fg, bool padToWidth = false)
        {
            int x = InnerLeft + relX;
            int y = InnerTop + relY;
            if (y > FooterRow) return;

            int maxSpan = (Left + Width - 2) - x;
            if (maxSpan <= 0) return;

            Screen.WriteClipped(x, y, text, maxSpan, fg, Background, padToWidth);
        }

        private static readonly string _barFilled = "====================";
        private static readonly string _barEmpty  = "                    ";

        /// <summary>
        /// Draws a progress / resource bar at relative coordinates within the window safely.
        /// </summary>
        public void DrawBar(int relX, int relY, string label, int percentage, ConsoleColor color)
        {
            int y = InnerTop + relY;
            if (y > FooterRow) return;

            int clampedPct = Math.Max(0, Math.Min(100, percentage));
            int bars = clampedPct / 5; // 0 to 20
            if (bars > 20) bars = 20;

            string labelPart = (label + " : ").PadRight(11);
            WriteAt(relX, relY, labelPart + "[", ConsoleColor.White);

            int barRelX = relX + labelPart.Length + 1;
            if (bars > 0)
            {
                WriteAt(barRelX, relY, _barFilled.Substring(0, bars), color);
            }
            if (bars < 20)
            {
                WriteAt(barRelX + bars, relY, _barEmpty.Substring(0, 20 - bars), ConsoleColor.DarkGray);
            }
            WriteAt(barRelX + 20, relY, "] " + clampedPct.ToString().PadLeft(3) + "%", ConsoleColor.White);
        }
    }
}
