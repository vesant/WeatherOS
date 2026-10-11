using System;
using Cosmos.System;

namespace WeatherOS.Services
{
    public class TextEditorState
    {
        public string Text = "";
        public int CursorPos = 0;
        public int SelectionAnchor = -1;
        public int SelectionActive = -1;
        public bool AllowMultiline = false;
        public int MaxLength = 2048;

        public bool HasSelection => SelectionAnchor >= 0 && SelectionActive >= 0 && SelectionAnchor != SelectionActive;

        public int SelMin => HasSelection ? Math.Min(SelectionAnchor, SelectionActive) : 0;
        public int SelMax => HasSelection ? Math.Max(SelectionAnchor, SelectionActive) : 0;

        public void ClearSelection()
        {
            SelectionAnchor = -1;
            SelectionActive = -1;
        }

        public void SelectAll()
        {
            if (Text.Length > 0)
            {
                SelectionAnchor = 0;
                SelectionActive = Text.Length;
                CursorPos = Text.Length;
            }
        }

        public void SelectLeft()
        {
            if (CursorPos > 0)
            {
                if (SelectionAnchor == -1)
                {
                    SelectionAnchor = CursorPos;
                    SelectionActive = CursorPos - 1;
                }
                else
                {
                    SelectionActive = Math.Max(0, SelectionActive - 1);
                }
                CursorPos = SelectionActive;
            }
        }

        public void SelectRight()
        {
            if (CursorPos < Text.Length)
            {
                if (SelectionAnchor == -1)
                {
                    SelectionAnchor = CursorPos;
                    SelectionActive = CursorPos + 1;
                }
                else
                {
                    SelectionActive = Math.Min(Text.Length, SelectionActive + 1);
                }
                CursorPos = SelectionActive;
            }
        }

        public void DeleteSelection()
        {
            if (HasSelection)
            {
                int min = SelMin;
                int max = SelMax;
                Text = Text.Substring(0, min) + Text.Substring(max);
                CursorPos = min;
                ClearSelection();
            }
        }

        public void InsertChar(char c)
        {
            if (HasSelection)
            {
                DeleteSelection();
            }

            if (Text.Length < MaxLength)
            {
                if (CursorPos > Text.Length) CursorPos = Text.Length;
                if (CursorPos < 0) CursorPos = 0;
                Text = Text.Insert(CursorPos, c.ToString());
                CursorPos++;
            }
        }

        public void Backspace()
        {
            if (HasSelection)
            {
                DeleteSelection();
            }
            else if (CursorPos > 0 && Text.Length > 0)
            {
                if (CursorPos > Text.Length) CursorPos = Text.Length;
                Text = Text.Remove(CursorPos - 1, 1);
                CursorPos--;
            }
        }

        public void Delete()
        {
            if (HasSelection)
            {
                DeleteSelection();
            }
            else if (CursorPos < Text.Length)
            {
                Text = Text.Remove(CursorPos, 1);
            }
        }

        public bool HandleKey(KeyEvent keyEvent)
        {
            bool isCtrl = (keyEvent.Modifiers & ConsoleModifiers.Control) != 0;
            bool isShift = (keyEvent.Modifiers & ConsoleModifiers.Shift) != 0;

            // 1. Ctrl+A (Select All)
            if ((isCtrl && (keyEvent.Key == ConsoleKeyEx.A || keyEvent.KeyChar == 'a' || keyEvent.KeyChar == 'A')) || keyEvent.KeyChar == 1)
            {
                SelectAll();
                return true;
            }

            // 2. Ctrl+Left or Shift+Left (Select Left)
            if ((isCtrl || isShift) && keyEvent.Key == ConsoleKeyEx.LeftArrow)
            {
                SelectLeft();
                return true;
            }

            // 3. Ctrl+Right or Shift+Right (Select Right)
            if ((isCtrl || isShift) && keyEvent.Key == ConsoleKeyEx.RightArrow)
            {
                SelectRight();
                return true;
            }

            // 4. Regular Left Arrow (Cancel selection, move left)
            if (keyEvent.Key == ConsoleKeyEx.LeftArrow)
            {
                if (HasSelection)
                {
                    CursorPos = SelMin;
                    ClearSelection();
                }
                else if (CursorPos > 0)
                {
                    CursorPos--;
                }
                return true;
            }

            // 5. Regular Right Arrow (Cancel selection, move right)
            if (keyEvent.Key == ConsoleKeyEx.RightArrow)
            {
                if (HasSelection)
                {
                    CursorPos = SelMax;
                    ClearSelection();
                }
                else if (CursorPos < Text.Length)
                {
                    CursorPos++;
                }
                return true;
            }

            // 6. Home / End
            if (keyEvent.Key == ConsoleKeyEx.Home)
            {
                CursorPos = 0;
                ClearSelection();
                return true;
            }
            if (keyEvent.Key == ConsoleKeyEx.End)
            {
                CursorPos = Text.Length;
                ClearSelection();
                return true;
            }

            // 7. Multi-line navigation (Up / Down)
            if (AllowMultiline)
            {
                if (keyEvent.Key == ConsoleKeyEx.UpArrow)
                {
                    MoveUpLine();
                    return true;
                }
                if (keyEvent.Key == ConsoleKeyEx.DownArrow)
                {
                    MoveDownLine();
                    return true;
                }
                if (keyEvent.Key == ConsoleKeyEx.Enter)
                {
                    InsertChar('\n');
                    return true;
                }
            }

            // 8. Backspace & Delete
            if (keyEvent.Key == ConsoleKeyEx.Backspace)
            {
                Backspace();
                return true;
            }
            if (keyEvent.Key == ConsoleKeyEx.Delete)
            {
                Delete();
                return true;
            }

            // 9. Printable characters
            if (keyEvent.KeyChar >= 32 && keyEvent.KeyChar <= 126)
            {
                InsertChar(keyEvent.KeyChar);
                return true;
            }

            return false;
        }

        private void MoveUpLine()
        {
            ClearSelection();
            if (CursorPos <= 0) return;

            int curLineStart = Text.LastIndexOf('\n', Math.Max(0, CursorPos - 1));
            int col = curLineStart == -1 ? CursorPos : CursorPos - (curLineStart + 1);

            if (curLineStart > 0)
            {
                int prevLineEnd = curLineStart - 1;
                int prevLineStart = Text.LastIndexOf('\n', Math.Max(0, prevLineEnd - 1));
                int actualStart = prevLineStart == -1 ? 0 : prevLineStart + 1;
                int prevLineLen = prevLineEnd - actualStart;

                CursorPos = actualStart + Math.Min(col, prevLineLen);
            }
            else
            {
                CursorPos = 0;
            }
        }

        private void MoveDownLine()
        {
            ClearSelection();
            int curLineEnd = Text.IndexOf('\n', CursorPos);
            if (curLineEnd != -1 && curLineEnd < Text.Length - 1)
            {
                int curLineStart = Text.LastIndexOf('\n', Math.Max(0, CursorPos - 1));
                int col = curLineStart == -1 ? CursorPos : CursorPos - (curLineStart + 1);

                int nextLineStart = curLineEnd + 1;
                int nextLineEnd = Text.IndexOf('\n', nextLineStart);
                if (nextLineEnd == -1) nextLineEnd = Text.Length;
                int nextLineLen = nextLineEnd - nextLineStart;

                CursorPos = nextLineStart + Math.Min(col, nextLineLen);
            }
            else
            {
                CursorPos = Text.Length;
            }
        }

        public void DrawInline(int startX, int startY, int maxLen, ConsoleColor normalBg, ConsoleColor normalFg, ConsoleColor selBg = ConsoleColor.White, ConsoleColor selFg = ConsoleColor.Black, bool showCursor = true)
        {
            System.Console.SetCursorPosition(startX, startY);
            for (int i = 0; i < Text.Length && i < maxLen; i++)
            {
                bool isSel = HasSelection && i >= SelMin && i < SelMax;
                System.Console.BackgroundColor = isSel ? selBg : normalBg;
                System.Console.ForegroundColor = isSel ? selFg : normalFg;
                System.Console.Write(Text[i]);
            }

            if (showCursor && !HasSelection && CursorPos >= Text.Length && Text.Length < maxLen)
            {
                System.Console.BackgroundColor = normalBg;
                System.Console.ForegroundColor = ConsoleColor.Yellow;
                System.Console.Write("_");
            }

            int written = Text.Length + (showCursor && !HasSelection && CursorPos >= Text.Length ? 1 : 0);
            System.Console.BackgroundColor = normalBg;
            for (int i = written; i < maxLen; i++)
            {
                System.Console.Write(" ");
            }
        }
    }
}
