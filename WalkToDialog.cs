namespace MudProxyViewer;

/// <summary>
/// Dialog for searching for a destination room and walking to it.
/// Stays open during the walk to show live progress and allow cancellation.
/// 
/// Uses:
///   - RoomGraphManager.SearchByName() for room search
///   - RoomGraphManager.FindPath() for path preview
///   - AutoWalkManager.StartWalk() / Stop() for walk execution
///   - RoomTracker.CurrentRoom for current location
/// </summary>
public class WalkToDialog : Form
{
    // ── Dependencies ──
    private readonly GameManager _gameManager;

    // ── Controls ──
    private TextBox _searchBox = null!;
    private Button _searchButton = null!;
    private ListView _resultsList = null!;
    private Label _currentRoomLabel = null!;
    private Label _destinationLabel = null!;
    private Label _pathInfoLabel = null!;
    private ProgressBar _progressBar = null!;
    private Label _progressLabel = null!;
    private Label _stateLabel = null!;
    private Label _blockingReasonsLabel = null!;
    private Button _walkButton = null!;
    private Button _stopButton = null!;
    private Button _closeButton = null!;

    // ── State ──
    private DebugLogWriter? _walkLog;
    private PathResult? _previewPath = null;

    // ── Colors (matching app theme) ──
    private static readonly Color BgDark = Color.FromArgb(45, 45, 45);
    private static readonly Color BgMedium = Color.FromArgb(50, 50, 50);
    private static readonly Color BgButton = Color.FromArgb(60, 60, 60);
    private static readonly Color BgPanel = Color.FromArgb(40, 40, 40);
    private static readonly Color TextWhite = Color.White;
    private static readonly Color TextGray = Color.LightGray;
    private static readonly Color TextDim = Color.FromArgb(150, 150, 150);
    private static readonly Color AccentGreen = Color.FromArgb(0, 200, 100);
    private static readonly Color AccentRed = Color.FromArgb(220, 60, 60);
    private static readonly Color AccentYellow = Color.FromArgb(220, 200, 60);
    private static readonly Color AccentTeal = Color.FromArgb(0, 210, 180);

    public WalkToDialog(GameManager gameManager)
    {
        _gameManager = gameManager;
        InitializeComponent();
        WireEvents();
        UpdateCurrentRoomDisplay();
        UpdateWalkButtonState();
    }

    #region UI Construction

    private void InitializeComponent()
    {
        this.Text = "Walk To...";
        this.Size = new Size(520, 620);
        this.MinimumSize = new Size(460, 560);
        this.StartPosition = FormStartPosition.Manual;
        this.BackColor = BgDark;
        this.ForeColor = TextWhite;
        this.FormBorderStyle = FormBorderStyle.Sizable;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.Font = new Font("Segoe UI", 9);

        int y = 12;
        int pad = 12;
        int contentWidth = this.ClientSize.Width - (pad * 2);

        // ── Search row ──
        var searchLabel = new Label
        {
            Text = "Search:",
            Location = new Point(pad, y + 3),
            AutoSize = true,
            ForeColor = TextGray
        };
        this.Controls.Add(searchLabel);

        _searchBox = new TextBox
        {
            Location = new Point(pad + 55, y),
            Size = new Size(contentWidth - 55 - 80, 23),
            BackColor = BgMedium,
            ForeColor = TextWhite,
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _searchBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { PerformSearch(); e.SuppressKeyPress = true; } };
        this.Controls.Add(_searchBox);

        _searchButton = new Button
        {
            Text = "Search",
            Location = new Point(contentWidth + pad - 72, y - 1),
            Size = new Size(72, 25),
            BackColor = BgButton,
            ForeColor = TextWhite,
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _searchButton.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
        _searchButton.Click += (s, e) => PerformSearch();
        this.Controls.Add(_searchButton);

        y += 32;

        // ── Results list ──
        _resultsList = new ListView
        {
            Location = new Point(pad, y),
            Size = new Size(contentWidth, 220),
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            BackColor = BgMedium,
            ForeColor = TextWhite,
            BorderStyle = BorderStyle.FixedSingle,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };
        _resultsList.Columns.Add("Room", 80);
        _resultsList.Columns.Add("Name", 380);
        _resultsList.SelectedIndexChanged += ResultsList_SelectedIndexChanged;
        _resultsList.DoubleClick += ResultsList_DoubleClick;
        this.Controls.Add(_resultsList);

        y += 228;

        // ── Status panel ──
        var statusGroup = new GroupBox
        {
            Text = "Status",
            Location = new Point(pad, y),
            Size = new Size(contentWidth, 210),
            ForeColor = TextGray,
            BackColor = BgDark,
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };

        int sy = 20;

        _currentRoomLabel = new Label
        {
            Text = "Current: (unknown)",
            Location = new Point(10, sy),
            Size = new Size(statusGroup.Width - 20, 18),
            ForeColor = AccentTeal,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        statusGroup.Controls.Add(_currentRoomLabel);
        sy += 22;

        _destinationLabel = new Label
        {
            Text = "Destination: (none selected)",
            Location = new Point(10, sy),
            Size = new Size(statusGroup.Width - 20, 18),
            ForeColor = TextGray,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        statusGroup.Controls.Add(_destinationLabel);
        sy += 22;

        _pathInfoLabel = new Label
        {
            Text = "Path: —",
            Location = new Point(10, sy),
            Size = new Size(statusGroup.Width - 20, 18),
            ForeColor = TextGray,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        statusGroup.Controls.Add(_pathInfoLabel);
        sy += 20;

        // ── Blocking reasons (shown when path is blocked by restrictions) ──
        _blockingReasonsLabel = new Label
        {
            Text = "",
            Location = new Point(10, sy),
            Size = new Size(statusGroup.Width - 20, 80),
            ForeColor = AccentYellow,
            Font = new Font("Segoe UI", 8.5f),
            Visible = false,
            AutoSize = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        statusGroup.Controls.Add(_blockingReasonsLabel);

        sy += 2;

        _progressBar = new ProgressBar
        {
            Location = new Point(10, sy),
            Size = new Size(statusGroup.Width - 20, 20),
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Style = ProgressBarStyle.Continuous,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        statusGroup.Controls.Add(_progressBar);
        sy += 24;

        _progressLabel = new Label
        {
            Text = "",
            Location = new Point(10, sy),
            Size = new Size(statusGroup.Width - 130, 18),
            ForeColor = TextGray,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        statusGroup.Controls.Add(_progressLabel);

        _stateLabel = new Label
        {
            Text = "Idle",
            Location = new Point(statusGroup.Width - 130, sy),
            Size = new Size(120, 18),
            ForeColor = TextDim,
            TextAlign = ContentAlignment.TopRight,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        statusGroup.Controls.Add(_stateLabel);

        this.Controls.Add(statusGroup);

        y += 178;

        // ── Button row ──
        var buttonPanel = new Panel
        {
            Location = new Point(0, 0),
            Dock = DockStyle.Bottom,
            Height = 50,
            BackColor = BgPanel
        };

        _walkButton = new Button
        {
            Text = "▶  Walk",
            Size = new Size(90, 32),
            BackColor = BgButton,
            ForeColor = TextWhite,
            FlatStyle = FlatStyle.Flat,
            Enabled = false
        };
        _walkButton.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
        _walkButton.Click += WalkButton_Click;

        _stopButton = new Button
        {
            Text = "⏹  Stop",
            Size = new Size(90, 32),
            BackColor = BgButton,
            ForeColor = TextWhite,
            FlatStyle = FlatStyle.Flat,
            Enabled = false
        };
        _stopButton.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
        _stopButton.Click += StopButton_Click;

        _closeButton = new Button
        {
            Text = "Close",
            Size = new Size(80, 32),
            BackColor = BgButton,
            ForeColor = TextWhite,
            FlatStyle = FlatStyle.Flat
        };
        _closeButton.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
        _closeButton.Click += (s, e) => this.Close();

        // Position buttons anchored to bottom-right
        buttonPanel.Layout += (s, e) =>
        {
            int bx = buttonPanel.Width - 12;
            bx -= _closeButton.Width;
            _closeButton.Location = new Point(bx, 9);
            bx -= _stopButton.Width + 8;
            _stopButton.Location = new Point(bx, 9);
            bx -= _walkButton.Width + 8;
            _walkButton.Location = new Point(bx, 9);
        };

        buttonPanel.Controls.Add(_walkButton);
        buttonPanel.Controls.Add(_stopButton);
        buttonPanel.Controls.Add(_closeButton);
        this.Controls.Add(buttonPanel);

        this.CancelButton = _closeButton;
    }

    #endregion

    #region Event Wiring

    private void WireEvents()
    {
        var walker = _gameManager.AutoWalkManager;

        walker.OnWalkProgress += (step, total, roomName) =>
        {
            if (InvokeRequired) { BeginInvoke(() => UpdateProgress(step, total, roomName)); return; }
            UpdateProgress(step, total, roomName);
        };

        walker.OnWalkStateChanged += (state) =>
        {
            if (InvokeRequired) { BeginInvoke(() => UpdateStateDisplay(state)); return; }
            UpdateStateDisplay(state);
        };

        walker.OnWalkCompleted += (name) =>
        {
            if (InvokeRequired) { BeginInvoke(() => OnWalkCompleted(name)); return; }
            OnWalkCompleted(name);
        };

        walker.OnWalkFailed += (reason) =>
        {
            if (InvokeRequired) { BeginInvoke(() => OnWalkFailed(reason)); return; }
            OnWalkFailed(reason);
        };

        _gameManager.RoomTracker.OnRoomChanged += (room) =>
        {
            if (InvokeRequired) { BeginInvoke(() => UpdateCurrentRoomDisplay()); return; }
            UpdateCurrentRoomDisplay();
        };
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        if (Owner != null)
        {
            var ob = Owner.Bounds;
            int x = ob.Left + (ob.Width - Width) / 2;
            int y = ob.Top + (ob.Height - Height) / 2;

            var work = Screen.FromControl(Owner).WorkingArea;
            x = Math.Max(work.Left, Math.Min(x, work.Right - Width));
            y = Math.Max(work.Top, Math.Min(y, work.Bottom - Height));

            Location = new Point(x, y);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Don't stop walk on close — let it continue in the background
        base.OnFormClosing(e);
    }

    #endregion

    #region Search

    private static readonly System.Text.RegularExpressions.Regex RoomKeyRegex = new(
        @"^(\d+)\s*/\s*(\d+)$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private void PerformSearch()
    {
        var query = _searchBox.Text.Trim();
        if (string.IsNullOrEmpty(query))
            return;

        _resultsList.Items.Clear();
        _previewPath = null;
        _destinationLabel.Text = "Destination: (none selected)";
        _pathInfoLabel.Text = "Path: —";
        UpdateWalkButtonState();

        // Direct room key lookup: "map/room" format (e.g., "9/62", "10/4")
        var keyMatch = RoomKeyRegex.Match(query);
        if (keyMatch.Success)
        {
            int map = int.Parse(keyMatch.Groups[1].Value);
            int room = int.Parse(keyMatch.Groups[2].Value);
            var directRoom = _gameManager.RoomGraph.GetRoom(map, room);

            if (directRoom != null)
            {
                var item = new ListViewItem(new[] { directRoom.Key, directRoom.Name });
                item.Tag = directRoom;
                _resultsList.Items.Add(item);
                _resultsList.Items[0].Selected = true;
            }
            else
            {
                var noResult = new ListViewItem(new[] { "", $"Room {map}/{room} not found" });
                noResult.ForeColor = TextDim;
                _resultsList.Items.Add(noResult);
            }
            return;
        }

        // Name search
        var results = _gameManager.RoomGraph.SearchByName(query, 200);

        if (results.Count == 0)
        {
            var noResult = new ListViewItem(new[] { "", "No rooms found" });
            noResult.ForeColor = TextDim;
            _resultsList.Items.Add(noResult);
            return;
        }

        foreach (var rm in results)
        {
            var item = new ListViewItem(new[] { rm.Key, rm.Name });
            item.Tag = rm;
            _resultsList.Items.Add(item);
        }

        // Auto-resize name column to fill
        if (_resultsList.Columns.Count >= 2)
        {
            _resultsList.Columns[1].Width = _resultsList.ClientSize.Width - _resultsList.Columns[0].Width - 4;
        }
    }

    #endregion

    #region Selection & Path Preview

    private void ResultsList_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_resultsList.SelectedItems.Count == 0 || _resultsList.SelectedItems[0].Tag == null)
        {
            _previewPath = null;
            _destinationLabel.Text = "Destination: (none selected)";
            _pathInfoLabel.Text = "Path: —";
            _blockingReasonsLabel.Visible = false;
            UpdateWalkButtonState();
            return;
        }

        var room = (RoomNode)_resultsList.SelectedItems[0].Tag!;
        _destinationLabel.Text = $"Destination: [{room.Key}] {room.Name}";

        // Calculate path preview
        var currentRoom = _gameManager.RoomTracker.CurrentRoom;
        if (currentRoom == null)
        {
            _pathInfoLabel.Text = "Path: Current location unknown — cannot calculate";
            _pathInfoLabel.ForeColor = AccentYellow;
            _previewPath = null;
            _blockingReasonsLabel.Visible = false;
        }
        else if (currentRoom.Key == room.Key)
        {
            _pathInfoLabel.Text = "Path: You are already here!";
            _pathInfoLabel.ForeColor = AccentGreen;
            _previewPath = null;
            _blockingReasonsLabel.Visible = false;
        }
        else
        {
            var path = _gameManager.RoomGraph.FindPath(currentRoom.Key, room.Key, _gameManager.GetExitFilter());
            if (path.Success)
            {
                _previewPath = path;
                _blockingReasonsLabel.Visible = false;
                var notes = new List<string>();
                if (path.Requirements.HasRemoteActionExits) notes.Add("remote-action detours");
                if (path.Requirements.HasTeleportExits) notes.Add("teleports");
                var noteText = notes.Count > 0 ? $" (includes {string.Join(", ", notes)})" : "";
                _pathInfoLabel.Text = $"Path: {path.Steps.Count} steps{noteText}";
                _pathInfoLabel.ForeColor = TextGray;
            }
            else
            {
                _previewPath = null;
                _blockingReasonsLabel.Visible = false;

                // Re-run without filter to diagnose WHY the path is blocked
                var unfilteredPath = _gameManager.RoomGraph.FindPath(currentRoom.Key, room.Key, includeAllExits: true);
                if (unfilteredPath.Success)
                {
                    // Path exists but player can't traverse it — find the blockers
                    var reasons = AnalyzeBlockingReasons(unfilteredPath);
                    _pathInfoLabel.Text = "Path: No route found — blocked by exit restrictions:";
                    _pathInfoLabel.ForeColor = AccentRed;
                    _blockingReasonsLabel.Text = string.Join("\n", reasons);
                    _blockingReasonsLabel.Visible = true;
                }
                else
                {
                    _pathInfoLabel.Text = "Path: No route found (rooms are not connected)";
                    _pathInfoLabel.ForeColor = AccentRed;
                }
            }
        }

        UpdateWalkButtonState();
    }

    private void ResultsList_DoubleClick(object? sender, EventArgs e)
    {
        // Double-click starts walking if a valid path is ready
        if (_previewPath != null && _walkButton.Enabled)
            WalkButton_Click(sender, e);
    }

    #endregion

    #region Walk / Stop

    private void WalkButton_Click(object? sender, EventArgs e)
    {
        if (_previewPath == null)
            return;

        _walkLog?.Close();
        _walkLog = DebugLogWriter.Create("walk");
        _gameManager.AutoWalkManager.SetDebugLog(_walkLog);

        // Expand remote-action exits into linear prerequisite sequences
        var expander = new RemoteActionPathExpander(
            _gameManager.RoomGraph,
            () => _gameManager.GetExitFilter());
        var expandedPath = expander.Expand(_previewPath);

        if (!expandedPath.Success)
        {
            _pathInfoLabel.Text = $"Path: Failed to expand remote-action prerequisites";
            _pathInfoLabel.ForeColor = AccentRed;
            return;
        }

        _gameManager.AutoWalkManager.StartWalk(expandedPath);

        // Disable search during walk
        _searchBox.Enabled = false;
        _searchButton.Enabled = false;
        _resultsList.Enabled = false;
        _walkButton.Enabled = false;
        _stopButton.Enabled = true;
    }

    private void StopButton_Click(object? sender, EventArgs e)
    {
        _gameManager.AutoWalkManager.Stop();
        ResetToSearchMode();
    }

    private void ResetToSearchMode()
    {
        _searchBox.Enabled = true;
        _searchButton.Enabled = true;
        _resultsList.Enabled = true;
        _stopButton.Enabled = false;
        UpdateWalkButtonState();
    }

    #endregion

    #region Display Updates

    private void UpdateCurrentRoomDisplay()
    {
        var room = _gameManager.RoomTracker.CurrentRoom;
        if (room != null)
        {
            _currentRoomLabel.Text = $"Current: [{room.Key}] {room.Name}";
            _currentRoomLabel.ForeColor = AccentTeal;
        }
        else
        {
            _currentRoomLabel.Text = "Current: (unknown)";
            _currentRoomLabel.ForeColor = TextDim;
        }

        // Refresh path preview if a destination is selected (current room might have changed)
        if (!_gameManager.AutoWalkManager.IsActive && _resultsList.SelectedItems.Count > 0)
        {
            ResultsList_SelectedIndexChanged(null, EventArgs.Empty);
        }
    }

    private void UpdateProgress(int step, int total, string roomName)
    {
        _progressBar.Maximum = total;
        _progressBar.Value = Math.Min(step, total);
        _progressLabel.Text = $"Step {step} of {total} — {roomName}";
    }

    private void UpdateStateDisplay(AutoWalkState state)
    {
        switch (state)
        {
            case AutoWalkState.Idle:
                _stateLabel.Text = "Idle";
                _stateLabel.ForeColor = TextDim;
                break;
            case AutoWalkState.Walking:
                _stateLabel.Text = "🚶 Walking";
                _stateLabel.ForeColor = AccentGreen;
                break;
            case AutoWalkState.WaitingForCombat:
                _stateLabel.Text = "⚔️ In Combat";
                _stateLabel.ForeColor = AccentRed;
                break;
            case AutoWalkState.Paused:
                _stateLabel.Text = "⏸️ Paused";
                _stateLabel.ForeColor = AccentYellow;
                break;
            case AutoWalkState.Completed:
                _stateLabel.Text = "✅ Arrived";
                _stateLabel.ForeColor = AccentGreen;
                ResetToSearchMode();
                break;
            case AutoWalkState.Failed:
                _stateLabel.Text = "❌ Failed";
                _stateLabel.ForeColor = AccentRed;
                ResetToSearchMode();
                break;
        }
    }

    private void OnWalkCompleted(string name)
    {
        _progressBar.Value = _progressBar.Maximum;
        _progressLabel.Text = $"Arrived at {name}";
        _gameManager.AutoWalkManager.SetDebugLog(null);
        _walkLog?.Close();
        _walkLog = null;
    }

    private void OnWalkFailed(string reason)
    {
        _progressLabel.Text = reason;
        _progressLabel.ForeColor = AccentRed;
        _gameManager.AutoWalkManager.SetDebugLog(null);
        _walkLog?.Close();
        _walkLog = null;
    }

    /// <summary>
    /// Analyze an unfiltered path to find which exits block the player.
    /// Called when the filtered path fails but the unfiltered path succeeds.
    /// </summary>
    private List<string> AnalyzeBlockingReasons(PathResult unfilteredPath)
    {
        var reasons = new List<string>();
        var playerInfo = _gameManager.PlayerStateManager.PlayerInfo;
        var exitFilter = _gameManager.GetExitFilter();
        var graph = _gameManager.RoomGraph;

        foreach (var step in unfilteredPath.Steps)
        {
            // Look up the actual exit on the source room to check all properties
            var room = graph.GetRoom(step.FromKey);
            if (room == null) continue;

            var exit = room.Exits.FirstOrDefault(e =>
                e.Direction == step.Direction && e.DestinationKey == step.ToKey);
            if (exit == null) continue;

            // Check if this exit would be filtered out
            if (!exit.Traversable || !exitFilter(exit))
            {
                var roomName = room.Name;
                var reason = DescribeBlockingReason(exit, playerInfo, graph);
                reasons.Add($"  [{step.FromKey}] {roomName} {exit.Direction}: {reason}");
            }
        }

        if (reasons.Count == 0)
            reasons.Add("  Unknown restriction");

        return reasons;
    }

    /// <summary>
    /// Describe why a specific exit blocks the player.
    /// </summary>
    private string DescribeBlockingReason(RoomExit exit, PlayerInfo playerInfo, RoomGraphManager graph)
    {
        // Non-traversable exit types
        if (exit.ExitType == RoomExitType.Locked)
        {
            if (exit.KeyItemId > 0)
            {
                var keyName = graph.GetItemName(exit.KeyItemId);
                return !string.IsNullOrEmpty(keyName)
                    ? $"Locked (requires {keyName})"
                    : $"Locked (requires key #{exit.KeyItemId})";
            }
            return "Locked (key required)";
        }

        if (exit.ExitType == RoomExitType.SearchableHidden)
            return "Hidden (requires search)";

        if (exit.ExitType == RoomExitType.MultiActionHidden)
        {
            var data = exit.MultiActionData;
            if (data != null && data.HasItemRequirements)
                return "Multi-action (requires items)";
            if (data != null && data.HasRemoteActions)
                return "Multi-action (remote actions not reachable)";
            return "Multi-action (deferred)";
        }

        if (exit.ExitType == RoomExitType.Teleport)
        {
            var reasons = new List<string>();
            // Level restriction (promoted from TeleportConditions.MinLevel/MaxLevel)
            if (exit.LevelRestriction != null)
                reasons.Add($"{exit.LevelRestriction} (you are level {playerInfo.Level})");
            // Runtime conditions
            var conds = exit.TeleportConditions;
            if (conds != null)
            {
                if (conds.RoomItemId > 0)
                {
                    var name = graph.GetItemName(conds.RoomItemId);
                    reasons.Add(!string.IsNullOrEmpty(name) ? $"room item: {name}" : $"room item #{conds.RoomItemId}");
                }
                if (conds.CheckItemId > 0)
                {
                    var name = graph.GetItemName(conds.CheckItemId);
                    reasons.Add(!string.IsNullOrEmpty(name) ? $"requires: {name}" : $"requires item #{conds.CheckItemId}");
                }
                if (conds.RequiresNoMonsters) reasons.Add("no monsters in room");
                if (conds.RequiresMonster) reasons.Add("monster must be present");
                if (!string.IsNullOrEmpty(conds.TestSkill)) reasons.Add($"skill: {conds.TestSkill}");
                if (conds.RequiresAbilityCheck) reasons.Add("ability check");
                if (conds.RequiresEvil) reasons.Add("evil alignment");
                if (conds.RequiresGood) reasons.Add("good alignment");
            }
            return reasons.Count > 0
                ? $"Teleport \"{exit.Command}\": {string.Join(", ", reasons)}"
                : $"Teleport \"{exit.Command}\": unknown condition";
        }

        // Stat-gated door
        if (exit.ExitType == RoomExitType.Door && exit.DoorStatRequirement > 0)
            return $"Door (requires {exit.DoorStatRequirement} STR or Picklocks)";

        // Level restriction
        if (exit.LevelRestriction != null)
        {
            var lr = exit.LevelRestriction;
            string range = lr.ToString();
            return $"{range} (you are level {playerInfo.Level})";
        }

        // Class restriction
        if (exit.ClassRestriction != null)
        {
            var names = exit.ClassRestriction.AllowedClassIds
                .Select(id => graph.GetClassName(id))
                .Where(n => !string.IsNullOrEmpty(n));
            return $"Class: {string.Join(", ", names)} only (you are {playerInfo.Class})";
        }

        // Race restriction
        if (exit.RaceRestriction != null)
        {
            var names = exit.RaceRestriction.AllowedRaceIds
                .Select(id => graph.GetRaceName(id))
                .Where(n => !string.IsNullOrEmpty(n));
            return $"Race: {string.Join(", ", names)} only (you are {playerInfo.Race})";
        }

        // Non-traversable for unknown reason
        if (!exit.Traversable)
            return $"Non-traversable ({exit.ExitType})";

        return "Unknown restriction";
    }

    private void UpdateWalkButtonState()
    {
        var walker = _gameManager.AutoWalkManager;

        if (walker.IsActive)
        {
            _walkButton.Enabled = false;
            _stopButton.Enabled = true;
        }
        else
        {
            _walkButton.Enabled = _previewPath != null;
            _stopButton.Enabled = false;
        }
    }

    #endregion
}
