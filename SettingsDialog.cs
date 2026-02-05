namespace MudProxyViewer;

public class SettingsDialog : Form
{
    private readonly BuffManager _buffManager;
    private readonly CombatManager _combatManager;
    private readonly string _currentCharacter;
    private TabControl _tabControl = null!;
    
    // General tab controls
    private ListBox _priorityListBox = null!;
    private NumericUpDown _manaReserveNumeric = null!;
    
    // Party tab controls
    private CheckBox _parAutoCheckBox = null!;
    private NumericUpDown _parFrequencyNumeric = null!;
    private CheckBox _parAfterTickCheckBox = null!;
    private CheckBox _healthRequestCheckBox = null!;
    private NumericUpDown _healthRequestIntervalNumeric = null!;
    
    // Combat tab controls
    private TextBox _attackCommandText = null!;
    private TextBox _backstabWeaponText = null!;
    private TextBox _normalWeaponText = null!;
    private TextBox _alternateWeaponText = null!;
    private TextBox _shieldText = null!;
    private CheckBox _useShieldWithBSCheck = null!;
    private CheckBox _useNormalForAtkSpellsCheck = null!;
    private TextBox _multiAttackSpellText = null!;
    private NumericUpDown _multiAttackMinEnemiesNum = null!;
    private NumericUpDown _multiAttackMaxCastNum = null!;
    private NumericUpDown _multiAttackReqManaNum = null!;
    private Label _multiAttackManaLabel = null!;
    private TextBox _preAttackSpellText = null!;
    private NumericUpDown _preAttackMaxCastNum = null!;
    private NumericUpDown _preAttackReqManaNum = null!;
    private Label _preAttackManaLabel = null!;
    private TextBox _attackSpellText = null!;
    private NumericUpDown _attackMaxCastNum = null!;
    private NumericUpDown _attackReqManaNum = null!;
    private Label _attackManaLabel = null!;
    private CheckBox _doBackstabCheck = null!;
    private NumericUpDown _maxMonstersNum = null!;
    private NumericUpDown _runDistanceNum = null!;
    
    // BBS tab controls
    private TextBox _bbsAddressText = null!;
    private NumericUpDown _bbsPortNum = null!;
    private ListView _logonSequencesList = null!;
    private TextBox _logoffCommandText = null!;
    private TextBox _relogCommandText = null!;
    private NumericUpDown _pvpLevelNum = null!;
    
    public SettingsDialog(BuffManager buffManager, CombatManager combatManager, string currentCharacter = "")
    {
        _buffManager = buffManager;
        _combatManager = combatManager;
        _currentCharacter = currentCharacter;
        InitializeComponent();
    }
    
    private void InitializeComponent()
    {
        this.Text = "Settings";
        this.Size = new Size(600, 500);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.StartPosition = FormStartPosition.CenterParent;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.BackColor = Color.FromArgb(45, 45, 45);
        
        // Tab control
        _tabControl = new TabControl
        {
            Location = new Point(10, 10),
            Size = new Size(565, 400),
            Font = new Font("Segoe UI", 9)
        };
        
        // Create tabs
        var generalTab = CreateGeneralTab();
        var combatTab = CreateCombatTab();
        var healingTab = CreateHealingTab();
        var curesTab = CreateCuresTab();
        var buffsTab = CreateBuffsTab();
        var partyTab = CreatePartyTab();
        var bbsTab = CreateBbsTab();
        
        _tabControl.TabPages.AddRange(new TabPage[] { generalTab, combatTab, healingTab, curesTab, buffsTab, partyTab, bbsTab });
        this.Controls.Add(_tabControl);
        
        // Save button
        var saveButton = new Button
        {
            Text = "Save",
            Size = new Size(80, 30),
            Location = new Point(405, 420),
            BackColor = Color.FromArgb(0, 100, 0),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.OK
        };
        saveButton.Click += SaveButton_Click;
        this.Controls.Add(saveButton);
        this.AcceptButton = saveButton;
        
        // Cancel button
        var cancelButton = new Button
        {
            Text = "Cancel",
            Size = new Size(80, 30),
            Location = new Point(495, 420),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.Cancel
        };
        this.Controls.Add(cancelButton);
        this.CancelButton = cancelButton;
    }
    
    private void SaveButton_Click(object? sender, EventArgs e)
    {
        // Save all settings to BuffManager
        SaveCombatSettings();
        SaveBbsSettings();
        
        // Save to character profile file
        var profilePath = _buffManager.CurrentProfilePath;
        if (string.IsNullOrEmpty(profilePath))
        {
            // No profile loaded - prompt user to save
            using var dialog = new SaveFileDialog
            {
                Filter = "Character Profile (*.json)|*.json",
                DefaultExt = "json",
                FileName = _buffManager.GetDefaultProfileFilename(),
                InitialDirectory = _buffManager.CharacterProfilesPath
            };
            
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                profilePath = dialog.FileName;
            }
            else
            {
                return; // User cancelled, don't close dialog
            }
        }
        
        var (success, message) = _buffManager.SaveCharacterProfile(profilePath);
        if (!success)
        {
            MessageBox.Show(message, "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    
    private TabPage CreateCombatTab()
    {
        var tab = new TabPage("Combat")
        {
            BackColor = Color.FromArgb(45, 45, 45)
        };
        
        var settings = _combatManager.GetCurrentSettings();
        
        // ── Weapon Combat Panel ──
        var weaponPanel = new Panel
        {
            Location = new Point(10, 10),
            Size = new Size(270, 205),
            BackColor = Color.FromArgb(40, 40, 40)
        };
        
        int wy = 8;
        AddLabel(weaponPanel, "── Weapon Combat ──", 10, wy);
        wy += 22;
        
        AddLabel(weaponPanel, "Attack Command:", 10, wy + 2);
        _attackCommandText = new TextBox
        {
            Location = new Point(120, wy),
            Width = 60,
            MaxLength = 5,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Text = settings.AttackCommand
        };
        weaponPanel.Controls.Add(_attackCommandText);
        wy += 26;
        
        AddLabel(weaponPanel, "Backstab Weapon:", 10, wy + 2);
        _backstabWeaponText = new TextBox
        {
            Location = new Point(120, wy),
            Width = 140,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Text = settings.BackstabWeapon
        };
        weaponPanel.Controls.Add(_backstabWeaponText);
        wy += 26;
        
        AddLabel(weaponPanel, "Normal Weapon:", 10, wy + 2);
        _normalWeaponText = new TextBox
        {
            Location = new Point(120, wy),
            Width = 140,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Text = settings.NormalWeapon
        };
        weaponPanel.Controls.Add(_normalWeaponText);
        wy += 26;
        
        AddLabel(weaponPanel, "Alternate Weapon:", 10, wy + 2);
        _alternateWeaponText = new TextBox
        {
            Location = new Point(120, wy),
            Width = 140,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Text = settings.AlternateWeapon
        };
        weaponPanel.Controls.Add(_alternateWeaponText);
        wy += 26;
        
        AddLabel(weaponPanel, "Shield:", 10, wy + 2);
        _shieldText = new TextBox
        {
            Location = new Point(120, wy),
            Width = 140,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Text = settings.Shield
        };
        weaponPanel.Controls.Add(_shieldText);
        wy += 28;
        
        _useShieldWithBSCheck = new CheckBox
        {
            Text = "Use shield with BS weapon",
            ForeColor = Color.White,
            Location = new Point(10, wy),
            AutoSize = true,
            Checked = settings.UseShieldWithBSWeapon
        };
        weaponPanel.Controls.Add(_useShieldWithBSCheck);
        wy += 22;
        
        _useNormalForAtkSpellsCheck = new CheckBox
        {
            Text = "Use normal weapon for atk spells",
            ForeColor = Color.White,
            Location = new Point(10, wy),
            AutoSize = true,
            Checked = settings.UseNormalWeaponForAtkSpells
        };
        weaponPanel.Controls.Add(_useNormalForAtkSpellsCheck);
        
        tab.Controls.Add(weaponPanel);
        
        // ── Options Panel ──
        var optionsPanel = new Panel
        {
            Location = new Point(290, 10),
            Size = new Size(260, 205),
            BackColor = Color.FromArgb(40, 40, 40)
        };
        
        int oy = 8;
        AddLabel(optionsPanel, "── Options ──", 10, oy);
        oy += 22;
        
        _doBackstabCheck = new CheckBox
        {
            Text = "Do BS Attack",
            ForeColor = Color.White,
            Location = new Point(10, oy),
            AutoSize = true,
            Checked = settings.DoBackstabAttack
        };
        optionsPanel.Controls.Add(_doBackstabCheck);
        oy += 30;
        
        AddLabel(optionsPanel, "Max Monsters:", 10, oy + 2);
        _maxMonstersNum = new NumericUpDown
        {
            Location = new Point(110, oy),
            Width = 50,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Minimum = 0,
            Maximum = 99,
            Value = settings.MaxMonsters
        };
        optionsPanel.Controls.Add(_maxMonstersNum);
        oy += 30;
        
        AddLabel(optionsPanel, "Run Distance:", 10, oy + 2);
        _runDistanceNum = new NumericUpDown
        {
            Location = new Point(110, oy),
            Width = 50,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Minimum = 0,
            Maximum = 20,
            Value = settings.RunDistance
        };
        optionsPanel.Controls.Add(_runDistanceNum);
        
        tab.Controls.Add(optionsPanel);
        
        // ── Spell Combat Panel ──
        var spellPanel = new Panel
        {
            Location = new Point(10, 225),
            Size = new Size(540, 130),
            BackColor = Color.FromArgb(40, 40, 40)
        };
        
        int sy = 8;
        AddLabel(spellPanel, "── Spell Combat ──", 10, sy);
        sy += 22;
        
        // Header row
        AddLabel(spellPanel, "Spell", 115, sy);
        AddLabel(spellPanel, "Min Enemies", 175, sy);
        AddLabel(spellPanel, "Max Cast", 275, sy);
        AddLabel(spellPanel, "Req Mana %", 365, sy);
        sy += 20;
        
        int maxMana = _buffManager.MaxMana;
        
        // Multi-Attack row
        AddLabel(spellPanel, "Multi-Attack:", 10, sy + 2);
        _multiAttackSpellText = new TextBox
        {
            Location = new Point(100, sy),
            Width = 50,
            MaxLength = 4,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Text = settings.MultiAttackSpell
        };
        spellPanel.Controls.Add(_multiAttackSpellText);
        
        _multiAttackMinEnemiesNum = new NumericUpDown
        {
            Location = new Point(185, sy),
            Width = 50,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Minimum = 1,
            Maximum = 20,
            Value = settings.MultiAttackMinEnemies
        };
        spellPanel.Controls.Add(_multiAttackMinEnemiesNum);
        
        _multiAttackMaxCastNum = new NumericUpDown
        {
            Location = new Point(280, sy),
            Width = 50,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Minimum = 1,
            Maximum = 99,
            Value = settings.MultiAttackMaxCast
        };
        spellPanel.Controls.Add(_multiAttackMaxCastNum);
        
        _multiAttackReqManaNum = new NumericUpDown
        {
            Location = new Point(375, sy),
            Width = 50,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Minimum = 0,
            Maximum = 100,
            Value = settings.MultiAttackReqManaPercent
        };
        _multiAttackReqManaNum.ValueChanged += (s, e) => UpdateManaLabels();
        _multiAttackReqManaNum.TextChanged += (s, e) => UpdateManaLabels();
        spellPanel.Controls.Add(_multiAttackReqManaNum);
        
        _multiAttackManaLabel = new Label
        {
            Text = $"({settings.MultiAttackReqManaPercent * maxMana / 100}/{maxMana})",
            ForeColor = Color.Gray,
            Location = new Point(430, sy + 2),
            AutoSize = true
        };
        spellPanel.Controls.Add(_multiAttackManaLabel);
        sy += 28;
        
        // Pre-Attack row
        AddLabel(spellPanel, "Pre-Attack:", 10, sy + 2);
        _preAttackSpellText = new TextBox
        {
            Location = new Point(100, sy),
            Width = 50,
            MaxLength = 4,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Text = settings.PreAttackSpell
        };
        spellPanel.Controls.Add(_preAttackSpellText);
        
        AddLabel(spellPanel, "n/a", 200, sy + 2);
        
        _preAttackMaxCastNum = new NumericUpDown
        {
            Location = new Point(280, sy),
            Width = 50,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Minimum = 1,
            Maximum = 99,
            Value = settings.PreAttackMaxCast
        };
        spellPanel.Controls.Add(_preAttackMaxCastNum);
        
        _preAttackReqManaNum = new NumericUpDown
        {
            Location = new Point(375, sy),
            Width = 50,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Minimum = 0,
            Maximum = 100,
            Value = settings.PreAttackReqManaPercent
        };
        _preAttackReqManaNum.ValueChanged += (s, e) => UpdateManaLabels();
        _preAttackReqManaNum.TextChanged += (s, e) => UpdateManaLabels();
        spellPanel.Controls.Add(_preAttackReqManaNum);
        
        _preAttackManaLabel = new Label
        {
            Text = $"({settings.PreAttackReqManaPercent * maxMana / 100}/{maxMana})",
            ForeColor = Color.Gray,
            Location = new Point(430, sy + 2),
            AutoSize = true
        };
        spellPanel.Controls.Add(_preAttackManaLabel);
        sy += 28;
        
        // Attack Spell row
        AddLabel(spellPanel, "Attack Spell:", 10, sy + 2);
        _attackSpellText = new TextBox
        {
            Location = new Point(100, sy),
            Width = 50,
            MaxLength = 4,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Text = settings.AttackSpell
        };
        spellPanel.Controls.Add(_attackSpellText);
        
        AddLabel(spellPanel, "n/a", 200, sy + 2);
        
        _attackMaxCastNum = new NumericUpDown
        {
            Location = new Point(280, sy),
            Width = 50,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Minimum = 1,
            Maximum = 99,
            Value = settings.AttackMaxCast
        };
        spellPanel.Controls.Add(_attackMaxCastNum);
        
        _attackReqManaNum = new NumericUpDown
        {
            Location = new Point(375, sy),
            Width = 50,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Minimum = 0,
            Maximum = 100,
            Value = settings.AttackReqManaPercent
        };
        _attackReqManaNum.ValueChanged += (s, e) => UpdateManaLabels();
        _attackReqManaNum.TextChanged += (s, e) => UpdateManaLabels();
        spellPanel.Controls.Add(_attackReqManaNum);
        
        _attackManaLabel = new Label
        {
            Text = $"({settings.AttackReqManaPercent * maxMana / 100}/{maxMana})",
            ForeColor = Color.Gray,
            Location = new Point(430, sy + 2),
            AutoSize = true
        };
        spellPanel.Controls.Add(_attackManaLabel);
        
        tab.Controls.Add(spellPanel);
        
        return tab;
    }
    
    private void UpdateManaLabels()
    {
        int maxMana = _buffManager.MaxMana;
        _multiAttackManaLabel.Text = $"({(int)_multiAttackReqManaNum.Value * maxMana / 100}/{maxMana})";
        _preAttackManaLabel.Text = $"({(int)_preAttackReqManaNum.Value * maxMana / 100}/{maxMana})";
        _attackManaLabel.Text = $"({(int)_attackReqManaNum.Value * maxMana / 100}/{maxMana})";
    }
    
    private void SaveCombatSettings()
    {
        if (string.IsNullOrEmpty(_currentCharacter))
            return;
        
        var settings = new CombatSettings
        {
            CharacterName = _currentCharacter,
            
            // Weapon Combat
            AttackCommand = _attackCommandText.Text.Trim(),
            BackstabWeapon = _backstabWeaponText.Text.Trim(),
            NormalWeapon = _normalWeaponText.Text.Trim(),
            AlternateWeapon = _alternateWeaponText.Text.Trim(),
            Shield = _shieldText.Text.Trim(),
            UseShieldWithBSWeapon = _useShieldWithBSCheck.Checked,
            UseNormalWeaponForAtkSpells = _useNormalForAtkSpellsCheck.Checked,
            
            // Spell Combat - Multi-Attack
            MultiAttackSpell = _multiAttackSpellText.Text.Trim(),
            MultiAttackMinEnemies = (int)_multiAttackMinEnemiesNum.Value,
            MultiAttackMaxCast = (int)_multiAttackMaxCastNum.Value,
            MultiAttackReqManaPercent = (int)_multiAttackReqManaNum.Value,
            
            // Spell Combat - Pre-Attack
            PreAttackSpell = _preAttackSpellText.Text.Trim(),
            PreAttackMaxCast = (int)_preAttackMaxCastNum.Value,
            PreAttackReqManaPercent = (int)_preAttackReqManaNum.Value,
            
            // Spell Combat - Attack
            AttackSpell = _attackSpellText.Text.Trim(),
            AttackMaxCast = (int)_attackMaxCastNum.Value,
            AttackReqManaPercent = (int)_attackReqManaNum.Value,
            
            // Options
            DoBackstabAttack = _doBackstabCheck.Checked,
            MaxMonsters = (int)_maxMonstersNum.Value,
            RunDistance = (int)_runDistanceNum.Value
        };
        
        _combatManager.SaveSettings(settings);
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
    
    private TabPage CreateGeneralTab()
    {
        var tab = new TabPage("General")
        {
            BackColor = Color.FromArgb(45, 45, 45)
        };
        
        int y = 20;
        
        // Cast Priority section
        var priorityLabel = new Label
        {
            Text = "Cast Priority (drag to reorder):",
            Location = new Point(20, y),
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        tab.Controls.Add(priorityLabel);
        y += 25;
        
        _priorityListBox = new ListBox
        {
            Location = new Point(20, y),
            Size = new Size(250, 120),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10),
            AllowDrop = true
        };
        
        // Populate priority list
        foreach (var priority in _buffManager.CureManager.PriorityOrder)
        {
            _priorityListBox.Items.Add(GetPriorityDisplayName(priority));
        }
        
        _priorityListBox.MouseDown += PriorityListBox_MouseDown;
        _priorityListBox.DragOver += PriorityListBox_DragOver;
        _priorityListBox.DragDrop += PriorityListBox_DragDrop;
        tab.Controls.Add(_priorityListBox);
        
        // Move up/down buttons
        var moveUpButton = new Button
        {
            Text = "Move Up",
            Location = new Point(280, y),
            Size = new Size(80, 28),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        moveUpButton.Click += (s, e) => MovePriorityItem(-1);
        tab.Controls.Add(moveUpButton);
        
        var moveDownButton = new Button
        {
            Text = "Move Down",
            Location = new Point(280, y + 35),
            Size = new Size(80, 28),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        moveDownButton.Click += (s, e) => MovePriorityItem(1);
        tab.Controls.Add(moveDownButton);
        
        y += 140;
        
        var infoLabel = new Label
        {
            Text = "Cast priority determines the order in which the proxy\n" +
                   "will attempt to cast spells when multiple actions are needed.\n\n" +
                   "Priority Types:\n" +
                   "  Heals - Self and party healing spells\n" +
                   "  Cures - Cure ailments (poison, paralysis, etc.)\n" +
                   "  Buffs - Buff spells with auto-recast enabled",
            Location = new Point(20, y),
            AutoSize = true,
            ForeColor = Color.FromArgb(150, 150, 150),
            Font = new Font("Segoe UI", 9)
        };
        tab.Controls.Add(infoLabel);
        
        return tab;
    }
    
    private TabPage CreateHealingTab()
    {
        var tab = new TabPage("Healing")
        {
            BackColor = Color.FromArgb(45, 45, 45)
        };
        
        int y = 20;
        
        var configureButton = new Button
        {
            Text = "Configure Healing...",
            Location = new Point(20, y),
            Size = new Size(200, 35),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10)
        };
        configureButton.Click += (s, e) =>
        {
            using var dialog = new HealingConfigDialog(_buffManager.HealingManager);
            dialog.ShowDialog(this);
        };
        tab.Controls.Add(configureButton);
        
        y += 50;
        
        var infoLabel = new Label
        {
            Text = "Configure heal spells and healing rules.\n\n" +
                   "Heal Spells: Define the healing spells available to your character.\n\n" +
                   "Self Heal Rules: Set HP thresholds to trigger self-healing.\n\n" +
                   "Party Heal Rules: Set HP thresholds to trigger party member healing.\n\n" +
                   "Party-Wide Heal Rules: Configure group heal conditions.",
            Location = new Point(20, y),
            Size = new Size(500, 150),
            ForeColor = Color.FromArgb(180, 180, 180),
            Font = new Font("Segoe UI", 9)
        };
        tab.Controls.Add(infoLabel);
        
        return tab;
    }
    
    private TabPage CreateCuresTab()
    {
        var tab = new TabPage("Cures")
        {
            BackColor = Color.FromArgb(45, 45, 45)
        };
        
        int y = 20;
        
        var configureButton = new Button
        {
            Text = "Configure Cures...",
            Location = new Point(20, y),
            Size = new Size(200, 35),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10)
        };
        configureButton.Click += (s, e) =>
        {
            using var dialog = new CureConfigDialog(_buffManager.CureManager);
            dialog.ShowDialog(this);
        };
        tab.Controls.Add(configureButton);
        
        y += 50;
        
        var clearAilmentsButton = new Button
        {
            Text = "Clear All Active Ailments",
            Location = new Point(20, y),
            Size = new Size(200, 30),
            BackColor = Color.FromArgb(80, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        clearAilmentsButton.Click += (s, e) =>
        {
            _buffManager.CureManager.ClearAllAilments();
            MessageBox.Show("All active ailments cleared.", "Ailments Cleared", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        tab.Controls.Add(clearAilmentsButton);
        
        y += 50;
        
        var infoLabel = new Label
        {
            Text = "Configure ailments and cure spells.\n\n" +
                   "Ailments: Define conditions like poison, blindness, etc.\n" +
                   "Set detection patterns and telepath request codes.\n\n" +
                   "Cure Spells: Define spells to cure each ailment.\n" +
                   "Set mana costs and priorities.",
            Location = new Point(20, y),
            Size = new Size(500, 120),
            ForeColor = Color.FromArgb(180, 180, 180),
            Font = new Font("Segoe UI", 9)
        };
        tab.Controls.Add(infoLabel);
        
        return tab;
    }
    
    private TabPage CreateBuffsTab()
    {
        var tab = new TabPage("Buffs")
        {
            BackColor = Color.FromArgb(45, 45, 45)
        };
        
        int y = 20;
        
        var configureButton = new Button
        {
            Text = "Manage Buff Configurations...",
            Location = new Point(20, y),
            Size = new Size(220, 35),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10)
        };
        configureButton.Click += (s, e) =>
        {
            using var dialog = new BuffListDialog(_buffManager);
            dialog.ShowDialog(this);
        };
        tab.Controls.Add(configureButton);
        
        y += 50;
        
        // Buff State Settings section
        var stateLabel = new Label
        {
            Text = "Buff Casting Conditions:",
            Location = new Point(20, y),
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        tab.Controls.Add(stateLabel);
        y += 25;
        
        var buffWhileRestingCheckBox = new CheckBox
        {
            Text = "Buff while resting",
            Location = new Point(20, y),
            AutoSize = true,
            ForeColor = Color.White,
            Checked = _buffManager.BuffWhileResting
        };
        buffWhileRestingCheckBox.CheckedChanged += (s, e) => _buffManager.BuffWhileResting = buffWhileRestingCheckBox.Checked;
        tab.Controls.Add(buffWhileRestingCheckBox);
        y += 28;
        
        var buffWhileInCombatCheckBox = new CheckBox
        {
            Text = "Buff while in combat",
            Location = new Point(20, y),
            AutoSize = true,
            ForeColor = Color.White,
            Checked = _buffManager.BuffWhileInCombat
        };
        buffWhileInCombatCheckBox.CheckedChanged += (s, e) => _buffManager.BuffWhileInCombat = buffWhileInCombatCheckBox.Checked;
        tab.Controls.Add(buffWhileInCombatCheckBox);
        y += 40;
        
        // Mana Reserve section
        var manaReserveLabel = new Label
        {
            Text = "Mana Reserve %:",
            Location = new Point(20, y),
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        tab.Controls.Add(manaReserveLabel);
        y += 25;
        
        _manaReserveNumeric = new NumericUpDown
        {
            Location = new Point(20, y),
            Size = new Size(80, 25),
            Minimum = 0,
            Maximum = 100,
            Value = _buffManager.ManaReservePercent,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        _manaReserveNumeric.ValueChanged += (s, e) => _buffManager.ManaReservePercent = (int)_manaReserveNumeric.Value;
        tab.Controls.Add(_manaReserveNumeric);
        
        var percentLabel = new Label
        {
            Text = "%",
            Location = new Point(105, y + 3),
            AutoSize = true,
            ForeColor = Color.White
        };
        tab.Controls.Add(percentLabel);
        
        y += 35;
        
        var manaReserveHint = new Label
        {
            Text = "Buffs will not cast if mana drops below this percentage.\nHeals and cures are not affected by this setting.",
            Location = new Point(20, y),
            AutoSize = true,
            ForeColor = Color.FromArgb(150, 150, 150),
            Font = new Font("Segoe UI", 8)
        };
        tab.Controls.Add(manaReserveHint);
        
        return tab;
    }
    
    private TabPage CreatePartyTab()
    {
        var tab = new TabPage("Party")
        {
            BackColor = Color.FromArgb(45, 45, 45)
        };
        
        int y = 20;
        
        // Par command section
        var parSectionLabel = new Label
        {
            Text = "Party List Updates",
            Location = new Point(20, y),
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        tab.Controls.Add(parSectionLabel);
        y += 25;
        
        _parAutoCheckBox = new CheckBox
        {
            Text = "Auto-send 'par' command",
            Location = new Point(20, y),
            AutoSize = true,
            ForeColor = Color.White,
            Checked = _buffManager.ParAutoEnabled
        };
        tab.Controls.Add(_parAutoCheckBox);
        y += 30;
        
        _parAfterTickCheckBox = new CheckBox
        {
            Text = "Send 'par' after each combat tick (overrides frequency)",
            Location = new Point(40, y),
            AutoSize = true,
            ForeColor = Color.White,
            Checked = _buffManager.ParAfterCombatTick
        };
        tab.Controls.Add(_parAfterTickCheckBox);
        y += 30;
        
        var freqLabel = new Label
        {
            Text = "Frequency:",
            Location = new Point(40, y + 3),
            AutoSize = true,
            ForeColor = Color.White,
            Name = "freqLabel"
        };
        tab.Controls.Add(freqLabel);
        
        _parFrequencyNumeric = new NumericUpDown
        {
            Location = new Point(120, y),
            Size = new Size(60, 25),
            Minimum = 5,
            Maximum = 300,
            Value = _buffManager.ParFrequencySeconds,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Enabled = !_buffManager.ParAfterCombatTick
        };
        _parFrequencyNumeric.ValueChanged += (s, e) => _buffManager.ParFrequencySeconds = (int)_parFrequencyNumeric.Value;
        tab.Controls.Add(_parFrequencyNumeric);
        
        var secondsLabel = new Label
        {
            Text = "seconds",
            Location = new Point(185, y + 3),
            AutoSize = true,
            ForeColor = Color.White,
            Name = "secondsLabel"
        };
        tab.Controls.Add(secondsLabel);
        y += 40;
        
        // Update enabled state based on checkbox
        _parAutoCheckBox.CheckedChanged += (s, e) => 
        {
            _buffManager.ParAutoEnabled = _parAutoCheckBox.Checked;
            UpdateParFrequencyEnabledState(freqLabel, secondsLabel);
        };
        
        _parAfterTickCheckBox.CheckedChanged += (s, e) => 
        {
            _buffManager.ParAfterCombatTick = _parAfterTickCheckBox.Checked;
            UpdateParFrequencyEnabledState(freqLabel, secondsLabel);
        };
        
        // Initial state
        UpdateParFrequencyEnabledState(freqLabel, secondsLabel);
        
        // Health request section
        var healthSectionLabel = new Label
        {
            Text = "Health Data Requests",
            Location = new Point(20, y),
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        tab.Controls.Add(healthSectionLabel);
        y += 25;
        
        _healthRequestCheckBox = new CheckBox
        {
            Text = "Auto-request health data from party members",
            Location = new Point(20, y),
            AutoSize = true,
            ForeColor = Color.White,
            Checked = _buffManager.HealthRequestEnabled
        };
        _healthRequestCheckBox.CheckedChanged += (s, e) => _buffManager.HealthRequestEnabled = _healthRequestCheckBox.Checked;
        tab.Controls.Add(_healthRequestCheckBox);
        y += 30;
        
        var healthFreqLabel = new Label
        {
            Text = "Request interval:",
            Location = new Point(40, y + 3),
            AutoSize = true,
            ForeColor = Color.White
        };
        tab.Controls.Add(healthFreqLabel);
        
        _healthRequestIntervalNumeric = new NumericUpDown
        {
            Location = new Point(145, y),
            Size = new Size(60, 25),
            Minimum = 30,
            Maximum = 300,
            Value = _buffManager.HealthRequestIntervalSeconds,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        _healthRequestIntervalNumeric.ValueChanged += (s, e) => _buffManager.HealthRequestIntervalSeconds = (int)_healthRequestIntervalNumeric.Value;
        tab.Controls.Add(_healthRequestIntervalNumeric);
        
        var healthSecondsLabel = new Label
        {
            Text = "seconds",
            Location = new Point(210, y + 3),
            AutoSize = true,
            ForeColor = Color.White
        };
        tab.Controls.Add(healthSecondsLabel);
        y += 40;
        
        var healthHint = new Label
        {
            Text = "Sends '/{player} @health' to party members who haven't\n" +
                   "provided their actual HP values via telepath.",
            Location = new Point(40, y),
            AutoSize = true,
            ForeColor = Color.FromArgb(150, 150, 150),
            Font = new Font("Segoe UI", 8)
        };
        tab.Controls.Add(healthHint);
        
        return tab;
    }
    
    private string GetPriorityDisplayName(CastPriorityType priority)
    {
        return priority switch
        {
            CastPriorityType.Heals => "Heals",
            CastPriorityType.Cures => "Cures",
            CastPriorityType.Buffs => "Buffs",
            _ => priority.ToString()
        };
    }
    
    private CastPriorityType GetPriorityFromDisplayName(string name)
    {
        return name switch
        {
            "Heals" => CastPriorityType.Heals,
            "Cures" => CastPriorityType.Cures,
            "Buffs" => CastPriorityType.Buffs,
            _ => CastPriorityType.Buffs
        };
    }
    
    private void MovePriorityItem(int direction)
    {
        if (_priorityListBox.SelectedIndex < 0) return;
        
        int index = _priorityListBox.SelectedIndex;
        int newIndex = index + direction;
        
        if (newIndex < 0 || newIndex >= _priorityListBox.Items.Count) return;
        
        var item = _priorityListBox.Items[index];
        _priorityListBox.Items.RemoveAt(index);
        _priorityListBox.Items.Insert(newIndex, item);
        _priorityListBox.SelectedIndex = newIndex;
        
        SavePriorityOrder();
    }
    
    private void PriorityListBox_MouseDown(object? sender, MouseEventArgs e)
    {
        if (_priorityListBox.SelectedItem != null)
        {
            _priorityListBox.DoDragDrop(_priorityListBox.SelectedItem, DragDropEffects.Move);
        }
    }
    
    private void PriorityListBox_DragOver(object? sender, DragEventArgs e)
    {
        e.Effect = DragDropEffects.Move;
    }
    
    private void PriorityListBox_DragDrop(object? sender, DragEventArgs e)
    {
        var point = _priorityListBox.PointToClient(new Point(e.X, e.Y));
        int index = _priorityListBox.IndexFromPoint(point);
        if (index < 0) index = _priorityListBox.Items.Count - 1;
        
        var data = e.Data?.GetData(typeof(string)) as string;
        if (data == null) return;
        
        int oldIndex = _priorityListBox.Items.IndexOf(data);
        if (oldIndex < 0) return;
        
        _priorityListBox.Items.RemoveAt(oldIndex);
        _priorityListBox.Items.Insert(index, data);
        _priorityListBox.SelectedIndex = index;
        
        SavePriorityOrder();
    }
    
    private void SavePriorityOrder()
    {
        var newOrder = new List<CastPriorityType>();
        foreach (var item in _priorityListBox.Items)
        {
            newOrder.Add(GetPriorityFromDisplayName(item.ToString() ?? ""));
        }
        _buffManager.CureManager.PriorityOrder = newOrder;
    }
    
    private void UpdateParFrequencyEnabledState(Label freqLabel, Label secondsLabel)
    {
        // Frequency is disabled when "par after tick" is enabled
        bool enabled = !_parAfterTickCheckBox.Checked;
        _parFrequencyNumeric.Enabled = enabled;
        freqLabel.ForeColor = enabled ? Color.White : Color.FromArgb(100, 100, 100);
        secondsLabel.ForeColor = enabled ? Color.White : Color.FromArgb(100, 100, 100);
    }
    
    #region BBS Tab
    
    private TabPage CreateBbsTab()
    {
        var tab = new TabPage("BBS")
        {
            BackColor = Color.FromArgb(45, 45, 45)
        };
        
        var settings = _buffManager.BbsSettings;
        
        // ── Telnet Section ──
        var telnetPanel = new Panel
        {
            Location = new Point(10, 10),
            Size = new Size(260, 80),
            BackColor = Color.FromArgb(40, 40, 40)
        };
        
        int ty = 8;
        AddLabel(telnetPanel, "── Telnet ──", 10, ty);
        ty += 22;
        
        AddLabel(telnetPanel, "Address:", 10, ty + 2);
        _bbsAddressText = new TextBox
        {
            Location = new Point(70, ty),
            Width = 140,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Text = settings.Address
        };
        telnetPanel.Controls.Add(_bbsAddressText);
        ty += 26;
        
        AddLabel(telnetPanel, "Port:", 10, ty + 2);
        _bbsPortNum = new NumericUpDown
        {
            Location = new Point(70, ty),
            Width = 70,
            Minimum = 1,
            Maximum = 65535,
            Value = settings.Port > 0 ? settings.Port : 23,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        telnetPanel.Controls.Add(_bbsPortNum);
        
        tab.Controls.Add(telnetPanel);
        
        // ── Logon Automation Section ──
        var logonPanel = new Panel
        {
            Location = new Point(10, 95),
            Size = new Size(535, 200),
            BackColor = Color.FromArgb(40, 40, 40)
        };
        
        AddLabel(logonPanel, "── Logon Automation ──", 10, 8);
        
        _logonSequencesList = new ListView
        {
            Location = new Point(10, 30),
            Size = new Size(430, 130),
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.White
        };
        _logonSequencesList.Columns.Add("Trigger Message", 200);
        _logonSequencesList.Columns.Add("Response", 200);
        
        // Load existing sequences
        foreach (var seq in settings.LogonSequences)
        {
            var item = new ListViewItem(seq.TriggerMessage);
            item.SubItems.Add(seq.Response);
            _logonSequencesList.Items.Add(item);
        }
        
        logonPanel.Controls.Add(_logonSequencesList);
        
        // Buttons for logon sequences
        var addLogonBtn = new Button
        {
            Text = "Add",
            Location = new Point(450, 30),
            Size = new Size(75, 25),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        addLogonBtn.Click += AddLogonSequence_Click;
        logonPanel.Controls.Add(addLogonBtn);
        
        var editLogonBtn = new Button
        {
            Text = "Edit",
            Location = new Point(450, 60),
            Size = new Size(75, 25),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        editLogonBtn.Click += EditLogonSequence_Click;
        logonPanel.Controls.Add(editLogonBtn);
        
        var removeLogonBtn = new Button
        {
            Text = "Remove",
            Location = new Point(450, 90),
            Size = new Size(75, 25),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        removeLogonBtn.Click += RemoveLogonSequence_Click;
        logonPanel.Controls.Add(removeLogonBtn);
        
        var moveUpBtn = new Button
        {
            Text = "▲",
            Location = new Point(450, 125),
            Size = new Size(35, 25),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        moveUpBtn.Click += MoveLogonUp_Click;
        logonPanel.Controls.Add(moveUpBtn);
        
        var moveDownBtn = new Button
        {
            Text = "▼",
            Location = new Point(490, 125),
            Size = new Size(35, 25),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        moveDownBtn.Click += MoveLogonDown_Click;
        logonPanel.Controls.Add(moveDownBtn);
        
        // Info label
        AddLabel(logonPanel, "Sequences are matched in order during login (before HP bar appears)", 10, 165);
        
        tab.Controls.Add(logonPanel);
        
        // ── Commands Section ──
        var commandsPanel = new Panel
        {
            Location = new Point(280, 10),
            Size = new Size(265, 80),
            BackColor = Color.FromArgb(40, 40, 40)
        };
        
        int cy = 8;
        AddLabel(commandsPanel, "── Commands ──", 10, cy);
        cy += 22;
        
        AddLabel(commandsPanel, "Logoff:", 10, cy + 2);
        _logoffCommandText = new TextBox
        {
            Location = new Point(80, cy),
            Width = 80,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Text = settings.LogoffCommand
        };
        commandsPanel.Controls.Add(_logoffCommandText);
        
        AddLabel(commandsPanel, "Relog:", 170, cy + 2);
        _relogCommandText = new TextBox
        {
            Location = new Point(210, cy),
            Width = 45,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Text = settings.RelogCommand
        };
        commandsPanel.Controls.Add(_relogCommandText);
        cy += 26;
        
        AddLabel(commandsPanel, "PVP Level:", 10, cy + 2);
        _pvpLevelNum = new NumericUpDown
        {
            Location = new Point(80, cy),
            Width = 60,
            Minimum = 0,
            Maximum = 1000,
            Value = settings.PvpLevel,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        commandsPanel.Controls.Add(_pvpLevelNum);
        
        tab.Controls.Add(commandsPanel);
        
        return tab;
    }
    
    private void AddLogonSequence_Click(object? sender, EventArgs e)
    {
        using var dialog = new LogonSequenceDialog();
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            var item = new ListViewItem(dialog.TriggerMessage);
            item.SubItems.Add(dialog.Response);
            _logonSequencesList.Items.Add(item);
        }
    }
    
    private void EditLogonSequence_Click(object? sender, EventArgs e)
    {
        if (_logonSequencesList.SelectedItems.Count == 0) return;
        
        var selectedItem = _logonSequencesList.SelectedItems[0];
        using var dialog = new LogonSequenceDialog(
            selectedItem.Text, 
            selectedItem.SubItems[1].Text);
        
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            selectedItem.Text = dialog.TriggerMessage;
            selectedItem.SubItems[1].Text = dialog.Response;
        }
    }
    
    private void RemoveLogonSequence_Click(object? sender, EventArgs e)
    {
        if (_logonSequencesList.SelectedItems.Count == 0) return;
        _logonSequencesList.Items.Remove(_logonSequencesList.SelectedItems[0]);
    }
    
    private void MoveLogonUp_Click(object? sender, EventArgs e)
    {
        if (_logonSequencesList.SelectedItems.Count == 0) return;
        int index = _logonSequencesList.SelectedIndices[0];
        if (index <= 0) return;
        
        var item = _logonSequencesList.Items[index];
        _logonSequencesList.Items.RemoveAt(index);
        _logonSequencesList.Items.Insert(index - 1, item);
        item.Selected = true;
    }
    
    private void MoveLogonDown_Click(object? sender, EventArgs e)
    {
        if (_logonSequencesList.SelectedItems.Count == 0) return;
        int index = _logonSequencesList.SelectedIndices[0];
        if (index >= _logonSequencesList.Items.Count - 1) return;
        
        var item = _logonSequencesList.Items[index];
        _logonSequencesList.Items.RemoveAt(index);
        _logonSequencesList.Items.Insert(index + 1, item);
        item.Selected = true;
    }
    
    private void SaveBbsSettings()
    {
        var settings = new BbsSettings
        {
            Address = _bbsAddressText.Text.Trim(),
            Port = (int)_bbsPortNum.Value,
            LogoffCommand = _logoffCommandText.Text.Trim(),
            RelogCommand = _relogCommandText.Text.Trim(),
            PvpLevel = (int)_pvpLevelNum.Value
        };
        
        // Collect logon sequences from list
        settings.LogonSequences.Clear();
        foreach (ListViewItem item in _logonSequencesList.Items)
        {
            settings.LogonSequences.Add(new LogonSequence(
                item.Text,
                item.SubItems[1].Text
            ));
        }
        
        _buffManager.UpdateBbsSettings(settings);
    }
    
    #endregion
}

/// <summary>
/// Dialog for adding/editing a logon sequence
/// </summary>
public class LogonSequenceDialog : Form
{
    private TextBox _triggerTextBox = null!;
    private TextBox _responseTextBox = null!;
    
    public string TriggerMessage => _triggerTextBox.Text;
    public string Response => _responseTextBox.Text;
    
    public LogonSequenceDialog(string trigger = "", string response = "")
    {
        this.Text = string.IsNullOrEmpty(trigger) ? "Add Logon Sequence" : "Edit Logon Sequence";
        this.Size = new Size(400, 180);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.StartPosition = FormStartPosition.CenterParent;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.BackColor = Color.FromArgb(45, 45, 45);
        
        var triggerLabel = new Label
        {
            Text = "Trigger Message:",
            Location = new Point(10, 15),
            AutoSize = true,
            ForeColor = Color.White
        };
        this.Controls.Add(triggerLabel);
        
        _triggerTextBox = new TextBox
        {
            Location = new Point(10, 35),
            Width = 365,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Text = trigger
        };
        this.Controls.Add(_triggerTextBox);
        
        var responseLabel = new Label
        {
            Text = "Response (sent when trigger matches):",
            Location = new Point(10, 65),
            AutoSize = true,
            ForeColor = Color.White
        };
        this.Controls.Add(responseLabel);
        
        _responseTextBox = new TextBox
        {
            Location = new Point(10, 85),
            Width = 365,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Text = response
        };
        this.Controls.Add(_responseTextBox);
        
        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(220, 115),
            Size = new Size(75, 25),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        this.Controls.Add(okButton);
        
        var cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(300, 115),
            Size = new Size(75, 25),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        this.Controls.Add(cancelButton);
        
        this.AcceptButton = okButton;
        this.CancelButton = cancelButton;
    }
}
