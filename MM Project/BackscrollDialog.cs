namespace MudProxyViewer;

/// <summary>
/// Dialog that displays the scrollback buffer (game text history) with colors.
/// Supports Ctrl+F to search, F3 to find next, and standard copy/paste.
/// </summary>
public class BackscrollDialog : Form
{
    private readonly RichTextBox _textBox;
    private readonly Panel _searchPanel;
    private readonly TextBox _searchBox;
    private readonly Label _searchStatusLabel;
    private int _lastSearchIndex = 0;

    public BackscrollDialog(List<TerminalCell[]> scrollbackLines)
    {
        Text = $"Game Backscroll ({scrollbackLines.Count} lines)";
        Size = new Size(800, 600);
        MinimumSize = new Size(400, 300);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(30, 30, 30);
        KeyPreview = true;

        // Search panel (hidden by default)
        _searchPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 30,
            BackColor = Color.FromArgb(50, 50, 50),
            Visible = false,
            Padding = new Padding(4, 3, 4, 3)
        };

        var searchLabel = new Label
        {
            Text = "Find:",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9),
            AutoSize = true,
            Location = new Point(6, 5)
        };
        _searchPanel.Controls.Add(searchLabel);

        _searchBox = new TextBox
        {
            Location = new Point(45, 3),
            Width = 250,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9)
        };
        _searchBox.KeyDown += SearchBox_KeyDown;
        _searchPanel.Controls.Add(_searchBox);

        var findNextButton = new Button
        {
            Text = "Find Next",
            Location = new Point(305, 2),
            Size = new Size(70, 24),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8)
        };
        findNextButton.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
        findNextButton.Click += (s, e) => FindNext();
        _searchPanel.Controls.Add(findNextButton);

        _searchStatusLabel = new Label
        {
            Text = "",
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 8),
            AutoSize = true,
            Location = new Point(385, 6)
        };
        _searchPanel.Controls.Add(_searchStatusLabel);

        var closeSearchButton = new Label
        {
            Text = "✕",
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 9),
            AutoSize = true,
            Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        closeSearchButton.Click += (s, e) => HideSearch();
        closeSearchButton.MouseEnter += (s, e) => closeSearchButton.ForeColor = Color.White;
        closeSearchButton.MouseLeave += (s, e) => closeSearchButton.ForeColor = Color.Gray;
        _searchPanel.Controls.Add(closeSearchButton);
        _searchPanel.Resize += (s, e) =>
        {
            closeSearchButton.Location = new Point(_searchPanel.Width - 20, 5);
        };
        closeSearchButton.Location = new Point(_searchPanel.Width - 20, 5);

        // Main text display
        _textBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black,
            ForeColor = Color.FromArgb(192, 192, 192),
            Font = new Font("Consolas", 10),
            ReadOnly = true,
            WordWrap = false,
            BorderStyle = BorderStyle.None,
            HideSelection = false
        };

        Controls.Add(_textBox);
        Controls.Add(_searchPanel);

        // Populate with colored text
        PopulateText(scrollbackLines);

        // Scroll to bottom (most recent text)
        _textBox.SelectionStart = _textBox.TextLength;
        _textBox.ScrollToCaret();

        KeyDown += BackscrollDialog_KeyDown;
    }

    /// <summary>
    /// Populate the RichTextBox with colored text from scrollback lines.
    /// </summary>
    private void PopulateText(List<TerminalCell[]> lines)
    {
        _textBox.SuspendLayout();

        foreach (var line in lines)
        {
            // Find last non-space character
            int lastNonSpace = -1;
            for (int c = line.Length - 1; c >= 0; c--)
            {
                if (line[c].Ch != ' ' && line[c].Ch != '\0')
                {
                    lastNonSpace = c;
                    break;
                }
            }

            if (lastNonSpace < 0)
            {
                // Blank line
                _textBox.AppendText(Environment.NewLine);
                continue;
            }

            // Build runs of same-colored text for efficiency
            int runStart = 0;
            var runColor = ConsoleColorToColor(line[0].Fg);

            for (int c = 0; c <= lastNonSpace; c++)
            {
                var cellColor = ConsoleColorToColor(line[c].Fg);
                if (cellColor != runColor)
                {
                    // Flush previous run
                    AppendColoredText(line, runStart, c - 1, runColor);
                    runStart = c;
                    runColor = cellColor;
                }
            }

            // Flush final run
            AppendColoredText(line, runStart, lastNonSpace, runColor);
            _textBox.AppendText(Environment.NewLine);
        }

        _textBox.ResumeLayout();
    }

    /// <summary>
    /// Append a run of characters with a specific color.
    /// </summary>
    private void AppendColoredText(TerminalCell[] line, int startCol, int endCol, Color color)
    {
        var sb = new System.Text.StringBuilder(endCol - startCol + 1);
        for (int c = startCol; c <= endCol; c++)
        {
            var ch = line[c].Ch;
            sb.Append(ch == '\0' ? ' ' : ch);
        }

        var text = sb.ToString();
        int insertPos = _textBox.TextLength;
        _textBox.AppendText(text);
        _textBox.Select(insertPos, text.Length);
        _textBox.SelectionColor = color;
        _textBox.Select(_textBox.TextLength, 0);
    }

    #region Search

    private void ShowSearch()
    {
        _searchPanel.Visible = true;
        _searchBox.Focus();
        _searchBox.SelectAll();
    }

    private void HideSearch()
    {
        _searchPanel.Visible = false;
        _searchStatusLabel.Text = "";
        _textBox.Focus();
    }

    private void FindNext()
    {
        var searchText = _searchBox.Text;
        if (string.IsNullOrEmpty(searchText)) return;

        int startIndex = _lastSearchIndex;
        int foundIndex = _textBox.Find(searchText, startIndex, RichTextBoxFinds.None);

        if (foundIndex < 0 && startIndex > 0)
        {
            // Wrap around to beginning
            foundIndex = _textBox.Find(searchText, 0, startIndex, RichTextBoxFinds.None);
            if (foundIndex >= 0)
                _searchStatusLabel.Text = "Wrapped to top";
        }

        if (foundIndex >= 0)
        {
            _textBox.Select(foundIndex, searchText.Length);
            _textBox.ScrollToCaret();
            _lastSearchIndex = foundIndex + searchText.Length;
            if (_searchStatusLabel.Text != "Wrapped to top")
                _searchStatusLabel.Text = "";
        }
        else
        {
            _searchStatusLabel.Text = "Not found";
            _lastSearchIndex = 0;
        }
    }

    #endregion

    #region Key Handling

    private void BackscrollDialog_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.F)
        {
            ShowSearch();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.F3)
        {
            if (!_searchPanel.Visible)
                ShowSearch();
            else
                FindNext();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            if (_searchPanel.Visible)
            {
                HideSearch();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else
            {
                Close();
            }
        }
    }

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            FindNext();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            HideSearch();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    #endregion

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
}
