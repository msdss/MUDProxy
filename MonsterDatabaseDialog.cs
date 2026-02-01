namespace MudProxyViewer;

public class MonsterDatabaseDialog : Form
{
    private readonly MonsterDatabaseManager _databaseManager;
    
    private TextBox _searchTextBox = null!;
    private ListView _monsterListView = null!;
    private Label _statusLabel = null!;
    private Button _loadCsvButton = null!;
    private Button _editButton = null!;
    
    private string _sortColumn = "Name";
    private bool _sortAscending = true;
    
    public MonsterDatabaseDialog(MonsterDatabaseManager databaseManager)
    {
        _databaseManager = databaseManager;
        InitializeComponent();
        RefreshMonsterList();
    }
    
    private void InitializeComponent()
    {
        this.Text = "Monster Database";
        this.Size = new Size(750, 550);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.StartPosition = FormStartPosition.CenterParent;
        this.MaximizeBox = false;
        this.BackColor = Color.FromArgb(45, 45, 45);
        
        // Top panel with search and load button
        var topPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 45,
            Padding = new Padding(10, 5, 10, 5)
        };
        
        var searchLabel = new Label
        {
            Text = "Search:",
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(10, 14)
        };
        
        _searchTextBox = new TextBox
        {
            Location = new Point(70, 10),
            Width = 250,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        _searchTextBox.TextChanged += SearchTextBox_TextChanged;
        
        _statusLabel = new Label
        {
            Text = "No data loaded",
            ForeColor = Color.Gray,
            AutoSize = true,
            Location = new Point(340, 14)
        };
        
        _loadCsvButton = new Button
        {
            Text = "Load CSV...",
            Width = 100,
            Height = 28,
            Location = new Point(630, 8),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _loadCsvButton.Click += LoadCsvButton_Click;
        
        topPanel.Controls.AddRange(new Control[] { searchLabel, _searchTextBox, _statusLabel, _loadCsvButton });
        
        // Monster list
        _monsterListView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            BackColor = Color.FromArgb(35, 35, 35),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.None
        };
        
        _monsterListView.Columns.Add("#", 50);
        _monsterListView.Columns.Add("Name", 180);
        _monsterListView.Columns.Add("HP", 60);
        _monsterListView.Columns.Add("AC", 45);
        _monsterListView.Columns.Add("DR", 45);
        _monsterListView.Columns.Add("MR", 45);
        _monsterListView.Columns.Add("EXP", 80);
        _monsterListView.Columns.Add("Avg Dmg", 70);
        _monsterListView.Columns.Add("Relationship", 90);
        
        _monsterListView.ColumnClick += MonsterListView_ColumnClick;
        _monsterListView.DoubleClick += MonsterListView_DoubleClick;
        _monsterListView.SelectedIndexChanged += MonsterListView_SelectedIndexChanged;
        
        // Bottom panel
        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            Padding = new Padding(10)
        };
        
        var addButton = new Button
        {
            Text = "Add",
            Width = 80,
            Height = 30,
            Location = new Point(10, 10),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        addButton.Click += AddButton_Click;
        
        _editButton = new Button
        {
            Text = "Edit",
            Width = 80,
            Height = 30,
            Location = new Point(100, 10),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Enabled = false
        };
        _editButton.Click += EditButton_Click;
        
        var pathLabel = new Label
        {
            Text = GetPathDisplayText(),
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 8),
            AutoSize = true,
            Location = new Point(190, 15),
            MaximumSize = new Size(400, 0)
        };
        
        var closeButton = new Button
        {
            Text = "Close",
            Width = 80,
            Height = 30,
            Location = new Point(650, 10),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        closeButton.Click += (s, e) => this.Close();
        
        bottomPanel.Controls.AddRange(new Control[] { addButton, _editButton, pathLabel, closeButton });
        
        // Add controls
        this.Controls.Add(_monsterListView);
        this.Controls.Add(topPanel);
        this.Controls.Add(bottomPanel);
    }
    
    private string GetPathDisplayText()
    {
        if (string.IsNullOrEmpty(_databaseManager.CsvFilePath))
            return "No CSV loaded";
        return $"CSV: {_databaseManager.CsvFilePath}";
    }
    
    private void RefreshMonsterList()
    {
        _monsterListView.Items.Clear();
        
        if (!_databaseManager.IsLoaded)
        {
            _statusLabel.Text = "No data loaded";
            return;
        }
        
        var searchTerm = _searchTextBox.Text;
        var monsters = _databaseManager.SearchMonsters(searchTerm);
        
        // Apply sorting
        monsters = ApplySorting(monsters);
        
        foreach (var monster in monsters)
        {
            var monsterOverride = _databaseManager.GetOverride(monster.Number);
            
            var item = new ListViewItem(monster.Number.ToString());
            item.SubItems.Add(monster.Name);
            item.SubItems.Add(monster.HP.ToString());
            item.SubItems.Add(monster.ArmourClass.ToString());
            item.SubItems.Add(monster.DamageResist.ToString());
            item.SubItems.Add(monster.MagicRes.ToString());
            item.SubItems.Add(monster.EXP.ToString("N0"));
            item.SubItems.Add(monster.AvgDmg.ToString("F1"));
            item.SubItems.Add(monsterOverride.Relationship.ToString());
            item.Tag = monster;
            
            _monsterListView.Items.Add(item);
        }
        
        _statusLabel.Text = $"{_monsterListView.Items.Count} monster(s)";
    }
    
    private IEnumerable<MonsterData> ApplySorting(IEnumerable<MonsterData> monsters)
    {
        return _sortColumn switch
        {
            "Number" => _sortAscending 
                ? monsters.OrderBy(m => m.Number)
                : monsters.OrderByDescending(m => m.Number),
            "Name" => _sortAscending
                ? monsters.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                : monsters.OrderByDescending(m => m.Name, StringComparer.OrdinalIgnoreCase),
            "HP" => _sortAscending
                ? monsters.OrderBy(m => m.HP)
                : monsters.OrderByDescending(m => m.HP),
            "AC" => _sortAscending
                ? monsters.OrderBy(m => m.ArmourClass)
                : monsters.OrderByDescending(m => m.ArmourClass),
            "DR" => _sortAscending
                ? monsters.OrderBy(m => m.DamageResist)
                : monsters.OrderByDescending(m => m.DamageResist),
            "MR" => _sortAscending
                ? monsters.OrderBy(m => m.MagicRes)
                : monsters.OrderByDescending(m => m.MagicRes),
            "EXP" => _sortAscending
                ? monsters.OrderBy(m => m.EXP)
                : monsters.OrderByDescending(m => m.EXP),
            "AvgDmg" => _sortAscending
                ? monsters.OrderBy(m => m.AvgDmg)
                : monsters.OrderByDescending(m => m.AvgDmg),
            "Relationship" => _sortAscending
                ? monsters.OrderBy(m => _databaseManager.GetOverride(m.Number).Relationship)
                : monsters.OrderByDescending(m => _databaseManager.GetOverride(m.Number).Relationship),
            _ => monsters.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
        };
    }
    
    private void MonsterListView_ColumnClick(object? sender, ColumnClickEventArgs e)
    {
        var columnName = e.Column switch
        {
            0 => "Number",
            1 => "Name",
            2 => "HP",
            3 => "AC",
            4 => "DR",
            5 => "MR",
            6 => "EXP",
            7 => "AvgDmg",
            8 => "Relationship",
            _ => "Name"
        };
        
        if (_sortColumn == columnName)
        {
            _sortAscending = !_sortAscending;
        }
        else
        {
            _sortColumn = columnName;
            _sortAscending = true;
        }
        
        RefreshMonsterList();
    }
    
    private void MonsterListView_SelectedIndexChanged(object? sender, EventArgs e)
    {
        _editButton.Enabled = _monsterListView.SelectedItems.Count > 0;
    }
    
    private void MonsterListView_DoubleClick(object? sender, EventArgs e)
    {
        EditSelectedMonster();
    }
    
    private void EditButton_Click(object? sender, EventArgs e)
    {
        EditSelectedMonster();
    }
    
    private void AddButton_Click(object? sender, EventArgs e)
    {
        // Create a new empty monster and override
        var newMonster = new MonsterData();
        var newOverride = new MonsterOverride();
        
        using var dialog = new MonsterEditDialog(newMonster, newOverride, isNew: true);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            // Get the name from the dialog
            if (!string.IsNullOrWhiteSpace(dialog.MonsterName))
            {
                // Add custom monster with next available ID
                _databaseManager.AddCustomMonster(dialog.MonsterName, newOverride);
                RefreshMonsterList();
            }
            else
            {
                MessageBox.Show("Monster name is required.", "Add Monster", 
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
    
    private void EditSelectedMonster()
    {
        if (_monsterListView.SelectedItems.Count == 0) return;
        
        var monster = _monsterListView.SelectedItems[0].Tag as MonsterData;
        if (monster == null) return;
        
        var monsterOverride = _databaseManager.GetOverride(monster.Number);
        
        using var dialog = new MonsterEditDialog(monster, monsterOverride, isNew: false);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _databaseManager.SaveOverride(monsterOverride);
            RefreshMonsterList();
        }
    }
    
    private void SearchTextBox_TextChanged(object? sender, EventArgs e)
    {
        RefreshMonsterList();
    }
    
    private void LoadCsvButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select Monster CSV File",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = "Monsters.csv"
        };
        
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            if (_databaseManager.LoadFromCsv(dialog.FileName))
            {
                RefreshMonsterList();
                
                // Update path label
                foreach (Control c in this.Controls)
                {
                    if (c is Panel panel && panel.Dock == DockStyle.Bottom)
                    {
                        foreach (Control pc in panel.Controls)
                        {
                            if (pc is Label label && label.Location.X == 100)
                            {
                                label.Text = GetPathDisplayText();
                                break;
                            }
                        }
                    }
                }
                
                MessageBox.Show($"Loaded {_databaseManager.MonsterCount} monsters.", 
                    "Load Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("Failed to load monster CSV. Check the log for details.",
                    "Load Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}

public class MonsterEditDialog : Form
{
    private readonly MonsterData _monster;
    private readonly MonsterOverride _override;
    private readonly bool _isNew;
    
    private TextBox _nameText = null!;
    private ComboBox _relationshipCombo = null!;
    private TextBox _preAttackSpellText = null!;
    private TextBox _preAttackMaxText = null!;
    private TextBox _attackSpellText = null!;
    private TextBox _attackMaxText = null!;
    private RadioButton _priorityFirst = null!;
    private RadioButton _priorityHigh = null!;
    private RadioButton _priorityNormal = null!;
    private RadioButton _priorityLow = null!;
    private RadioButton _priorityLast = null!;
    private CheckBox _notHostileCheck = null!;
    
    public string MonsterName => _nameText.Text.Trim();
    
    public MonsterEditDialog(MonsterData monster, MonsterOverride monsterOverride, bool isNew = false)
    {
        _monster = monster;
        _override = monsterOverride;
        _isNew = isNew;
        InitializeComponent();
        LoadData();
    }
    
    private static string GetAlignmentText(int align)
    {
        return align switch
        {
            0 => "Good",
            1 => "Evil",
            2 => "Chaotic Evil",
            3 => "Neutral",
            4 => "Lawful Good",
            5 => "Neutral Evil",
            6 => "Lawful Evil",
            _ => $"Unknown ({align})"
        };
    }
    
    private static string GetTypeText(int type)
    {
        return type switch
        {
            0 => "Solo",
            1 => "Leader",
            2 => "Follower",
            3 => "Stationary",
            _ => $"Unknown ({type})"
        };
    }
    
    private void InitializeComponent()
    {
        this.Text = _isNew ? "Add Monster" : $"Edit Monster: {_monster.Name}";
        this.Size = new Size(580, 420);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.StartPosition = FormStartPosition.CenterParent;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.BackColor = Color.FromArgb(45, 45, 45);
        
        // Left panel - Settings
        var leftPanel = new Panel
        {
            Location = new Point(10, 10),
            Size = new Size(250, 320),
            BackColor = Color.FromArgb(40, 40, 40)
        };
        
        int y = 10;
        int labelX = 10;
        int controlX = 50;  // Moved closer to Name: label
        
        // Name (editable textbox) - wider, starts right after "Name:"
        AddLabel(leftPanel, "Name:", labelX, y + 2);
        _nameText = new TextBox
        {
            Location = new Point(controlX, y),
            Width = 190,  // Wider to fill available space
            MaxLength = 50,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        leftPanel.Controls.Add(_nameText);
        y += 25;
        
        // Separator
        AddLabel(leftPanel, "── Settings ──", labelX, y);
        y += 20;
        
        // Reset controlX for other fields
        int fieldControlX = 105;
        
        // Relationship
        AddLabel(leftPanel, "Relationship:", labelX, y + 2);
        _relationshipCombo = new ComboBox
        {
            Location = new Point(fieldControlX, y),
            Width = 75,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        foreach (var rel in Enum.GetValues<MonsterRelationship>())
            _relationshipCombo.Items.Add(rel);
        leftPanel.Controls.Add(_relationshipCombo);
        y += 26;
        
        // Pre-Attack Spell
        AddLabel(leftPanel, "Pre-Atk Spell:", labelX, y + 2);
        _preAttackSpellText = new TextBox
        {
            Location = new Point(fieldControlX, y),
            Width = 45,
            MaxLength = 4,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        leftPanel.Controls.Add(_preAttackSpellText);
        
        AddLabel(leftPanel, "Max:", fieldControlX + 55, y + 2);
        _preAttackMaxText = new TextBox
        {
            Location = new Point(fieldControlX + 90, y),  // Moved further right
            Width = 30,
            MaxLength = 2,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        leftPanel.Controls.Add(_preAttackMaxText);
        y += 26;
        
        // Attack Spell
        AddLabel(leftPanel, "Attack Spell:", labelX, y + 2);
        _attackSpellText = new TextBox
        {
            Location = new Point(fieldControlX, y),
            Width = 45,
            MaxLength = 4,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        leftPanel.Controls.Add(_attackSpellText);
        
        AddLabel(leftPanel, "Max:", fieldControlX + 55, y + 2);
        _attackMaxText = new TextBox
        {
            Location = new Point(fieldControlX + 90, y),  // Moved further right
            Width = 30,
            MaxLength = 2,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        leftPanel.Controls.Add(_attackMaxText);
        y += 30;
        
        // Attack Priority (vertical) and Options side by side
        AddLabel(leftPanel, "Attack Priority:", labelX, y);
        AddLabel(leftPanel, "Options:", labelX + 130, y);
        y += 20;
        
        _priorityFirst = new RadioButton { Text = "First", ForeColor = Color.White, Location = new Point(labelX + 5, y), AutoSize = true };
        _notHostileCheck = new CheckBox { Text = "Not Hostile", ForeColor = Color.White, Location = new Point(labelX + 135, y), AutoSize = true };
        leftPanel.Controls.Add(_priorityFirst);
        leftPanel.Controls.Add(_notHostileCheck);
        y += 20;
        
        _priorityHigh = new RadioButton { Text = "High", ForeColor = Color.White, Location = new Point(labelX + 5, y), AutoSize = true };
        leftPanel.Controls.Add(_priorityHigh);
        y += 20;
        
        _priorityNormal = new RadioButton { Text = "Normal", ForeColor = Color.White, Location = new Point(labelX + 5, y), AutoSize = true };
        leftPanel.Controls.Add(_priorityNormal);
        y += 20;
        
        _priorityLow = new RadioButton { Text = "Low", ForeColor = Color.White, Location = new Point(labelX + 5, y), AutoSize = true };
        leftPanel.Controls.Add(_priorityLow);
        y += 20;
        
        _priorityLast = new RadioButton { Text = "Last", ForeColor = Color.White, Location = new Point(labelX + 5, y), AutoSize = true };
        leftPanel.Controls.Add(_priorityLast);
        
        this.Controls.Add(leftPanel);
        
        // Right panel - Monster Info (only show if not a new monster)
        var rightPanel = new Panel
        {
            Location = new Point(270, 10),
            Size = new Size(290, 320),
            BackColor = Color.FromArgb(40, 40, 40),
            AutoScroll = true
        };
        
        var infoY = 10;
        AddLabel(rightPanel, "── Monster Info ──", 10, infoY);
        infoY += 25;
        
        if (!_isNew && _monster.Number > 0)
        {
            AddInfoRow(rightPanel, "ID #:", _monster.Number.ToString(), ref infoY);
            AddInfoRow(rightPanel, "HP:", _monster.HP.ToString(), ref infoY);
            AddInfoRow(rightPanel, "AC:", $"{_monster.ArmourClass}/{_monster.DamageResist}", ref infoY);
            AddInfoRow(rightPanel, "Magic Res:", _monster.MagicRes.ToString(), ref infoY);
            AddInfoRow(rightPanel, "EXP:", _monster.EXP.ToString("N0"), ref infoY);
            AddInfoRow(rightPanel, "Avg Dmg:", _monster.AvgDmg.ToString("F1"), ref infoY);
            AddInfoRow(rightPanel, "Energy:", _monster.Energy.ToString(), ref infoY);
            AddInfoRow(rightPanel, "Enslave Lvl:", _monster.CharmLVL.ToString(), ref infoY);
            AddInfoRow(rightPanel, "Undead:", _monster.Undead ? "Yes" : "No", ref infoY);
            AddInfoRow(rightPanel, "Alignment:", GetAlignmentText(_monster.Align), ref infoY);
            AddInfoRow(rightPanel, "Type:", GetTypeText(_monster.Type), ref infoY);
            AddInfoRow(rightPanel, "BS Defense:", _monster.BSDefense.ToString(), ref infoY);
            AddInfoRow(rightPanel, "HP Regen:", _monster.HPRegen.ToString(), ref infoY);
            AddInfoRow(rightPanel, "Follow %:", _monster.FollowPercent.ToString(), ref infoY);
            AddInfoRow(rightPanel, "Weapon ID:", _monster.Weapon.ToString(), ref infoY);
            
            // Attacks
            if (_monster.Attacks.Count > 0)
            {
                infoY += 8;
                AddLabel(rightPanel, "Attacks:", 10, infoY);
                infoY += 20;
                foreach (var attack in _monster.Attacks)
                {
                    var attackText = $"  • {attack.Name} ({attack.Min}-{attack.Max})";
                    AddLabel(rightPanel, attackText, 10, infoY);
                    infoY += 18;
                }
            }
            
            // Drops
            if (_monster.Drops.Count > 0)
            {
                infoY += 8;
                AddLabel(rightPanel, "Drops:", 10, infoY);
                infoY += 20;
                foreach (var drop in _monster.Drops)
                {
                    var dropText = $"  • Item #{drop.ItemId} ({drop.DropPercent}%)";
                    AddLabel(rightPanel, dropText, 10, infoY);
                    infoY += 18;
                }
            }
        }
        else
        {
            // New monster - show placeholder
            AddLabel(rightPanel, "(No CSV data for custom monsters)", 10, infoY);
        }
        
        this.Controls.Add(rightPanel);
        
        // Buttons at bottom
        var saveButton = new Button
        {
            Text = "Save",
            DialogResult = DialogResult.OK,
            Width = 80,
            Height = 30,
            Location = new Point(400, 340),
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
            Location = new Point(485, 340),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        
        this.Controls.AddRange(new Control[] { saveButton, cancelButton });
        this.AcceptButton = saveButton;
        this.CancelButton = cancelButton;
    }
    
    private void AddLabel(Panel panel, string text, int x, int y)
    {
        var label = new Label
        {
            Text = text,
            ForeColor = Color.White,
            Location = new Point(x, y),
            AutoSize = true
        };
        panel.Controls.Add(label);
    }
    
    private void AddInfoRow(Panel panel, string label, string value, ref int y)
    {
        var labelCtrl = new Label
        {
            Text = label,
            ForeColor = Color.Gray,
            Location = new Point(10, y),
            AutoSize = true
        };
        var valueCtrl = new Label
        {
            Text = value,
            ForeColor = Color.White,
            Location = new Point(90, y),
            AutoSize = true
        };
        panel.Controls.Add(labelCtrl);
        panel.Controls.Add(valueCtrl);
        y += 18;
    }
    
    private void LoadData()
    {
        if (_isNew)
        {
            _nameText.Text = _override.CustomName;
        }
        else
        {
            _nameText.Text = !string.IsNullOrEmpty(_override.CustomName) ? _override.CustomName : _monster.Name;
        }
        
        _relationshipCombo.SelectedItem = _override.Relationship;
        _preAttackSpellText.Text = _override.PreAttackSpell;
        _preAttackMaxText.Text = _override.PreAttackSpellMax > 0 ? _override.PreAttackSpellMax.ToString() : "";
        _attackSpellText.Text = _override.AttackSpell;
        _attackMaxText.Text = _override.AttackSpellMax > 0 ? _override.AttackSpellMax.ToString() : "";
        _notHostileCheck.Checked = _override.NotHostile;
        
        switch (_override.Priority)
        {
            case AttackPriority.First: _priorityFirst.Checked = true; break;
            case AttackPriority.High: _priorityHigh.Checked = true; break;
            case AttackPriority.Normal: _priorityNormal.Checked = true; break;
            case AttackPriority.Low: _priorityLow.Checked = true; break;
            case AttackPriority.Last: _priorityLast.Checked = true; break;
        }
        
        // Don't auto-select the name field - move focus to relationship dropdown
        if (!_isNew)
        {
            _nameText.SelectionStart = 0;
            _nameText.SelectionLength = 0;
            _relationshipCombo.Focus();
        }
    }
    
    private void SaveButton_Click(object? sender, EventArgs e)
    {
        if (_isNew)
        {
            _override.CustomName = _nameText.Text.Trim();
        }
        else
        {
            // Only save custom name if different from CSV name
            var newName = _nameText.Text.Trim();
            _override.CustomName = newName != _monster.Name ? newName : string.Empty;
        }
        
        _override.Relationship = (MonsterRelationship)_relationshipCombo.SelectedItem!;
        _override.PreAttackSpell = _preAttackSpellText.Text.Trim();
        _override.PreAttackSpellMax = int.TryParse(_preAttackMaxText.Text, out int preMax) ? Math.Min(preMax, 99) : 0;
        _override.AttackSpell = _attackSpellText.Text.Trim();
        _override.AttackSpellMax = int.TryParse(_attackMaxText.Text, out int atkMax) ? Math.Min(atkMax, 99) : 0;
        _override.NotHostile = _notHostileCheck.Checked;
        
        if (_priorityFirst.Checked) _override.Priority = AttackPriority.First;
        else if (_priorityHigh.Checked) _override.Priority = AttackPriority.High;
        else if (_priorityNormal.Checked) _override.Priority = AttackPriority.Normal;
        else if (_priorityLow.Checked) _override.Priority = AttackPriority.Low;
        else if (_priorityLast.Checked) _override.Priority = AttackPriority.Last;
    }
}
