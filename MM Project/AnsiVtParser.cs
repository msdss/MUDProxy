using System.Text;

namespace MudProxyViewer;

/// <summary>
/// ANSI/VT100 escape sequence parser.
/// Interprets escape sequences and updates the screen buffer accordingly.
/// </summary>
public sealed class AnsiVtParser
{
    private const byte ESC = 0x1B;
    private readonly ScreenBuffer _s;

    private enum State { Text, Esc, Csi, Charset }
    private State _state = State.Text;

    private readonly List<int> _params = new();
    private int _paramAcc = -1;
    private char _charsetWhich = '\0';

    public AnsiVtParser(ScreenBuffer screen)
    {
        _s = screen;
    }

    public void Feed(byte[] bytes)
    {
        Feed(bytes.AsSpan());
    }

    public void Feed(ReadOnlySpan<byte> bytes)
    {
        // Decode as CP437 (common for BBS)
        string text;
        try
        {
            text = Encoding.GetEncoding(437).GetString(bytes);
        }
        catch
        {
            text = Encoding.Latin1.GetString(bytes);
        }

        foreach (char ch in text)
        {
            byte b = (byte)ch;

            switch (_state)
            {
                case State.Text:
                    if (b == ESC)
                        _state = State.Esc;
                    else
                        _s.PutChar(ch);
                    break;

                case State.Esc:
                    if (ch == '[')
                    {
                        EnterCsi();
                    }
                    else if (ch == '(' || ch == ')')
                    {
                        _charsetWhich = ch;
                        _state = State.Charset;
                    }
                    else if (ch == 'M')
                    {
                        // Reverse index - move cursor up, scroll if at top
                        if (_s.CursorY == _s.ScrollTop)
                            _s.ScrollDown(1);
                        else
                            _s.MoveCursorRel(-1, 0);
                        _state = State.Text;
                    }
                    else if (ch == 'D')
                    {
                        // Index - move cursor down, scroll if at bottom
                        if (_s.CursorY == _s.ScrollBottom)
                            _s.ScrollUp(1);
                        else
                            _s.MoveCursorRel(1, 0);
                        _state = State.Text;
                    }
                    else if (ch == 'E')
                    {
                        // Next line
                        _s.CursorX = 0;
                        if (_s.CursorY == _s.ScrollBottom)
                            _s.ScrollUp(1);
                        else
                            _s.MoveCursorRel(1, 0);
                        _state = State.Text;
                    }
                    else if (ch == 'H')
                    {
                        // Tab set - ignore
                        _state = State.Text;
                    }
                    else if (ch == '7')
                    {
                        // Save cursor - not implemented
                        _state = State.Text;
                    }
                    else if (ch == '8')
                    {
                        // Restore cursor - not implemented
                        _state = State.Text;
                    }
                    else if (ch == 'c')
                    {
                        // Reset terminal
                        _s.ClearAll();
                        _s.MoveCursorAbs(0, 0);
                        _s.Fg = ConsoleColor.Gray;
                        _s.Bg = ConsoleColor.Black;
                        _s.DecLineDrawing = false;
                        _state = State.Text;
                    }
                    else
                    {
                        // Unknown escape
                        _state = State.Text;
                    }
                    break;

                case State.Charset:
                    if (_charsetWhich == '(')
                    {
                        if (ch == '0') _s.DecLineDrawing = true;
                        else if (ch == 'B') _s.DecLineDrawing = false;
                    }
                    _state = State.Text;
                    break;

                case State.Csi:
                    HandleCsiChar(ch);
                    break;
            }
        }
    }

    private void EnterCsi()
    {
        _params.Clear();
        _paramAcc = -1;
        _state = State.Csi;
    }

    private void PushParam()
    {
        if (_paramAcc < 0) _params.Add(0);
        else _params.Add(_paramAcc);
        _paramAcc = -1;
    }

    private int P(int idx, int def)
    {
        if (idx < 0 || idx >= _params.Count) return def;
        return _params[idx];
    }

    private void HandleCsiChar(char ch)
    {
        // Parse digits/semicolons
        if (ch >= '0' && ch <= '9')
        {
            int d = ch - '0';
            _paramAcc = (_paramAcc < 0) ? d : (_paramAcc * 10 + d);
            return;
        }

        if (ch == ';')
        {
            PushParam();
            return;
        }

        // Final byte
        PushParam();

        switch (ch)
        {
            // Cursor positioning
            case 'H':
            case 'f':
                {
                    int row = P(0, 1) - 1;
                    int col = P(1, 1) - 1;
                    _s.MoveCursorAbs(row, col);
                    break;
                }
            case 'A': _s.MoveCursorRel(-P(0, 1), 0); break;  // CUU - up
            case 'B': _s.MoveCursorRel(P(0, 1), 0); break;   // CUD - down
            case 'C': _s.MoveCursorRel(0, P(0, 1)); break;   // CUF - forward
            case 'D': _s.MoveCursorRel(0, -P(0, 1)); break;  // CUB - back
            case 'E': // CNL - cursor next line
                _s.CursorX = 0;
                _s.MoveCursorRel(P(0, 1), 0);
                break;
            case 'F': // CPL - cursor previous line
                _s.CursorX = 0;
                _s.MoveCursorRel(-P(0, 1), 0);
                break;
            case 'G': // CHA - cursor horizontal absolute
                {
                    int col = P(0, 1) - 1;
                    _s.MoveCursorAbs(_s.CursorY, col);
                    break;
                }
            case 'd': // VPA - vertical position absolute
                {
                    int row = P(0, 1) - 1;
                    _s.MoveCursorAbs(row, _s.CursorX);
                    break;
                }

            // Erase
            case 'J': _s.EraseDisplay(P(0, 0)); break;
            case 'K': _s.EraseLine(P(0, 0)); break;

            // Scroll region
            case 'r':
                {
                    if (_params.Count >= 2)
                    {
                        int top = P(0, 1) - 1;
                        int bot = P(1, _s.Rows) - 1;
                        _s.SetScrollRegion(top, bot);
                    }
                    else
                    {
                        _s.SetScrollRegion(0, _s.Rows - 1);
                    }
                    break;
                }

            // Insert/Delete line
            case 'L': _s.InsertLines(P(0, 1)); break;
            case 'M': _s.DeleteLines(P(0, 1)); break;

            // Insert/Delete char
            case '@': _s.InsertChars(P(0, 1)); break;
            case 'P': _s.DeleteChars(P(0, 1)); break;

            // Scroll
            case 'S': _s.ScrollUp(P(0, 1)); break;
            case 'T': _s.ScrollDown(P(0, 1)); break;

            // SGR - colors
            case 'm':
                ApplySgr(_params);
                break;

            // Modes (ignored but parsed)
            case 'h':
            case 'l':
                // Set/reset mode - ignore
                break;

            case 'n':
                // Device status report - ignore
                break;

            case 's':
                // Save cursor position - not implemented
                break;

            case 'u':
                // Restore cursor position - not implemented
                break;

            default:
                // Unknown CSI command - ignore
                break;
        }

        _state = State.Text;
    }

    private void ApplySgr(List<int> ps)
    {
        if (ps.Count == 0) ps.Add(0);

        bool bold = false;

        for (int i = 0; i < ps.Count; i++)
        {
            int p = ps[i];

            if (p == 0)
            {
                _s.Fg = ConsoleColor.Gray;
                _s.Bg = ConsoleColor.Black;
                bold = false;
            }
            else if (p == 1)
            {
                bold = true;
            }
            else if (p == 22)
            {
                bold = false;
            }
            else if (p == 7)
            {
                // Reverse video
                (_s.Fg, _s.Bg) = (_s.Bg, _s.Fg);
            }
            else if (p == 27)
            {
                // Reverse off - swap back
                (_s.Fg, _s.Bg) = (_s.Bg, _s.Fg);
            }
            else if (30 <= p && p <= 37)
            {
                _s.Fg = Ansi16ToConsoleColor(p - 30, bold);
            }
            else if (p == 39)
            {
                _s.Fg = ConsoleColor.Gray;
            }
            else if (40 <= p && p <= 47)
            {
                _s.Bg = Ansi16ToConsoleColor(p - 40, false);
            }
            else if (p == 49)
            {
                _s.Bg = ConsoleColor.Black;
            }
            else if (90 <= p && p <= 97)
            {
                _s.Fg = Ansi16ToConsoleColor(p - 90, true);
            }
            else if (100 <= p && p <= 107)
            {
                _s.Bg = Ansi16ToConsoleColor(p - 100, true);
            }
        }
    }

    private static ConsoleColor Ansi16ToConsoleColor(int ansi, bool bright)
    {
        ansi = Math.Clamp(ansi, 0, 7);

        return (ansi, bright) switch
        {
            (0, false) => ConsoleColor.Black,
            (1, false) => ConsoleColor.DarkRed,
            (2, false) => ConsoleColor.DarkGreen,
            (3, false) => ConsoleColor.DarkYellow,
            (4, false) => ConsoleColor.DarkBlue,
            (5, false) => ConsoleColor.DarkMagenta,
            (6, false) => ConsoleColor.DarkCyan,
            (7, false) => ConsoleColor.Gray,

            (0, true) => ConsoleColor.DarkGray,
            (1, true) => ConsoleColor.Red,
            (2, true) => ConsoleColor.Green,
            (3, true) => ConsoleColor.Yellow,
            (4, true) => ConsoleColor.Blue,
            (5, true) => ConsoleColor.Magenta,
            (6, true) => ConsoleColor.Cyan,
            (7, true) => ConsoleColor.White,

            _ => ConsoleColor.Gray
        };
    }
}
