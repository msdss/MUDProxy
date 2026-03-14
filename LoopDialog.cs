namespace MudProxyViewer;

/// <summary>
/// Dialog for creating, editing, loading, saving, and executing loops.
/// Shows live stats during loop execution (laps, exp/hr, runtime).
/// 
/// Uses:
///   - LoopManager for validation, execution, and stats
///   - RoomGraphManager for room lookups and exit validation
///   - RoomTracker for "Add Current Room" convenience
///   - LoopDefinition for file I/O
/// </summary>
public class LoopDialog : Form
{
    // ── Dependencies ──
    private readonly GameManager _gameManager;

    // ── Controls: Loop Editor ──
    private TextBox _loopNameBox = null!;
    private TextBox _notesBox = null!;
    private ListView _stepsList = null!;
    private TextBox _addRoomBox = null!;
    private Button _addButton = null!;
    private Button _addCurrentButton = null!;
    private Button _moveUpButton = null!;
    private Button _moveDownButton = null!;
    private Button _removeButton = null!;
    private Label _validationLabel = null!;
    private Label _stepsLabel = null!;
    private Label _addRoomLabel = null!;
    private Label _notesLabel = null!;
    private GroupBox _statusGroup = null!;

    // ── Controls: Status ──
    private Label _stateLabel = null!;
    private Label _lapLabel = null!;
    private Label _runtimeLabel = null!;
    private Label _expLabel = null!;
    private Label _expRateLabel = null!;
    private ProgressBar _progressBar = null!;
    //private Label _progressLabel = null!;

    // ── Controls: Buttons ──
    private Button _startButton = null!;
    private Button _stopButton = null!;
    private Button _pauseButton = null!;
    private Button _saveButton = null!;
    private Button _loadButton = null!;
    private Button _newButton = null!;

    // ── State ──
    private LoopDefinition _currentLoop = new();
    private LoopValidationResult? _lastValidation;
    private System.Windows.Forms.Timer? _statsTimer;
    private string? _currentFilePath;

    // ── Collapse State ──
    private bool _isCollapsed;
    private Size _expandedSize;
    private Size _expandedMinimumSize;
    private Point _statusGroupExpandedLocation;

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

    public LoopDialog(GameManager gameManager)
    {
        _gameManager = gameManager;
        InitializeComponent();

        _expandedSize = this.Size;
        _expandedMinimumSize = this.MinimumSize;

        WireEvents();
        UpdateButtonStates();
        ValidateLoop();
    }

    #region UI Construction

    private void InitializeComponent()
    {
        this.Text = "Loop Editor";
        this.Size = new Size(600, 720);
        this.MinimumSize = new Size(540, 660);
        this.StartPosition = FormStartPosition.Manual;
        this.BackColor = BgDark;
        this.ForeColor = TextWhite;
        this.FormBorderStyle = FormBorderStyle.Sizable;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.Font = new Font("Segoe UI", 9);

        int pad = 12;
        int contentWidth = this.ClientSize.Width - (pad * 2);
        int y = 10;

        // ── Loop Name ──
        var nameLabel = new Label
        {
            Text = "Loop Name:",
            Location = new Point(pad, y + 3),
            AutoSize = true,
            ForeColor = TextGray
        };
        this.Controls.Add(nameLabel);

        _loopNameBox = new TextBox
        {
            Location = new Point(pad + 80, y),
            Size = new Size(contentWidth - 80, 23),
            BackColor = BgMedium,
            ForeColor = TextWhite,
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        this.Controls.Add(_loopNameBox);
        y += 30;

        // ── Steps List ──
        _stepsLabel = new Label
        {
            Text = "Steps:",
            Location = new Point(pad, y),
            AutoSize = true,
            ForeColor = TextGray
        };
        this.Controls.Add(_stepsLabel);
        y += 20;

        _stepsList = new ListView
        {
            Location = new Point(pad, y),
            Size = new Size(contentWidth - 90, 200),
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            BackColor = BgMedium,
            ForeColor = TextWhite,
            BorderStyle = BorderStyle.FixedSingle,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };
        _stepsList.Columns.Add("#", 30);
        _stepsList.Columns.Add("Room Key", 70);
        _stepsList.Columns.Add("Room Name", 200);
        _stepsList.Columns.Add("Exit", 60);
        _stepsList.Columns.Add("Type", 60);
        _stepsList.SelectedIndexChanged += (s, e) => UpdateButtonStates();
        this.Controls.Add(_stepsList);

        // ── Step edit buttons (right of list) ──
        int bx = contentWidth + pad - 78;

        _moveUpButton = CreateSmallButton("▲ Up", bx, y, 78, 30);
        _moveUpButton.Click += MoveUpButton_Click;
        this.Controls.Add(_moveUpButton);

        _moveDownButton = CreateSmallButton("▼ Down", bx, y + 34, 78, 30);
        _moveDownButton.Click += MoveDownButton_Click;
        this.Controls.Add(_moveDownButton);

        _removeButton = CreateSmallButton("✕ Remove", bx, y + 68, 78, 30);
        _removeButton.Click += RemoveButton_Click;
        this.Controls.Add(_removeButton);

        // Anchor edit buttons to the right
        _moveUpButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _moveDownButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _removeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        y += 208;

        // ── Add Room Row ──
        _addRoomLabel = new Label
        {
            Text = "Add Room:",
            Location = new Point(pad, y + 3),
            AutoSize = true,
            ForeColor = TextGray,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        this.Controls.Add(_addRoomLabel);

        _addRoomBox = new TextBox
        {
            Location = new Point(pad + 70, y),
            Size = new Size(100, 23),
            BackColor = BgMedium,
            ForeColor = TextWhite,
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        _addRoomBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { AddRoom(); e.SuppressKeyPress = true; } };
        this.Controls.Add(_addRoomBox);

        _addButton = CreateSmallButton("Add", pad + 176, y, 50, 25);
        _addButton.Click += (s, e) => AddRoom();
        _addButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        this.Controls.Add(_addButton);

        _addCurrentButton = CreateSmallButton("Add Current Room", pad + 232, y, 130, 25);
        _addCurrentButton.Click += (s, e) => AddCurrentRoom();
        _addCurrentButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        this.Controls.Add(_addCurrentButton);

        y += 30;

        // ── Validation ──
        _validationLabel = new Label
        {
            Text = "",
            Location = new Point(pad, y),
            Size = new Size(contentWidth, 34),
            ForeColor = TextDim,
            Font = new Font("Segoe UI", 8.5f),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        this.Controls.Add(_validationLabel);
        y += 38;

        // ── Notes ──
        _notesLabel = new Label
        {
            Text = "Notes:",
            Location = new Point(pad, y + 2),
            AutoSize = true,
            ForeColor = TextGray,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        this.Controls.Add(_notesLabel);

        _notesBox = new TextBox
        {
            Location = new Point(pad + 50, y),
            Size = new Size(contentWidth - 50, 23),
            BackColor = BgMedium,
            ForeColor = TextWhite,
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        this.Controls.Add(_notesBox);
        y += 32;

        // ── Status Panel ──
        _statusGroup = new GroupBox
        {
            Text = "Status",
            Location = new Point(pad, y),
            Size = new Size(contentWidth, 130),
            ForeColor = TextGray,
            BackColor = BgDark,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        int sy = 18;

        _stateLabel = new Label
        {
            Text = "State: Idle",
            Location = new Point(10, sy),
            Size = new Size(200, 18),
            ForeColor = TextDim
        };
        _statusGroup.Controls.Add(_stateLabel);

        _lapLabel = new Label
        {
            Text = "Laps: 0",
            Location = new Point(220, sy),
            Size = new Size(120, 18),
            ForeColor = TextDim
        };
        _statusGroup.Controls.Add(_lapLabel);
        sy += 20;

        _runtimeLabel = new Label
        {
            Text = "Runtime: —",
            Location = new Point(10, sy),
            Size = new Size(200, 18),
            ForeColor = TextDim
        };
        _statusGroup.Controls.Add(_runtimeLabel);
        sy += 20;

        _expLabel = new Label
        {
            Text = "EXP Gained: —",
            Location = new Point(10, sy),
            Size = new Size(200, 18),
            ForeColor = TextDim
        };
        _statusGroup.Controls.Add(_expLabel);

        _expRateLabel = new Label
        {
            Text = "Rate: —",
            Location = new Point(220, sy),
            Size = new Size(200, 18),
            ForeColor = TextDim
        };
        _statusGroup.Controls.Add(_expRateLabel);
        sy += 24;

        _progressBar = new ProgressBar
        {
            Location = new Point(10, sy),
            Size = new Size(_statusGroup.Width - 20, 18),
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Style = ProgressBarStyle.Continuous,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _statusGroup.Controls.Add(_progressBar);

        this.Controls.Add(_statusGroup);
        y += 138;

        // ── Button Row (docked to bottom) ──
        var buttonPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            BackColor = BgPanel
        };

        _startButton = CreateThemedButton("▶ Start", 0, 0, 80, 32);
        _startButton.Click += StartButton_Click;

        _pauseButton = CreateThemedButton("⏸ Pause", 0, 0, 80, 32);
        _pauseButton.Click += PauseButton_Click;

        _stopButton = CreateThemedButton("⏹ Stop", 0, 0, 80, 32);
        _stopButton.Click += StopButton_Click;

        _saveButton = CreateThemedButton("💾 Save", 0, 0, 80, 32);
        _saveButton.Click += SaveButton_Click;

        _loadButton = CreateThemedButton("📂 Load", 0, 0, 80, 32);
        _loadButton.Click += LoadButton_Click;

        _newButton = CreateThemedButton("📄 New", 0, 0, 80, 32);
        _newButton.Click += NewButton_Click;

        // Position buttons centered
        var buttons = new[] { _startButton, _pauseButton, _stopButton, _saveButton, _loadButton, _newButton };
        int totalWidth = buttons.Length * 84;
        int startX = (this.ClientSize.Width - totalWidth) / 2;
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].Location = new Point(startX + i * 84, 9);
            buttonPanel.Controls.Add(buttons[i]);
        }

        this.Controls.Add(buttonPanel);
    }

    private Button CreateSmallButton(string text, int x, int y, int w, int h)
    {
        var btn = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(w, h),
            BackColor = BgButton,
            ForeColor = TextWhite,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8)
        };
        btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
        return btn;
    }

    private Button CreateThemedButton(string text, int x, int y, int w, int h)
    {
        var btn = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(w, h),
            BackColor = BgButton,
            ForeColor = TextWhite,
            FlatStyle = FlatStyle.Flat
        };
        btn.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
        return btn;
    }

    #endregion

    #region Event Wiring

    private void WireEvents()
    {
        var loopMgr = _gameManager.LoopManager;

        loopMgr.OnLoopStateChanged += (state) =>
        {
            if (InvokeRequired) { BeginInvoke(() => UpdateLoopStateDisplay(state)); return; }
            UpdateLoopStateDisplay(state);
        };

        loopMgr.OnLapCompleted += (lap) =>
        {
            if (InvokeRequired) { BeginInvoke(() => UpdateStats()); return; }
            UpdateStats();
        };

        loopMgr.OnLoopFailed += (reason) =>
        {
            if (InvokeRequired) { BeginInvoke(() => OnLoopFailed(reason)); return; }
            OnLoopFailed(reason);
        };

        loopMgr.OnLoopStatsUpdated += () =>
        {
            if (InvokeRequired) { BeginInvoke(() => UpdateStats()); return; }
            UpdateStats();
        };

        // Walk progress for the progress bar
        _gameManager.AutoWalkManager.OnWalkProgress += (step, total, name) =>
        {
            if (InvokeRequired) { BeginInvoke(() => UpdateWalkProgress(step, total, name)); return; }
            UpdateWalkProgress(step, total, name);
        };

        // Stats timer — update runtime/exp display every second during loops
        _statsTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _statsTimer.Tick += (s, e) =>
        {
            if (_gameManager.LoopManager.IsActive)
                UpdateStats();
        };
        _statsTimer.Start();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // CenterParent is unreliable for modeless dialogs (.Show).
        // Manually center on the owner's bounds, clamped to the owner's screen.
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
        _statsTimer?.Stop();
        _statsTimer?.Dispose();
        // Don't stop loop on close — let it continue in the background
        base.OnFormClosing(e);
    }

    #endregion

    #region Step Management

    private void AddRoom()
    {
        var roomKey = _addRoomBox.Text.Trim();
        if (string.IsNullOrEmpty(roomKey))
            return;

        // Validate room exists
        var room = _gameManager.RoomGraph.GetRoom(roomKey);
        if (room == null)
        {
            _validationLabel.Text = $"❌ Room {roomKey} not found in room data.";
            _validationLabel.ForeColor = AccentRed;
            return;
        }

        _currentLoop.Steps.Add(new LoopStep
        {
            RoomKey = room.Key,
            RoomName = room.Name
        });

        _addRoomBox.Clear();
        _addRoomBox.Focus();
        RefreshStepsList();
        ValidateLoop();
    }

    private void AddCurrentRoom()
    {
        var currentRoom = _gameManager.RoomTracker.CurrentRoom;
        if (currentRoom == null)
        {
            _validationLabel.Text = "❌ Current room unknown — cannot add.";
            _validationLabel.ForeColor = AccentRed;
            return;
        }

        _currentLoop.Steps.Add(new LoopStep
        {
            RoomKey = currentRoom.Key,
            RoomName = currentRoom.Name
        });

        RefreshStepsList();
        ValidateLoop();
    }

    private void MoveUpButton_Click(object? sender, EventArgs e)
    {
        if (_stepsList.SelectedIndices.Count == 0)
            return;

        int idx = _stepsList.SelectedIndices[0];
        if (idx <= 0)
            return;

        var step = _currentLoop.Steps[idx];
        _currentLoop.Steps.RemoveAt(idx);
        _currentLoop.Steps.Insert(idx - 1, step);

        RefreshStepsList();
        _stepsList.Items[idx - 1].Selected = true;
        ValidateLoop();
    }

    private void MoveDownButton_Click(object? sender, EventArgs e)
    {
        if (_stepsList.SelectedIndices.Count == 0)
            return;

        int idx = _stepsList.SelectedIndices[0];
        if (idx >= _currentLoop.Steps.Count - 1)
            return;

        var step = _currentLoop.Steps[idx];
        _currentLoop.Steps.RemoveAt(idx);
        _currentLoop.Steps.Insert(idx + 1, step);

        RefreshStepsList();
        _stepsList.Items[idx + 1].Selected = true;
        ValidateLoop();
    }

    private void RemoveButton_Click(object? sender, EventArgs e)
    {
        if (_stepsList.SelectedIndices.Count == 0)
            return;

        int idx = _stepsList.SelectedIndices[0];
        _currentLoop.Steps.RemoveAt(idx);

        RefreshStepsList();

        // Re-select nearest item
        if (_stepsList.Items.Count > 0)
        {
            int newIdx = Math.Min(idx, _stepsList.Items.Count - 1);
            _stepsList.Items[newIdx].Selected = true;
        }

        ValidateLoop();
    }

    #endregion

    #region Steps Display

    /// <summary>
    /// Rebuild the steps ListView from the current loop definition.
    /// Shows room key, room name, exit command to next room, and exit type.
    /// </summary>
    private void RefreshStepsList()
    {
        _stepsList.Items.Clear();

        for (int i = 0; i < _currentLoop.Steps.Count; i++)
        {
            var step = _currentLoop.Steps[i];
            var toStep = _currentLoop.Steps[(i + 1) % _currentLoop.Steps.Count];

            // Look up room name (refresh cached name)
            var room = _gameManager.RoomGraph.GetRoom(step.RoomKey);
            if (room != null)
                step.RoomName = room.Name;

            // Find exit to next room
            string exitCmd = "";
            string exitType = "";
            if (_currentLoop.Steps.Count >= 2 && room != null)
            {
                var exit = FindBestExit(room, toStep.RoomKey);
                if (exit != null)
                {
                    exitCmd = exit.Command;
                    exitType = exit.ExitType switch
                    {
                        RoomExitType.Door => "🚪 Door",
                        RoomExitType.Text => "📝 Text",
                        RoomExitType.Hidden => "👁 Hidden",
                        RoomExitType.Locked => "🔒 Locked",
                        _ => ""
                    };
                }
                else
                {
                    exitCmd = "???";
                    exitType = "❌ None";
                }
            }

            var item = new ListViewItem(new[]
            {
                (i + 1).ToString(),
                step.RoomKey,
                step.RoomName,
                exitCmd,
                exitType
            });

            // Color the loop-back row differently
            if (i == _currentLoop.Steps.Count - 1 && _currentLoop.Steps.Count >= 2)
            {
                // Check if loop-back exit exists
                var lastRoom = _gameManager.RoomGraph.GetRoom(step.RoomKey);
                var firstKey = _currentLoop.Steps[0].RoomKey;
                if (lastRoom != null && FindBestExit(lastRoom, firstKey) == null)
                    item.ForeColor = AccentRed;
            }

            _stepsList.Items.Add(item);
        }

        // Auto-resize columns
        if (_stepsList.Columns.Count >= 5)
        {
            _stepsList.Columns[2].Width = _stepsList.ClientSize.Width
                - _stepsList.Columns[0].Width
                - _stepsList.Columns[1].Width
                - _stepsList.Columns[3].Width
                - _stepsList.Columns[4].Width - 4;
        }

        UpdateButtonStates();
    }

    /// <summary>
    /// Find the best traversable exit from a room to a destination key.
    /// </summary>
    private RoomExit? FindBestExit(RoomNode room, string toKey)
    {
        foreach (var exit in room.Exits)
        {
            if (exit.DestinationKey == toKey && exit.Traversable)
                return exit;
        }
        return null;
    }

    #endregion

    #region Validation

    private void ValidateLoop()
    {
        if (_currentLoop.Steps.Count < 2)
        {
            _lastValidation = null;
            _validationLabel.Text = _currentLoop.Steps.Count == 0
                ? "Add rooms to define the loop path."
                : "Add at least one more room to complete the loop.";
            _validationLabel.ForeColor = TextDim;
            UpdateButtonStates();
            return;
        }

        _lastValidation = _gameManager.LoopManager.Validate(_currentLoop);

        if (_lastValidation.IsValid)
        {
            var reqText = "";
            if (_lastValidation.Requirements.HasDoors)
            {
                var maxReq = _lastValidation.Requirements.MaxDoorStatRequirement;
                reqText = maxReq > 0
                    ? $" | Doors: {maxReq} str/pick"
                    : " | Has doors";
            }

            _validationLabel.Text = $"✅ Valid — {_currentLoop.Steps.Count} waypoints, {_lastValidation.TotalMoveCommands} steps{reqText}";
            _validationLabel.ForeColor = AccentGreen;
        }
        else
        {
            var firstError = _lastValidation.Errors.FirstOrDefault() ?? "Unknown error";
            _validationLabel.Text = $"❌ {firstError}";
            _validationLabel.ForeColor = AccentRed;
        }

        UpdateButtonStates();
    }

    #endregion

    #region Start / Stop / Pause

    private void StartButton_Click(object? sender, EventArgs e)
    {
        // Sync name and notes to the definition
        _currentLoop.Name = _loopNameBox.Text.Trim();
        _currentLoop.Notes = _notesBox.Text.Trim();

        if (string.IsNullOrEmpty(_currentLoop.Name))
        {
            _validationLabel.Text = "❌ Please enter a loop name.";
            _validationLabel.ForeColor = AccentRed;
            _loopNameBox.Focus();
            return;
        }

        var success = _gameManager.LoopManager.Start(_currentLoop);
        if (success)
        {
            SetEditorEnabled(false);
            CollapseToRunningView();
            UpdateStats();
        }
    }

    private void PauseButton_Click(object? sender, EventArgs e)
    {
        var loopMgr = _gameManager.LoopManager;
        if (loopMgr.State == LoopState.Running)
        {
            loopMgr.Pause();
            _pauseButton.Text = "▶ Resume";
        }
        else if (loopMgr.State == LoopState.Paused)
        {
            loopMgr.Resume();
            _pauseButton.Text = "⏸ Pause";
        }
    }

    private void StopButton_Click(object? sender, EventArgs e)
    {
        _gameManager.LoopManager.Stop();
        SetEditorEnabled(true);
        ExpandToFullView();
        _pauseButton.Text = "⏸ Pause";
    }

    #endregion

    #region Save / Load / New

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        _currentLoop.Name = _loopNameBox.Text.Trim();
        _currentLoop.Notes = _notesBox.Text.Trim();

        if (string.IsNullOrEmpty(_currentLoop.Name))
        {
            _validationLabel.Text = "❌ Please enter a loop name before saving.";
            _validationLabel.ForeColor = AccentRed;
            _loopNameBox.Focus();
            return;
        }

        // Set metadata
        var playerInfo = _gameManager.PlayerStateManager.PlayerInfo;
        if (!string.IsNullOrEmpty(playerInfo.Name))
            _currentLoop.CreatedBy = playerInfo.Name;
        _currentLoop.CreatedDate = DateTime.Now;

        // Default to Loops directory with sanitized name
        var loopsDir = LoopDefinition.GetLoopsDirectory();
        var safeName = SanitizeFileName(_currentLoop.Name);
        var defaultPath = _currentFilePath ?? Path.Combine(loopsDir, safeName + LoopDefinition.FileExtension);

        using var dialog = new SaveFileDialog
        {
            Title = "Save Loop",
            Filter = $"Loop Files (*{LoopDefinition.FileExtension})|*{LoopDefinition.FileExtension}",
            InitialDirectory = loopsDir,
            FileName = Path.GetFileName(defaultPath)
        };

        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        if (LoopDefinition.SaveToFile(_currentLoop, dialog.FileName))
        {
            _currentFilePath = dialog.FileName;
            _validationLabel.Text = $"💾 Saved: {Path.GetFileName(dialog.FileName)}";
            _validationLabel.ForeColor = AccentGreen;
        }
        else
        {
            _validationLabel.Text = "❌ Failed to save file.";
            _validationLabel.ForeColor = AccentRed;
        }
    }

    private void LoadButton_Click(object? sender, EventArgs e)
    {
        var loopsDir = LoopDefinition.GetLoopsDirectory();

        using var dialog = new OpenFileDialog
        {
            Title = "Load Loop",
            Filter = $"Loop Files (*{LoopDefinition.FileExtension})|*{LoopDefinition.FileExtension}",
            InitialDirectory = loopsDir
        };

        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        var loop = LoopDefinition.LoadFromFile(dialog.FileName);
        if (loop == null)
        {
            _validationLabel.Text = "❌ Failed to load file.";
            _validationLabel.ForeColor = AccentRed;
            return;
        }

        _currentLoop = loop;
        _currentFilePath = dialog.FileName;
        _loopNameBox.Text = loop.Name;
        _notesBox.Text = loop.Notes;

        RefreshStepsList();
        ValidateLoop();
        _validationLabel.Text = $"📂 Loaded: {Path.GetFileName(dialog.FileName)}";
        _validationLabel.ForeColor = AccentGreen;
    }

    private void NewButton_Click(object? sender, EventArgs e)
    {
        if (_currentLoop.Steps.Count > 0)
        {
            var result = MessageBox.Show(
                "Clear the current loop and start a new one?",
                "New Loop",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
                return;
        }

        _currentLoop = new LoopDefinition();
        _currentFilePath = null;
        _loopNameBox.Clear();
        _notesBox.Clear();

        RefreshStepsList();
        ValidateLoop();
    }

    #endregion

    #region Display Updates

    private void UpdateLoopStateDisplay(LoopState state)
    {
        switch (state)
        {
            case LoopState.Idle:
                _stateLabel.Text = "State: Idle";
                _stateLabel.ForeColor = TextDim;
                SetEditorEnabled(true);
                ExpandToFullView();
                _pauseButton.Text = "⏸ Pause";
                break;
            case LoopState.Running:
                _stateLabel.Text = "State: 🔄 Running";
                _stateLabel.ForeColor = AccentGreen;
                break;
            case LoopState.Paused:
                _stateLabel.Text = "State: ⏸️ Paused";
                _stateLabel.ForeColor = AccentYellow;
                break;
            case LoopState.Failed:
                _stateLabel.Text = "State: ❌ Failed";
                _stateLabel.ForeColor = AccentRed;
                SetEditorEnabled(true);
                ExpandToFullView();
                _pauseButton.Text = "⏸ Pause";
                break;
        }

        UpdateButtonStates();
    }

    private void UpdateStats()
    {
        var loopMgr = _gameManager.LoopManager;

        _lapLabel.Text = $"Laps: {loopMgr.CurrentLap}";
        _lapLabel.ForeColor = loopMgr.IsActive ? TextWhite : TextDim;

        if (loopMgr.IsActive || loopMgr.State == LoopState.Failed)
        {
            _runtimeLabel.Text = $"Runtime: {loopMgr.Runtime:hh\\:mm\\:ss}";
            _runtimeLabel.ForeColor = TextWhite;

            var expGained = loopMgr.ExpGainedDuringLoop;
            _expLabel.Text = $"EXP Gained: {ExperienceTracker.FormatNumber(expGained)}";
            _expLabel.ForeColor = TextWhite;

            var expRate = loopMgr.ExpPerHourDuringLoop;
            _expRateLabel.Text = $"Rate: {ExperienceTracker.FormatNumber(expRate)}/hr";
            _expRateLabel.ForeColor = expRate > 0 ? AccentGreen : TextDim;
        }
        else
        {
            _runtimeLabel.Text = "Runtime: —";
            _runtimeLabel.ForeColor = TextDim;
            _expLabel.Text = "EXP Gained: —";
            _expLabel.ForeColor = TextDim;
            _expRateLabel.Text = "Rate: —";
            _expRateLabel.ForeColor = TextDim;
        }
    }

    private void UpdateWalkProgress(int step, int total, string name)
    {
        if (!_gameManager.LoopManager.IsActive)
            return;

        _progressBar.Maximum = total;
        _progressBar.Value = Math.Min(step, total);
    }

    private void OnLoopFailed(string reason)
    {
        _validationLabel.Text = $"❌ {reason}";
        _validationLabel.ForeColor = AccentRed;
        UpdateStats();
    }

    private void CollapseToRunningView()
    {
        if (_isCollapsed) return;

        // Capture current state (user may have resized the dialog)
        _expandedSize = this.Size;
        _statusGroupExpandedLocation = _statusGroup.Location;
        _isCollapsed = true;

        this.SuspendLayout();

        // Hide all editor controls
        _stepsLabel.Visible = false;
        _stepsList.Visible = false;
        _moveUpButton.Visible = false;
        _moveDownButton.Visible = false;
        _removeButton.Visible = false;
        _addRoomLabel.Visible = false;
        _addRoomBox.Visible = false;
        _addButton.Visible = false;
        _addCurrentButton.Visible = false;
        _validationLabel.Visible = false;
        _notesLabel.Visible = false;
        _notesBox.Visible = false;

        // Reposition status group just below the loop name row
        _statusGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _statusGroup.Top = _loopNameBox.Bottom + 8;

        // Shrink the form — status group + padding + button panel (50px docked bottom)
        int frameExtra = this.Height - this.ClientSize.Height;
        int collapsedHeight = _statusGroup.Bottom + 8 + 50 + frameExtra;

        this.MinimumSize = Size.Empty;
        this.MaximumSize = Size.Empty;
        this.Size = new Size(this.Width, collapsedHeight);
        this.MinimumSize = this.Size;
        this.MaximumSize = this.Size;

        this.ResumeLayout(true);
    }

    private void ExpandToFullView()
    {
        if (!_isCollapsed) return;
        _isCollapsed = false;

        this.SuspendLayout();

        // Remove size lock and restore expanded dimensions
        this.MinimumSize = Size.Empty;
        this.MaximumSize = Size.Empty;
        this.Size = _expandedSize;
        this.MinimumSize = _expandedMinimumSize;

        // Restore status group position and anchor
        _statusGroup.Location = _statusGroupExpandedLocation;
        _statusGroup.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        // Show all editor controls
        _stepsLabel.Visible = true;
        _stepsList.Visible = true;
        _moveUpButton.Visible = true;
        _moveDownButton.Visible = true;
        _removeButton.Visible = true;
        _addRoomLabel.Visible = true;
        _addRoomBox.Visible = true;
        _addButton.Visible = true;
        _addCurrentButton.Visible = true;
        _validationLabel.Visible = true;
        _notesLabel.Visible = true;
        _notesBox.Visible = true;

        this.ResumeLayout(true);
    }

    private void UpdateButtonStates()
    {
        var loopMgr = _gameManager.LoopManager;
        bool isActive = loopMgr.IsActive;
        bool hasSelection = _stepsList.SelectedIndices.Count > 0;
        bool isValid = _lastValidation?.IsValid == true;

        _startButton.Enabled = !isActive && isValid;
        _stopButton.Enabled = isActive;
        _pauseButton.Enabled = isActive;

        _saveButton.Enabled = !isActive && _currentLoop.Steps.Count > 0;
        _loadButton.Enabled = !isActive;
        _newButton.Enabled = !isActive;

        _addButton.Enabled = !isActive;
        _addCurrentButton.Enabled = !isActive;
        _addRoomBox.Enabled = !isActive;
        _moveUpButton.Enabled = !isActive && hasSelection && _stepsList.SelectedIndices[0] > 0;
        _moveDownButton.Enabled = !isActive && hasSelection && _stepsList.SelectedIndices[0] < _stepsList.Items.Count - 1;
        _removeButton.Enabled = !isActive && hasSelection;
    }

    private void SetEditorEnabled(bool enabled)
    {
        _loopNameBox.Enabled = enabled;
        _notesBox.Enabled = enabled;
        _stepsList.Enabled = enabled;
        _addRoomBox.Enabled = enabled;
        _addButton.Enabled = enabled;
        _addCurrentButton.Enabled = enabled;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
    }

    #endregion
}
