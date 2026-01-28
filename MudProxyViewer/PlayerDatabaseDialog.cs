namespace MudProxyViewer;

public class PlayerDatabaseDialog : Form
{
    private readonly PlayerDatabaseManager _databaseManager;
    
    private TextBox _searchTextBox = null!;
    private ListView _playerListView = null!;
    private Label _statusLabel = null!;
    private Button _deleteButton = null!;
    private Button _editButton = null!;
    
    private string _sortColumn = "FirstName";
    private bool _sortAscending = true;
    
    public PlayerDatabaseDialog(PlayerDatabaseManager databaseManager)
    {
        _databaseManager = databaseManager;
        InitializeComponent();
        RefreshPlayerList();
    }
    
    private void InitializeComponent()
    {
        this.Text = "Player Database";
        this.Size = new Size(520, 400);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.StartPosition = FormStartPosition.CenterParent;
        this.MaximizeBox = false;
        this.BackColor = Color.FromArgb(45, 45, 45);
        
        // Search panel
        var searchPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(10, 5, 10, 5)
        };
        
        var searchLabel = new Label
        {
            Text = "Search:",
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(10, 12)
        };
        
        _searchTextBox = new TextBox
        {
            Location = new Point(70, 8),
            Width = 200,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        _searchTextBox.TextChanged += SearchTextBox_TextChanged;
        
        _statusLabel = new Label
        {
            Text = "0 players",
            ForeColor = Color.Gray,
            AutoSize = true,
            Location = new Point(290, 12)
        };
        
        searchPanel.Controls.AddRange(new Control[] { searchLabel, _searchTextBox, _statusLabel });
        
        // Player list
        _playerListView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            BackColor = Color.FromArgb(35, 35, 35),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.None
        };
        
        _playerListView.Columns.Add("First Name", 120);
        _playerListView.Columns.Add("Last Name", 120);
        _playerListView.Columns.Add("Relationship", 100);
        _playerListView.Columns.Add("Last Seen", 140);
        
        _playerListView.ColumnClick += PlayerListView_ColumnClick;
        _playerListView.DoubleClick += PlayerListView_DoubleClick;
        _playerListView.SelectedIndexChanged += PlayerListView_SelectedIndexChanged;
        
        // Button panel
        var buttonPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            Padding = new Padding(10)
        };
        
        _editButton = new Button
        {
            Text = "Edit",
            Width = 80,
            Height = 30,
            Location = new Point(10, 10),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Enabled = false
        };
        _editButton.Click += EditButton_Click;
        
        _deleteButton = new Button
        {
            Text = "Delete",
            Width = 80,
            Height = 30,
            Location = new Point(100, 10),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Enabled = false
        };
        _deleteButton.Click += DeleteButton_Click;
        
        var closeButton = new Button
        {
            Text = "Close",
            Width = 80,
            Height = 30,
            Location = new Point(420, 10),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        closeButton.Click += (s, e) => this.Close();
        
        // Help text
        var helpLabel = new Label
        {
            Text = "Tip: Use 'who' in-game to populate",
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 8),
            AutoSize = true,
            Location = new Point(190, 15)
        };
        
        buttonPanel.Controls.AddRange(new Control[] { _editButton, _deleteButton, closeButton, helpLabel });
        
        // Add controls in order
        this.Controls.Add(_playerListView);
        this.Controls.Add(searchPanel);
        this.Controls.Add(buttonPanel);
    }
    
    private void RefreshPlayerList()
    {
        _playerListView.Items.Clear();
        
        var searchTerm = _searchTextBox.Text;
        var players = string.IsNullOrWhiteSpace(searchTerm) 
            ? _databaseManager.Players 
            : _databaseManager.SearchPlayers(searchTerm);
        
        // Apply sorting
        var sortedPlayers = ApplySorting(players);
        
        foreach (var player in sortedPlayers)
        {
            var item = new ListViewItem(player.FirstName);
            item.SubItems.Add(player.LastName);
            item.SubItems.Add(player.Relationship.ToString());
            item.SubItems.Add(player.LastSeen.ToString("yyyy-MM-dd HH:mm"));
            item.Tag = player;
            
            _playerListView.Items.Add(item);
        }
        
        _statusLabel.Text = $"{_playerListView.Items.Count} player(s)";
    }
    
    private IEnumerable<PlayerData> ApplySorting(IEnumerable<PlayerData> players)
    {
        return _sortColumn switch
        {
            "FirstName" => _sortAscending 
                ? players.OrderBy(p => p.FirstName, StringComparer.OrdinalIgnoreCase)
                : players.OrderByDescending(p => p.FirstName, StringComparer.OrdinalIgnoreCase),
            "LastName" => _sortAscending
                ? players.OrderBy(p => p.LastName, StringComparer.OrdinalIgnoreCase)
                : players.OrderByDescending(p => p.LastName, StringComparer.OrdinalIgnoreCase),
            "Relationship" => _sortAscending
                ? players.OrderBy(p => GetRelationshipSortOrder(p.Relationship))
                : players.OrderByDescending(p => GetRelationshipSortOrder(p.Relationship)),
            _ => players.OrderBy(p => p.FirstName, StringComparer.OrdinalIgnoreCase)
        };
    }
    
    private int GetRelationshipSortOrder(PlayerRelationship relationship)
    {
        // Friend = 0, Neutral = 1, Enemy = 2
        return relationship switch
        {
            PlayerRelationship.Friend => 0,
            PlayerRelationship.Neutral => 1,
            PlayerRelationship.Enemy => 2,
            _ => 1
        };
    }
    
    private void PlayerListView_ColumnClick(object? sender, ColumnClickEventArgs e)
    {
        // Only allow sorting on first 3 columns (not Last Seen)
        if (e.Column >= 3) return;
        
        var columnName = e.Column switch
        {
            0 => "FirstName",
            1 => "LastName",
            2 => "Relationship",
            _ => "FirstName"
        };
        
        if (_sortColumn == columnName)
        {
            // Toggle sort direction
            _sortAscending = !_sortAscending;
        }
        else
        {
            // New column, default to ascending
            _sortColumn = columnName;
            _sortAscending = true;
        }
        
        RefreshPlayerList();
    }
    
    private void SearchTextBox_TextChanged(object? sender, EventArgs e)
    {
        RefreshPlayerList();
    }
    
    private void PlayerListView_SelectedIndexChanged(object? sender, EventArgs e)
    {
        var hasSelection = _playerListView.SelectedItems.Count > 0;
        _editButton.Enabled = hasSelection;
        _deleteButton.Enabled = hasSelection;
    }
    
    private void PlayerListView_DoubleClick(object? sender, EventArgs e)
    {
        EditSelectedPlayer();
    }
    
    private void EditButton_Click(object? sender, EventArgs e)
    {
        EditSelectedPlayer();
    }
    
    private void EditSelectedPlayer()
    {
        if (_playerListView.SelectedItems.Count == 0) return;
        
        var player = _playerListView.SelectedItems[0].Tag as PlayerData;
        if (player == null) return;
        
        using var dialog = new PlayerEditDialog(player);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _databaseManager.UpdatePlayer(player);
            RefreshPlayerList();
        }
    }
    
    private void DeleteButton_Click(object? sender, EventArgs e)
    {
        if (_playerListView.SelectedItems.Count == 0) return;
        
        var player = _playerListView.SelectedItems[0].Tag as PlayerData;
        if (player == null) return;
        
        var result = MessageBox.Show(
            $"Delete player '{player.FullName}' from the database?",
            "Confirm Delete",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        
        if (result == DialogResult.Yes)
        {
            _databaseManager.RemovePlayer(player.FirstName);
            RefreshPlayerList();
        }
    }
}

public class PlayerEditDialog : Form
{
    private readonly PlayerData _player;
    
    private TextBox _firstNameTextBox = null!;
    private TextBox _lastNameTextBox = null!;
    private ComboBox _relationshipComboBox = null!;
    
    public PlayerEditDialog(PlayerData player)
    {
        _player = player;
        InitializeComponent();
        LoadPlayerData();
    }
    
    private void InitializeComponent()
    {
        this.Text = "Edit Player";
        this.Size = new Size(350, 220);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.StartPosition = FormStartPosition.CenterParent;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.BackColor = Color.FromArgb(45, 45, 45);
        
        int y = 15;
        int controlLeft = 110;
        int controlWidth = 200;
        int rowHeight = 32;
        
        // First Name (read-only)
        AddLabel("First Name:", 20, y + 3);
        _firstNameTextBox = new TextBox
        {
            Location = new Point(controlLeft, y),
            Width = controlWidth,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.Gray,
            ReadOnly = true
        };
        this.Controls.Add(_firstNameTextBox);
        y += rowHeight;
        
        // Last Name (read-only)
        AddLabel("Last Name:", 20, y + 3);
        _lastNameTextBox = new TextBox
        {
            Location = new Point(controlLeft, y),
            Width = controlWidth,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.Gray,
            ReadOnly = true
        };
        this.Controls.Add(_lastNameTextBox);
        y += rowHeight;
        
        // Relationship
        AddLabel("Relationship:", 20, y + 3);
        _relationshipComboBox = new ComboBox
        {
            Location = new Point(controlLeft, y),
            Width = controlWidth,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        foreach (var relationship in Enum.GetValues<PlayerRelationship>())
        {
            _relationshipComboBox.Items.Add(relationship);
        }
        this.Controls.Add(_relationshipComboBox);
        y += rowHeight + 20;
        
        // Buttons
        var saveButton = new Button
        {
            Text = "Save",
            DialogResult = DialogResult.OK,
            Width = 80,
            Height = 30,
            Location = new Point(150, y),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        saveButton.Click += SaveButton_Click;
        
        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Width = 80,
            Height = 30,
            Location = new Point(240, y),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        
        this.Controls.AddRange(new Control[] { saveButton, cancelButton });
        this.AcceptButton = saveButton;
        this.CancelButton = cancelButton;
    }
    
    private void AddLabel(string text, int x, int y)
    {
        var label = new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = Color.White
        };
        this.Controls.Add(label);
    }
    
    private void LoadPlayerData()
    {
        _firstNameTextBox.Text = _player.FirstName;
        _lastNameTextBox.Text = _player.LastName;
        _relationshipComboBox.SelectedItem = _player.Relationship;
    }
    
    private void SaveButton_Click(object? sender, EventArgs e)
    {
        _player.Relationship = (PlayerRelationship)_relationshipComboBox.SelectedItem!;
    }
}
