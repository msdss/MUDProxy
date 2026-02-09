using System.Text;

namespace MudProxyViewer;

/// <summary>
/// Custom terminal control that paints a ScreenBuffer directly using GDI+.
/// Replaces RichTextBox for proper VT100/ANSI terminal emulation.
/// </summary>
public sealed class TerminalControl : UserControl
{
    private ScreenBuffer? _screenBuffer;
    private readonly StringBuilder _inputBuffer = new();
    private int _charWidth = 8;
    private int _charHeight = 16;
    private bool _cursorVisible = true;
    private readonly System.Windows.Forms.Timer _cursorTimer;
    
    // Events
    public event Action<string>? OnCommandEntered;
    public event Action<int, int>? OnTerminalSizeChanged;
    public event Action<byte[]>? OnRawKeyData;  // For pass-through mode (training screen)
    
    // Properties
    public int TerminalCols { get; private set; } = 80;
    public int TerminalRows { get; private set; } = 24;
    public string InputText => _inputBuffer.ToString();
    
    /// <summary>
    /// When true, keystrokes are sent directly to server instead of buffered locally.
    /// Used for training screen where server handles input fields.
    /// </summary>
    public bool PassThroughMode { get; set; } = false;
    
    public TerminalControl()
    {
        // Enable double buffering to reduce flicker
        SetStyle(ControlStyles.AllPaintingInWmPaint | 
                 ControlStyles.UserPaint | 
                 ControlStyles.DoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable, true);
        
        BackColor = Color.Black;
        Font = new Font("Consolas", 10f, FontStyle.Regular);
        
        // Cursor blink timer
        _cursorTimer = new System.Windows.Forms.Timer();
        _cursorTimer.Interval = 530;  // Standard cursor blink rate
        _cursorTimer.Tick += (s, e) => 
        {
            _cursorVisible = !_cursorVisible;
            InvalidateCursorArea();
        };
        _cursorTimer.Start();
        
        // Calculate initial character dimensions
        UpdateCharacterDimensions();
    }
    
    /// <summary>
    /// Set the screen buffer to render
    /// </summary>
    public void SetScreenBuffer(ScreenBuffer buffer)
    {
        _screenBuffer = buffer;
        Invalidate();
    }
    
    /// <summary>
    /// Clear the input buffer
    /// </summary>
    public void ClearInput()
    {
        _inputBuffer.Clear();
        InvalidateCursorArea();
    }
    
    /// <summary>
    /// Force a full repaint
    /// </summary>
    public void InvalidateTerminal()
    {
        Invalidate();
    }
    
    /// <summary>
    /// Update character dimensions based on current font
    /// </summary>
    private void UpdateCharacterDimensions()
    {
        using (var g = CreateGraphics())
        {
            // Measure character size using TextRenderer for accuracy
            var size = TextRenderer.MeasureText(g, "M", Font, Size.Empty, 
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            _charWidth = size.Width;
            _charHeight = Font.Height;
        }
        
        // Ensure minimum sizes
        if (_charWidth < 1) _charWidth = 8;
        if (_charHeight < 1) _charHeight = 16;
        
        UpdateTerminalSize();
    }
    
    /// <summary>
    /// Calculate terminal dimensions based on control size
    /// </summary>
    private void UpdateTerminalSize()
    {
        int newCols = Math.Max(20, ClientSize.Width / _charWidth);
        int newRows = Math.Max(5, ClientSize.Height / _charHeight);
        
        if (newCols != TerminalCols || newRows != TerminalRows)
        {
            TerminalCols = newCols;
            TerminalRows = newRows;
            
            // Resize the screen buffer if we have one
            _screenBuffer?.Resize(TerminalCols, TerminalRows);
            
            // Notify listeners
            OnTerminalSizeChanged?.Invoke(TerminalCols, TerminalRows);
        }
    }
    
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateTerminalSize();
    }
    
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        UpdateCharacterDimensions();
    }
    
    /// <summary>
    /// Invalidate just the cursor area for efficient blinking
    /// </summary>
    private void InvalidateCursorArea()
    {
        if (_screenBuffer == null) return;
        
        // Calculate cursor position considering input buffer
        int cursorRow = _screenBuffer.CursorY;
        int cursorCol = _screenBuffer.CursorX + _inputBuffer.Length;
        
        // Handle line wrapping
        while (cursorCol >= TerminalCols && cursorRow < TerminalRows - 1)
        {
            cursorCol -= TerminalCols;
            cursorRow++;
        }
        
        var rect = new Rectangle(cursorCol * _charWidth, cursorRow * _charHeight, 
                                 _charWidth, _charHeight);
        Invalidate(rect);
    }
    
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        
        var g = e.Graphics;
        g.Clear(BackColor);
        
        if (_screenBuffer == null) return;
        
        // Use TextRenderer for crisp text (like the system uses)
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        
        // Draw each cell from the buffer
        for (int row = 0; row < Math.Min(_screenBuffer.Rows, TerminalRows); row++)
        {
            for (int col = 0; col < Math.Min(_screenBuffer.Cols, TerminalCols); col++)
            {
                var cell = _screenBuffer.GetCell(row, col);
                int x = col * _charWidth;
                int y = row * _charHeight;
                
                // Draw background if not black (even for empty cells - important for input fields)
                if (cell.Bg != ConsoleColor.Black)
                {
                    using var bgBrush = new SolidBrush(ConsoleColorToColor(cell.Bg));
                    g.FillRectangle(bgBrush, x, y, _charWidth, _charHeight);
                }
                
                // Draw the character (skip spaces/nulls - they're just background)
                if (cell.Ch != ' ' && cell.Ch != '\0')
                {
                    var fgColor = ConsoleColorToColor(cell.Fg);
                    TextRenderer.DrawText(g, cell.Ch.ToString(), Font, 
                        new Point(x, y), fgColor, flags);
                }
            }
        }
        
        // Draw the input buffer after the cursor position
        if (_inputBuffer.Length > 0)
        {
            int inputRow = _screenBuffer.CursorY;
            int inputCol = _screenBuffer.CursorX;
            
            for (int i = 0; i < _inputBuffer.Length; i++)
            {
                int x = inputCol * _charWidth;
                int y = inputRow * _charHeight;
                
                // Draw the character in gray (user input color)
                TextRenderer.DrawText(g, _inputBuffer[i].ToString(), Font,
                    new Point(x, y), Color.FromArgb(192, 192, 192), flags);
                
                inputCol++;
                if (inputCol >= TerminalCols)
                {
                    inputCol = 0;
                    inputRow++;
                    if (inputRow >= TerminalRows) break;
                }
            }
        }
        
        // Draw cursor
        if (_cursorVisible && Focused)
        {
            int cursorRow = _screenBuffer.CursorY;
            int cursorCol = PassThroughMode ? _screenBuffer.CursorX : _screenBuffer.CursorX + _inputBuffer.Length;
            
            // Handle line wrapping (only in normal mode)
            if (!PassThroughMode)
            {
                while (cursorCol >= TerminalCols && cursorRow < TerminalRows - 1)
                {
                    cursorCol -= TerminalCols;
                    cursorRow++;
                }
            }
            
            int cx = cursorCol * _charWidth;
            int cy = cursorRow * _charHeight;
            
            if (PassThroughMode)
            {
                // Training screen cursor: dark grey background with white text
                using var cursorBrush = new SolidBrush(Color.FromArgb(96, 96, 96));  // Dark grey
                g.FillRectangle(cursorBrush, cx, cy, _charWidth, _charHeight);
                
                // Draw the character at cursor position in white (if any)
                var cell = _screenBuffer.GetCell(cursorRow, cursorCol);
                if (cell.Ch != ' ' && cell.Ch != '\0')
                {
                    TextRenderer.DrawText(g, cell.Ch.ToString(), Font,
                        new Point(cx, cy), Color.White, flags);
                }
            }
            else
            {
                // Normal cursor: semi-transparent grey block
                using var cursorBrush = new SolidBrush(Color.FromArgb(180, 192, 192, 192));
                g.FillRectangle(cursorBrush, cx, cy, _charWidth, _charHeight);
            }
        }
    }
    
    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Allow Ctrl+C for copy (future: implement copy)
        if (e.Control && e.KeyCode == Keys.C)
        {
            base.OnKeyDown(e);
            return;
        }
        
        // Pass-through mode: send keystrokes directly to server (for training screen)
        if (PassThroughMode)
        {
            byte[]? data = null;
            
            switch (e.KeyCode)
            {
                case Keys.Enter:
                    data = new byte[] { 0x0D };  // CR
                    break;
                case Keys.Back:
                    data = new byte[] { 0x08 };  // BS
                    break;
                case Keys.Escape:
                    data = new byte[] { 0x1B };  // ESC
                    break;
                case Keys.Up:
                    data = new byte[] { 0x1B, 0x5B, 0x41 };  // ESC [ A
                    break;
                case Keys.Down:
                    data = new byte[] { 0x1B, 0x5B, 0x42 };  // ESC [ B
                    break;
                case Keys.Right:
                    data = new byte[] { 0x1B, 0x5B, 0x43 };  // ESC [ C
                    break;
                case Keys.Left:
                    data = new byte[] { 0x1B, 0x5B, 0x44 };  // ESC [ D
                    break;
                case Keys.Space:
                    data = new byte[] { 0x20 };  // Space
                    break;
                case Keys.Tab:
                    data = new byte[] { 0x09 };  // Tab
                    break;
                case Keys.Delete:
                    data = new byte[] { 0x7F };  // DEL
                    break;
            }
            
            if (data != null)
            {
                // Only suppress KeyPress for special keys we handled
                e.Handled = true;
                e.SuppressKeyPress = true;
                OnRawKeyData?.Invoke(data);
            }
            // For regular character keys, let OnKeyPress handle them
            return;
        }
        
        // Normal mode: buffer input locally
        
        // Handle Enter - send command
        if (e.KeyCode == Keys.Enter)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            
            string command = _inputBuffer.ToString();
            _inputBuffer.Clear();
            _cursorVisible = true;  // Reset cursor visibility
            
            OnCommandEntered?.Invoke(command);
            Invalidate();
            return;
        }
        
        // Handle Backspace
        if (e.KeyCode == Keys.Back)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            
            if (_inputBuffer.Length > 0)
            {
                _inputBuffer.Remove(_inputBuffer.Length - 1, 1);
                _cursorVisible = true;
                Invalidate();
            }
            return;
        }
        
        // Handle Escape - clear input
        if (e.KeyCode == Keys.Escape)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            
            if (_inputBuffer.Length > 0)
            {
                _inputBuffer.Clear();
                _cursorVisible = true;
                Invalidate();
            }
            return;
        }
        
        // Block navigation and special keys
        if (e.Control || e.Alt ||
            e.KeyCode == Keys.Left || e.KeyCode == Keys.Right ||
            e.KeyCode == Keys.Up || e.KeyCode == Keys.Down ||
            e.KeyCode == Keys.Home || e.KeyCode == Keys.End ||
            e.KeyCode == Keys.PageUp || e.KeyCode == Keys.PageDown ||
            e.KeyCode == Keys.Delete || e.KeyCode == Keys.Insert)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
        
        base.OnKeyDown(e);
    }
    
    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        // Pass-through mode: send characters directly to server
        if (PassThroughMode)
        {
            if (!char.IsControl(e.KeyChar))
            {
                // Send the character directly
                OnRawKeyData?.Invoke(new byte[] { (byte)e.KeyChar });
                e.Handled = true;
            }
            return;
        }
        
        // Normal mode: buffer locally
        
        // Ignore control characters
        if (char.IsControl(e.KeyChar) && e.KeyChar != '\r' && e.KeyChar != '\b')
        {
            e.Handled = true;
            return;
        }
        
        // Add printable characters to input buffer
        if (!char.IsControl(e.KeyChar))
        {
            _inputBuffer.Append(e.KeyChar);
            _cursorVisible = true;
            Invalidate();
            e.Handled = true;
        }
        
        base.OnKeyPress(e);
    }
    
    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        _cursorVisible = true;
        _cursorTimer.Start();
        Invalidate();
    }
    
    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        _cursorTimer.Stop();
        _cursorVisible = false;
        Invalidate();
    }
    
    protected override bool IsInputKey(Keys keyData)
    {
        // Allow arrow keys and other navigation to be handled by KeyDown
        switch (keyData)
        {
            case Keys.Up:
            case Keys.Down:
            case Keys.Left:
            case Keys.Right:
            case Keys.Tab:
            case Keys.Escape:
                return true;
        }
        return base.IsInputKey(keyData);
    }
    
    /// <summary>
    /// Convert ConsoleColor to System.Drawing.Color
    /// </summary>
    private static Color ConsoleColorToColor(ConsoleColor cc)
    {
        return cc switch
        {
            ConsoleColor.Black => Color.FromArgb(0, 0, 0),
            ConsoleColor.DarkBlue => Color.FromArgb(0, 0, 128),
            ConsoleColor.DarkGreen => Color.FromArgb(0, 128, 0),
            ConsoleColor.DarkCyan => Color.FromArgb(0, 128, 128),
            ConsoleColor.DarkRed => Color.FromArgb(128, 0, 0),
            ConsoleColor.DarkMagenta => Color.FromArgb(128, 0, 128),
            ConsoleColor.DarkYellow => Color.FromArgb(128, 128, 0),
            ConsoleColor.Gray => Color.FromArgb(192, 192, 192),
            ConsoleColor.DarkGray => Color.FromArgb(128, 128, 128),
            ConsoleColor.Blue => Color.FromArgb(0, 0, 255),
            ConsoleColor.Green => Color.FromArgb(0, 255, 0),
            ConsoleColor.Cyan => Color.FromArgb(0, 255, 255),
            ConsoleColor.Red => Color.FromArgb(255, 0, 0),
            ConsoleColor.Magenta => Color.FromArgb(255, 0, 255),
            ConsoleColor.Yellow => Color.FromArgb(255, 255, 0),
            ConsoleColor.White => Color.FromArgb(255, 255, 255),
            _ => Color.FromArgb(192, 192, 192)
        };
    }
    
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cursorTimer?.Stop();
            _cursorTimer?.Dispose();
        }
        base.Dispose(disposing);
    }
}
