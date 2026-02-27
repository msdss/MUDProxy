using System.Text;

namespace MudProxyViewer;

/// <summary>
/// Virtual terminal screen buffer - a 2D grid of characters with colors.
/// Supports cursor positioning, scrolling, and erase operations.
/// </summary>
public sealed class ScreenBuffer
{
    public int Cols { get; private set; }
    public int Rows { get; private set; }

    // Cursor position
    public int CursorX { get; set; }
    public int CursorY { get; set; }

    // Scroll region (inclusive, 0-based)
    public int ScrollTop { get; private set; }
    public int ScrollBottom { get; private set; }

    // Current text attributes
    public ConsoleColor Fg { get; set; } = ConsoleColor.Gray;
    public ConsoleColor Bg { get; set; } = ConsoleColor.Black;
    public bool DecLineDrawing { get; set; } = false;

    private TerminalCell[,] _cells;

    // Scrollback buffer - stores lines that scroll off the top of the full screen
    private const int MaxScrollbackLines = 500;
    private readonly List<TerminalCell[]> _scrollback = new();

    public ScreenBuffer(int cols, int rows)
    {
        Cols = Math.Clamp(cols, 20, 400);
        Rows = Math.Clamp(rows, 10, 200);
        _cells = new TerminalCell[Rows, Cols];
        ScrollTop = 0;
        ScrollBottom = Rows - 1;
        ClearAll();
    }

    public void Resize(int cols, int rows)
    {
        cols = Math.Clamp(cols, 20, 400);
        rows = Math.Clamp(rows, 10, 200);

        var newCells = new TerminalCell[rows, cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                newCells[r, c] = new TerminalCell(' ', ConsoleColor.Gray, ConsoleColor.Black);

        int copyRows = Math.Min(rows, Rows);
        int copyCols = Math.Min(cols, Cols);
        for (int r = 0; r < copyRows; r++)
            for (int c = 0; c < copyCols; c++)
                newCells[r, c] = _cells[r, c];

        _cells = newCells;
        Cols = cols;
        Rows = rows;

        CursorX = Math.Clamp(CursorX, 0, Cols - 1);
        CursorY = Math.Clamp(CursorY, 0, Rows - 1);

        ScrollTop = 0;
        ScrollBottom = Rows - 1;
    }

    public TerminalCell GetCell(int row, int col)
    {
        if ((uint)row >= (uint)Rows || (uint)col >= (uint)Cols)
            return new TerminalCell(' ', ConsoleColor.Gray, ConsoleColor.Black);
        return _cells[row, col];
    }

    public void SetCell(int row, int col, char ch, ConsoleColor fg, ConsoleColor bg)
    {
        if ((uint)row >= (uint)Rows || (uint)col >= (uint)Cols) return;
        _cells[row, col] = new TerminalCell(ch, fg, bg);
    }

    public void ClearAll()
    {
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                _cells[r, c] = new TerminalCell(' ', ConsoleColor.Gray, ConsoleColor.Black);
    }

    public void ClearLine(int row, int fromCol, int toCol)
    {
        if ((uint)row >= (uint)Rows) return;
        fromCol = Math.Clamp(fromCol, 0, Cols - 1);
        toCol = Math.Clamp(toCol, 0, Cols - 1);
        if (toCol < fromCol) (fromCol, toCol) = (toCol, fromCol);

        for (int c = fromCol; c <= toCol; c++)
            _cells[row, c] = new TerminalCell(' ', Fg, Bg);
    }

    public void MoveCursorAbs(int row, int col)
    {
        CursorY = Math.Clamp(row, 0, Rows - 1);
        CursorX = Math.Clamp(col, 0, Cols - 1);
    }

    public void MoveCursorRel(int dy, int dx)
    {
        MoveCursorAbs(CursorY + dy, CursorX + dx);
    }

    public void SetScrollRegion(int top, int bottom)
    {
        top = Math.Clamp(top, 0, Rows - 1);
        bottom = Math.Clamp(bottom, 0, Rows - 1);
        if (bottom < top) (top, bottom) = (bottom, top);

        ScrollTop = top;
        ScrollBottom = bottom;
        MoveCursorAbs(0, 0);
    }

    public void PutChar(char ch)
    {
        if (ch == '\n')
        {
            NewLine();
            return;
        }
        if (ch == '\r')
        {
            CursorX = 0;
            return;
        }
        if (ch == '\b')
        {
            CursorX = Math.Max(0, CursorX - 1);
            return;
        }
        if (ch == '\t')
        {
            CursorX = Math.Min(Cols - 1, ((CursorX / 8) + 1) * 8);
            return;
        }
        if (ch == '\a') // Bell - ignore
            return;

        char drawCh = DecLineDrawing ? DecGraphicsMap(ch) : ch;
        SetCell(CursorY, CursorX, drawCh, Fg, Bg);
        CursorX++;
        if (CursorX >= Cols)
        {
            CursorX = 0;
            NewLine();
        }
    }

    private void NewLine()
    {
        int nextY = CursorY + 1;
        if (nextY > ScrollBottom)
        {
            ScrollUp(1);
            CursorY = ScrollBottom;
        }
        else
        {
            CursorY = nextY;
        }
    }

    public void ScrollUp(int lines)
    {
        lines = Math.Clamp(lines, 1, ScrollBottom - ScrollTop + 1);

        for (int _ = 0; _ < lines; _++)
        {
            // Capture the top row to scrollback before it's overwritten
            // Only when using full-screen scroll region (skip partial regions like training screen)
            if (ScrollTop == 0)
            {
                CaptureLineToScrollback(0);
            }

            for (int r = ScrollTop; r < ScrollBottom; r++)
                for (int c = 0; c < Cols; c++)
                    _cells[r, c] = _cells[r + 1, c];

            for (int c = 0; c < Cols; c++)
                _cells[ScrollBottom, c] = new TerminalCell(' ', ConsoleColor.Gray, ConsoleColor.Black);
        }
    }

    public void ScrollDown(int lines)
    {
        lines = Math.Clamp(lines, 1, ScrollBottom - ScrollTop + 1);

        for (int _ = 0; _ < lines; _++)
        {
            for (int r = ScrollBottom; r > ScrollTop; r--)
                for (int c = 0; c < Cols; c++)
                    _cells[r, c] = _cells[r - 1, c];

            for (int c = 0; c < Cols; c++)
                _cells[ScrollTop, c] = new TerminalCell(' ', ConsoleColor.Gray, ConsoleColor.Black);
        }
    }

    public void InsertLines(int n)
    {
        n = Math.Clamp(n, 1, ScrollBottom - CursorY + 1);
        if (CursorY < ScrollTop || CursorY > ScrollBottom) return;

        for (int _ = 0; _ < n; _++)
        {
            for (int r = ScrollBottom; r > CursorY; r--)
                for (int c = 0; c < Cols; c++)
                    _cells[r, c] = _cells[r - 1, c];
            for (int c = 0; c < Cols; c++)
                _cells[CursorY, c] = new TerminalCell(' ', ConsoleColor.Gray, ConsoleColor.Black);
        }
    }

    public void DeleteLines(int n)
    {
        n = Math.Clamp(n, 1, ScrollBottom - CursorY + 1);
        if (CursorY < ScrollTop || CursorY > ScrollBottom) return;

        for (int _ = 0; _ < n; _++)
        {
            for (int r = CursorY; r < ScrollBottom; r++)
                for (int c = 0; c < Cols; c++)
                    _cells[r, c] = _cells[r + 1, c];
            for (int c = 0; c < Cols; c++)
                _cells[ScrollBottom, c] = new TerminalCell(' ', ConsoleColor.Gray, ConsoleColor.Black);
        }
    }

    public void InsertChars(int n)
    {
        n = Math.Clamp(n, 1, Cols - CursorX);
        int row = CursorY;
        int start = CursorX;

        for (int c = Cols - 1; c >= start + n; c--)
            _cells[row, c] = _cells[row, c - n];
        for (int c = start; c < start + n; c++)
            _cells[row, c] = new TerminalCell(' ', Fg, Bg);
    }

    public void DeleteChars(int n)
    {
        n = Math.Clamp(n, 1, Cols - CursorX);
        int row = CursorY;
        int start = CursorX;

        for (int c = start; c < Cols - n; c++)
            _cells[row, c] = _cells[row, c + n];
        for (int c = Cols - n; c < Cols; c++)
            _cells[row, c] = new TerminalCell(' ', Fg, Bg);
    }

    // Erase in display (ED)
    public void EraseDisplay(int mode)
    {
        if (mode == 2)
        {
            ClearAll();
            return;
        }

        if (mode == 0)
        {
            // cursor to end
            ClearLine(CursorY, CursorX, Cols - 1);
            for (int r = CursorY + 1; r < Rows; r++)
                ClearLine(r, 0, Cols - 1);
            return;
        }

        if (mode == 1)
        {
            // start to cursor
            for (int r = 0; r < CursorY; r++)
                ClearLine(r, 0, Cols - 1);
            ClearLine(CursorY, 0, CursorX);
        }
    }

    // Erase in line (EL)
    public void EraseLine(int mode)
    {
        if (mode == 2) { ClearLine(CursorY, 0, Cols - 1); return; }
        if (mode == 0) { ClearLine(CursorY, CursorX, Cols - 1); return; }
        if (mode == 1) { ClearLine(CursorY, 0, CursorX); return; }
    }

    // DEC Special Graphics character mapping
    private static char DecGraphicsMap(char c) => c switch
    {
        'j' => '┘',
        'k' => '┐',
        'l' => '┌',
        'm' => '└',
        'n' => '┼',
        'q' => '─',
        't' => '├',
        'u' => '┤',
        'v' => '┴',
        'w' => '┬',
        'x' => '│',
        _ => c
    };
    
    /// <summary>
    /// Get the screen content as a string (for saving logs)
    /// </summary>
    public string GetContentAsText()
    {
        var sb = new StringBuilder();
        
        for (int row = 0; row < Rows; row++)
        {
            // Find last non-space character in row
            int lastNonSpace = -1;
            for (int col = Cols - 1; col >= 0; col--)
            {
                if (_cells[row, col].Ch != ' ' && _cells[row, col].Ch != '\0')
                {
                    lastNonSpace = col;
                    break;
                }
            }
            
            // Add characters up to last non-space
            for (int col = 0; col <= lastNonSpace; col++)
            {
                sb.Append(_cells[row, col].Ch);
            }
            
            sb.AppendLine();
        }
        
        return sb.ToString();
    }

    #region Scrollback Buffer

    /// <summary>
    /// Capture a row from the screen into the scrollback buffer, preserving colors.
    /// </summary>
    private void CaptureLineToScrollback(int row)
    {
        var line = new TerminalCell[Cols];
        for (int c = 0; c < Cols; c++)
            line[c] = _cells[row, c];

        _scrollback.Add(line);

        // Trim to max size
        if (_scrollback.Count > MaxScrollbackLines)
            _scrollback.RemoveAt(0);
    }

    /// <summary>
    /// Get a snapshot of all game text: scrollback history plus current screen contents.
    /// Returns a list of rows, each row being an array of TerminalCells.
    /// </summary>
    public List<TerminalCell[]> GetScrollbackSnapshot()
    {
        var result = new List<TerminalCell[]>(_scrollback.Count + Rows);
        
        // Add scrollback history
        result.AddRange(_scrollback);
        
        // Add current screen contents
        for (int r = 0; r < Rows; r++)
        {
            var line = new TerminalCell[Cols];
            for (int c = 0; c < Cols; c++)
                line[c] = _cells[r, c];
            result.Add(line);
        }
        
        return result;
    }

    /// <summary>
    /// Get the number of lines currently in the scrollback buffer.
    /// </summary>
    public int ScrollbackLineCount => _scrollback.Count;

    #endregion
}
