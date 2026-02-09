namespace MudProxyViewer;

/// <summary>
/// Represents a single character cell in the terminal with foreground and background colors.
/// </summary>
public readonly struct TerminalCell
{
    public readonly char Ch;
    public readonly ConsoleColor Fg;
    public readonly ConsoleColor Bg;

    public TerminalCell(char ch, ConsoleColor fg, ConsoleColor bg)
    {
        Ch = ch;
        Fg = fg;
        Bg = bg;
    }
}
