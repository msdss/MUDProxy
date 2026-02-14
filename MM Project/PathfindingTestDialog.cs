namespace MudProxyViewer;

/// <summary>
/// Test dialog for verifying pathfinding between rooms.
/// Allows searching for rooms by name or key, and displays the BFS path result.
/// </summary>
public class PathfindingTestDialog : Form
{
    private readonly RoomGraphManager _roomGraph;

    // From controls
    private TextBox _fromSearchBox = null!;
    private Button _fromSearchButton = null!;
    private Label _fromSelectedLabel = null!;

    // To controls
    private TextBox _toSearchBox = null!;
    private Button _toSearchButton = null!;
    private Label _toSelectedLabel = null!;

    // Search results
    private ListView _searchResults = null!;
    private Label _searchResultsLabel = null!;

    // Path controls
    private Button _findPathButton = null!;
    private ListView _pathResults = null!;
    private Label _pathStatusLabel = null!;
    private TextBox _commandsTextBox = null!;
    private Label _commandsLabel = null!;

    // State
    private string _fromKey = "";
    private string _toKey = "";
    private bool _searchingForFrom = true; // Which field the search results apply to

    public PathfindingTestDialog(RoomGraphManager roomGraph)
    {
        _roomGraph = roomGraph;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        this.Text = "Pathfinding Test";
        this.Size = new Size(680, 700);
        this.MinimumSize = new Size(600, 600);
        this.StartPosition = FormStartPosition.CenterParent;
        this.BackColor = Color.FromArgb(45, 45, 45);
        this.FormBorderStyle = FormBorderStyle.Sizable;
        this.MaximizeBox = false;
        this.MinimizeBox = false;

        int y = 15;
        int labelX = 15;
        int inputX = 60;
        int inputWidth = 460;
        int buttonX = 528;

        // ── From ──
        var fromLabel = new Label
        {
            Text = "From:",
            Location = new Point(labelX, y + 3),
            AutoSize = true,
            ForeColor = Color.LightGray,
            Font = new Font("Segoe UI", 9)
        };
        this.Controls.Add(fromLabel);

        _fromSearchBox = new TextBox
        {
            Location = new Point(inputX, y),
            Size = new Size(inputWidth, 25),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9),
            BorderStyle = BorderStyle.FixedSingle
        };
        _fromSearchBox.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoSearch(true); }
        };
        this.Controls.Add(_fromSearchBox);

        _fromSearchButton = new Button
        {
            Text = "Search",
            Location = new Point(buttonX, y - 1),
            Size = new Size(70, 27),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8.5f)
        };
        _fromSearchButton.Click += (s, e) => DoSearch(true);
        this.Controls.Add(_fromSearchButton);

        y += 30;
        _fromSelectedLabel = new Label
        {
            Text = "(no room selected)",
            Location = new Point(inputX, y),
            Size = new Size(inputWidth, 18),
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 8.5f)
        };
        this.Controls.Add(_fromSelectedLabel);

        // ── To ──
        y += 28;
        var toLabel = new Label
        {
            Text = "To:",
            Location = new Point(labelX, y + 3),
            AutoSize = true,
            ForeColor = Color.LightGray,
            Font = new Font("Segoe UI", 9)
        };
        this.Controls.Add(toLabel);

        _toSearchBox = new TextBox
        {
            Location = new Point(inputX, y),
            Size = new Size(inputWidth, 25),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9),
            BorderStyle = BorderStyle.FixedSingle
        };
        _toSearchBox.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoSearch(false); }
        };
        this.Controls.Add(_toSearchBox);

        _toSearchButton = new Button
        {
            Text = "Search",
            Location = new Point(buttonX, y - 1),
            Size = new Size(70, 27),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8.5f)
        };
        _toSearchButton.Click += (s, e) => DoSearch(false);
        this.Controls.Add(_toSearchButton);

        y += 30;
        _toSelectedLabel = new Label
        {
            Text = "(no room selected)",
            Location = new Point(inputX, y),
            Size = new Size(inputWidth, 18),
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 8.5f)
        };
        this.Controls.Add(_toSelectedLabel);

        // ── Search Results ──
        y += 28;
        _searchResultsLabel = new Label
        {
            Text = "Search Results",
            Location = new Point(labelX, y),
            AutoSize = true,
            ForeColor = Color.LightGray,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        this.Controls.Add(_searchResultsLabel);

        y += 20;
        _searchResults = new ListView
        {
            Location = new Point(labelX, y),
            Size = new Size(635, 130),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            BackColor = Color.FromArgb(35, 35, 35),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9),
            HeaderStyle = ColumnHeaderStyle.Nonclickable
        };
        _searchResults.Columns.Add("Map", 50);
        _searchResults.Columns.Add("Room", 55);
        _searchResults.Columns.Add("Name", 430);
        _searchResults.Columns.Add("Exits", 80);
        _searchResults.DoubleClick += SearchResults_DoubleClick;
        _searchResults.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SelectSearchResult(); }
        };
        this.Controls.Add(_searchResults);

        // ── Find Path ──
        y += 138;
        _findPathButton = new Button
        {
            Text = "Find Path",
            Location = new Point(labelX, y),
            Size = new Size(100, 30),
            BackColor = Color.FromArgb(50, 90, 50),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Enabled = false
        };
        _findPathButton.Click += FindPathButton_Click;
        this.Controls.Add(_findPathButton);

        _pathStatusLabel = new Label
        {
            Text = "",
            Location = new Point(125, y + 6),
            Size = new Size(500, 20),
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 9)
        };
        this.Controls.Add(_pathStatusLabel);

        // ── Path Results ──
        y += 38;
        var pathLabel = new Label
        {
            Text = "Path Result",
            Location = new Point(labelX, y),
            AutoSize = true,
            ForeColor = Color.LightGray,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        this.Controls.Add(pathLabel);

        y += 20;
        _pathResults = new ListView
        {
            Location = new Point(labelX, y),
            Size = new Size(635, 180),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            BackColor = Color.FromArgb(35, 35, 35),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9),
            HeaderStyle = ColumnHeaderStyle.Nonclickable
        };
        _pathResults.Columns.Add("#", 35);
        _pathResults.Columns.Add("Cmd", 75);
        _pathResults.Columns.Add("To", 80);
        _pathResults.Columns.Add("Room Name", 425);
        this.Controls.Add(_pathResults);

        // ── Commands ──
        y += 188;
        _commandsLabel = new Label
        {
            Text = "Commands:",
            Location = new Point(labelX, y + 2),
            AutoSize = true,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            ForeColor = Color.LightGray,
            Font = new Font("Segoe UI", 9)
        };
        this.Controls.Add(_commandsLabel);

        _commandsTextBox = new TextBox
        {
            Location = new Point(95, y),
            Size = new Size(460, 25),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Font = new Font("Consolas", 9),
            BorderStyle = BorderStyle.FixedSingle,
            ReadOnly = true
        };
        this.Controls.Add(_commandsTextBox);

        // ── Close Button ──
        var closeButton = new Button
        {
            Text = "Close",
            Size = new Size(80, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9),
            DialogResult = DialogResult.OK
        };
        closeButton.Location = new Point(this.ClientSize.Width - 95, this.ClientSize.Height - 45);
        this.Controls.Add(closeButton);
        this.AcceptButton = closeButton;

        // Graph status in title bar
        if (_roomGraph.IsLoaded)
        {
            this.Text = $"Pathfinding Test — {_roomGraph.RoomCount:N0} rooms loaded";
        }
        else
        {
            this.Text = "Pathfinding Test — No room data loaded";
        }
    }

    /// <summary>
    /// Search for rooms by the text in the From or To search box.
    /// Supports both name search and direct "Map/Room" key entry.
    /// </summary>
    private void DoSearch(bool isFrom)
    {
        _searchingForFrom = isFrom;
        var searchText = isFrom ? _fromSearchBox.Text.Trim() : _toSearchBox.Text.Trim();

        if (string.IsNullOrEmpty(searchText))
            return;

        _searchResults.Items.Clear();

        // Check if it's a direct key entry (e.g., "1/297")
        if (searchText.Contains('/'))
        {
            var room = _roomGraph.GetRoom(searchText);
            if (room != null)
            {
                AddRoomToSearchResults(room);
                _searchResultsLabel.Text = $"Search Results — 1 match (direct key lookup)";
                // Auto-select if direct key
                SetSelectedRoom(isFrom, room);
                return;
            }
        }

        // Search by name
        var results = _roomGraph.SearchByName(searchText, 200);

        if (results.Count == 0)
        {
            _searchResultsLabel.Text = $"Search Results — no matches for \"{searchText}\"";
            return;
        }

        // Sort results: exact matches first, then by map/room number
        var sorted = results
            .OrderByDescending(r => r.Name.Equals(searchText, StringComparison.OrdinalIgnoreCase))
            .ThenBy(r => r.MapNumber)
            .ThenBy(r => r.RoomNumber)
            .ToList();

        foreach (var room in sorted)
        {
            AddRoomToSearchResults(room);
        }

        var searchingFor = isFrom ? "From" : "To";
        _searchResultsLabel.Text = $"Search Results — {results.Count} match(es) for \"{searchText}\" (selecting for {searchingFor})";
    }

    private void AddRoomToSearchResults(RoomNode room)
    {
        var item = new ListViewItem(room.MapNumber.ToString());
        item.SubItems.Add(room.RoomNumber.ToString());
        item.SubItems.Add(room.Name);

        // Show exit count summary
        var traversable = room.Exits.Count(e => e.Traversable);
        var total = room.Exits.Count;
        item.SubItems.Add(traversable == total ? $"{total}" : $"{traversable}/{total}");

        item.Tag = room;
        _searchResults.Items.Add(item);
    }

    private void SearchResults_DoubleClick(object? sender, EventArgs e)
    {
        SelectSearchResult();
    }

    private void SelectSearchResult()
    {
        if (_searchResults.SelectedItems.Count == 0)
            return;

        var room = _searchResults.SelectedItems[0].Tag as RoomNode;
        if (room == null)
            return;

        SetSelectedRoom(_searchingForFrom, room);
    }

    private void SetSelectedRoom(bool isFrom, RoomNode room)
    {
        if (isFrom)
        {
            _fromKey = room.Key;
            _fromSelectedLabel.Text = $"Map {room.MapNumber} / Room {room.RoomNumber} — {room.Name}";
            _fromSelectedLabel.ForeColor = Color.FromArgb(100, 200, 100);
        }
        else
        {
            _toKey = room.Key;
            _toSelectedLabel.Text = $"Map {room.MapNumber} / Room {room.RoomNumber} — {room.Name}";
            _toSelectedLabel.ForeColor = Color.FromArgb(100, 200, 100);
        }

        // Enable Find Path when both are set
        _findPathButton.Enabled = !string.IsNullOrEmpty(_fromKey) && !string.IsNullOrEmpty(_toKey);
    }

    private void FindPathButton_Click(object? sender, EventArgs e)
    {
        _pathResults.Items.Clear();
        _commandsTextBox.Text = "";

        if (string.IsNullOrEmpty(_fromKey) || string.IsNullOrEmpty(_toKey))
            return;

        _pathStatusLabel.Text = "Searching...";
        _pathStatusLabel.ForeColor = Color.Gray;
        Application.DoEvents();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = _roomGraph.FindPath(_fromKey, _toKey);
        sw.Stop();

        if (result.Success)
        {
            if (result.TotalSteps == 0)
            {
                _pathStatusLabel.Text = $"You're already there! ({sw.ElapsedMilliseconds}ms)";
                _pathStatusLabel.ForeColor = Color.FromArgb(100, 200, 100);
                return;
            }

            _pathStatusLabel.Text = $"Path found: {result.TotalSteps} step(s) in {sw.ElapsedMilliseconds}ms";
            _pathStatusLabel.ForeColor = Color.FromArgb(100, 200, 100);

            for (int i = 0; i < result.Steps.Count; i++)
            {
                var step = result.Steps[i];
                var item = new ListViewItem((i + 1).ToString());
                item.SubItems.Add(step.Command);
                item.SubItems.Add(step.ToKey);
                item.SubItems.Add(step.ToName);
                _pathResults.Items.Add(item);
            }

            // Build command string
            _commandsTextBox.Text = string.Join(", ", result.Steps.Select(s => s.Command));
        }
        else
        {
            _pathStatusLabel.Text = result.ErrorMessage;
            _pathStatusLabel.ForeColor = Color.FromArgb(200, 100, 100);
        }
    }
}
