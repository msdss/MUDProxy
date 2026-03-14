namespace MudProxyViewer;

public class SettingsDialog : Form
{
    private readonly GameManager _gameManager;
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
    private CheckBox _ignorePartyWaitCheckBox = null!;
    private NumericUpDown _partyWaitHealthThresholdNumeric = null!;
    private NumericUpDown _partyWaitTimeoutNumeric = null!;

    // Buff tab controls
    private CheckBox _buffWhileRestingCheckBox = null!;
    private CheckBox _buffWhileInCombatCheckBox = null!;
    
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
    
    // Health tab controls
    private NumericUpDown _hpRestMaxNum = null!;
    private NumericUpDown _hpRestBelowNum = null!;
    private NumericUpDown _hpRunBelowNum = null!;
    private NumericUpDown _hpHangBelowNum = null!;
    private Label _hpRestMaxLabel = null!;
    private Label _hpRestBelowLabel = null!;
    private Label _hpRunBelowLabel = null!;
    private Label _hpHangBelowLabel = null!;
    private NumericUpDown _manaRestMaxNum = null!;
    private NumericUpDown _manaRestBelowNum = null!;
    private Label _manaRestMaxLabel = null!;
    private Label _manaRestBelowLabel = null!;
    private CheckBox _useMeditateCheck = null!;
    private CheckBox _meditateBeforeRestCheck = null!;
    private CheckBox _allowHangAllOffCheck = null!;
    private CheckBox _useIntelligentRunCheck = null!;
    private NumericUpDown _healthRunDistanceNum = null!;

    // Navigation tab controls
    private CheckBox _pickLockCheck = null!;
    private NumericUpDown _maxDoorAttemptsNum = null!;
    private NumericUpDown _maxSearchAttemptsNum = null!;
    private NumericUpDown _multiActionDelayNum = null!;
    private NumericUpDown _maxRemoteActionRetriesNum = null!;

    // BBS tab controls
    private TextBox _bbsAddressText = null!;
    private NumericUpDown _bbsPortNum = null!;
    private ListView _logonSequencesList = null!;
    private TextBox _logoffCommandText = null!;
    private TextBox _relogCommandText = null!;
    private NumericUpDown _pvpLevelNum = null!;
    private CheckBox _reconnectOnFailCheck = null!;
    private CheckBox _reconnectOnLostCheck = null!;
    private NumericUpDown _maxConnectionAttemptsNum = null!;
    private NumericUpDown _connectionRetryPauseNum = null!;
    
    public SettingsDialog(GameManager gameManager, CombatManager combatManager, string currentCharacter = "")
    {
        _gameManager = gameManager;
        _buffManager = gameManager.BuffManager;
        _combatManager = combatManager;
        _currentCharacter = currentCharacter;
        InitializeComponent();
    }
    
    private void InitializeComponent()
    {
        this.Text = "Settings";
        this.Size = new Size(600, 580);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.StartPosition = FormStartPosition.CenterParent;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.BackColor = Color.FromArgb(45, 45, 45);
        
        // Tab control
        _tabControl = new TabControl
        {
            Location = new Point(10, 10),
            Size = new Size(565, 480),
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
        var navigationTab = CreateNavigationTab();
        var healthTab = CreateHealthTab();

        _tabControl.TabPages.AddRange(new TabPage[] { generalTab, combatTab, healingTab, curesTab, buffsTab, partyTab, healthTab, navigationTab, bbsTab });
        this.Controls.Add(_tabControl);
        
        // Save button
        var saveButton = new Button
        {
            Text = "Save",
            Size = new Size(80, 30),
            Location = new Point(405, 500),
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
            Location = new Point(495, 500),
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
        // Save all settings to managers
        SaveCombatSettings();
        SaveBbsSettings();
        SaveBuffSettings();
        SavePartySettings();
        SaveCurePriorityOrder();
        SaveNavigationSettings();
        SaveHealthSettings();
        
        // Save to character profile file
        var profilePath = _gameManager.CurrentProfilePath;
        if (string.IsNullOrEmpty(profilePath))
        {
            // No profile loaded - prompt user to save
            using var dialog = new SaveFileDialog
            {
                Filter = "Character Profile (*.json)|*.json",
                DefaultExt = "json",
                FileName = _gameManager.GetDefaultProfileFilename(),
                InitialDirectory = _gameManager.CharacterProfilesPath
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
        
        var (success, message) = _gameManager.SaveCharacterProfile(profilePath);
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
        
        int maxMana = _gameManager.PlayerStateManager.MaxMana;
        
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
        int maxMana = _gameManager.PlayerStateManager.MaxMana;
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
        foreach (var priority in _gameManager.CureManager.PriorityOrder)
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
            using var dialog = new HealingConfigDialog(_gameManager.HealingManager);
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
            using var dialog = new CureConfigDialog(_gameManager.CureManager);
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
            _gameManager.CureManager.ClearAllAilments();
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
        
        _buffWhileRestingCheckBox = new CheckBox
        {
            Text = "Buff while resting",
            Location = new Point(20, y),
            AutoSize = true,
            ForeColor = Color.White,
            Checked = _buffManager.BuffWhileResting
        };
        tab.Controls.Add(_buffWhileRestingCheckBox);
        y += 28;
        
        _buffWhileInCombatCheckBox = new CheckBox
        {
            Text = "Buff while in combat",
            Location = new Point(20, y),
            AutoSize = true,
            ForeColor = Color.White,
            Checked = _buffManager.BuffWhileInCombat
        };
        tab.Controls.Add(_buffWhileInCombatCheckBox);
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
            Checked = _gameManager.PartyManager.ParAutoEnabled
        };
        tab.Controls.Add(_parAutoCheckBox);
        y += 30;
        
        _parAfterTickCheckBox = new CheckBox
        {
            Text = "Send 'par' after each combat tick (overrides frequency)",
            Location = new Point(40, y),
            AutoSize = true,
            ForeColor = Color.White,
            Checked = _gameManager.PartyManager.ParAfterCombatTick
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
            Value = _gameManager.PartyManager.ParFrequencySeconds,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Enabled = !_gameManager.PartyManager.ParAfterCombatTick
        };
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
            UpdateParFrequencyEnabledState(freqLabel, secondsLabel);
        };
        
        _parAfterTickCheckBox.CheckedChanged += (s, e) => 
        {
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
            Checked = _gameManager.PartyManager.HealthRequestEnabled
        };
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
            Minimum = 15,
            Maximum = 300,
            Value = _gameManager.PartyManager.HealthRequestIntervalSeconds,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
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
        y += 45;

        // ── Party Wait System section ──
        var waitSectionLabel = new Label
        {
            Text = "Party Wait System",
            Location = new Point(20, y),
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        tab.Controls.Add(waitSectionLabel);
        y += 25;

        _ignorePartyWaitCheckBox = new CheckBox
        {
            Text = "Ignore party @Wait",
            Location = new Point(20, y),
            AutoSize = true,
            ForeColor = Color.White,
            Checked = _gameManager.PartyManager.IgnorePartyWait
        };
        _ignorePartyWaitCheckBox.CheckedChanged += (s, e) =>
        {
            var enabled = !_ignorePartyWaitCheckBox.Checked;
            _partyWaitHealthThresholdNumeric.Enabled = enabled;
            _partyWaitTimeoutNumeric.Enabled = enabled;
        };
        tab.Controls.Add(_ignorePartyWaitCheckBox);
        y += 30;

        var healthThresholdLabel = new Label
        {
            Text = "Wait if members are below",
            Location = new Point(20, y + 3),
            AutoSize = true,
            ForeColor = Color.White
        };
        tab.Controls.Add(healthThresholdLabel);

        _partyWaitHealthThresholdNumeric = new NumericUpDown
        {
            Location = new Point(200, y),
            Size = new Size(55, 25),
            Minimum = 0,
            Maximum = 100,
            Value = _gameManager.PartyManager.PartyWaitHealthThreshold,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Enabled = !_gameManager.PartyManager.IgnorePartyWait
        };
        tab.Controls.Add(_partyWaitHealthThresholdNumeric);

        var thresholdPercentLabel = new Label
        {
            Text = "% (0=disabled)",
            Location = new Point(260, y + 3),
            AutoSize = true,
            ForeColor = Color.White
        };
        tab.Controls.Add(thresholdPercentLabel);
        y += 30;

        var timeoutLabel = new Label
        {
            Text = "If leading, wait only",
            Location = new Point(20, y + 3),
            AutoSize = true,
            ForeColor = Color.White
        };
        tab.Controls.Add(timeoutLabel);

        _partyWaitTimeoutNumeric = new NumericUpDown
        {
            Location = new Point(165, y),
            Size = new Size(55, 25),
            Minimum = 0,
            Maximum = 60,
            Value = _gameManager.PartyManager.PartyWaitTimeoutMinutes,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            Enabled = !_gameManager.PartyManager.IgnorePartyWait
        };
        tab.Controls.Add(_partyWaitTimeoutNumeric);

        var timeoutMinLabel = new Label
        {
            Text = "mins (0=unlimited)",
            Location = new Point(225, y + 3),
            AutoSize = true,
            ForeColor = Color.White
        };
        tab.Controls.Add(timeoutMinLabel);

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
    }
    
    private void UpdateParFrequencyEnabledState(Label freqLabel, Label secondsLabel)
    {
        // Frequency is disabled when "par after tick" is enabled
        bool enabled = !_parAfterTickCheckBox.Checked;
        _parFrequencyNumeric.Enabled = enabled;
        freqLabel.ForeColor = enabled ? Color.White : Color.FromArgb(100, 100, 100);
        secondsLabel.ForeColor = enabled ? Color.White : Color.FromArgb(100, 100, 100);
    }

    #region Health Tab

    private TabPage CreateHealthTab()
    {
        var tab = new TabPage("Health")
        {
            BackColor = Color.FromArgb(45, 45, 45)
        };

        var settings = _gameManager.HealthSettings;
        int maxHp = _gameManager.PlayerStateManager.MaxHp;
        int maxMana = _gameManager.PlayerStateManager.MaxMana;

        // ── Health Panel (left) ──
        var healthPanel = new Panel
        {
            Location = new Point(10, 10),
            Size = new Size(260, 190),
            BackColor = Color.FromArgb(40, 40, 40)
        };

        int hy = 8;
        AddLabel(healthPanel, "── Health ──", 10, hy);
        hy += 28;

        AddLabel(healthPanel, "Rest max:", 15, hy + 2);
        _hpRestMaxNum = CreatePercentNumeric(140, hy, settings.HpRestMaxPercent);
        healthPanel.Controls.Add(_hpRestMaxNum);
        _hpRestMaxLabel = CreateValueLabel(210, hy + 2, settings.HpRestMaxPercent, maxHp);
        healthPanel.Controls.Add(_hpRestMaxLabel);
        _hpRestMaxNum.ValueChanged += (s, e) => UpdateHpLabel(_hpRestMaxLabel, (int)_hpRestMaxNum.Value, maxHp);
        hy += 30;

        AddLabel(healthPanel, "Rest if below:", 15, hy + 2);
        _hpRestBelowNum = CreatePercentNumeric(140, hy, settings.HpRestBelowPercent);
        healthPanel.Controls.Add(_hpRestBelowNum);
        _hpRestBelowLabel = CreateValueLabel(210, hy + 2, settings.HpRestBelowPercent, maxHp);
        healthPanel.Controls.Add(_hpRestBelowLabel);
        _hpRestBelowNum.ValueChanged += (s, e) => UpdateHpLabel(_hpRestBelowLabel, (int)_hpRestBelowNum.Value, maxHp);
        hy += 30;

        AddLabel(healthPanel, "Run if below:", 15, hy + 2);
        _hpRunBelowNum = CreatePercentNumeric(140, hy, settings.HpRunBelowPercent);
        healthPanel.Controls.Add(_hpRunBelowNum);
        _hpRunBelowLabel = CreateValueLabel(210, hy + 2, settings.HpRunBelowPercent, maxHp);
        healthPanel.Controls.Add(_hpRunBelowLabel);
        _hpRunBelowNum.ValueChanged += (s, e) => UpdateHpLabel(_hpRunBelowLabel, (int)_hpRunBelowNum.Value, maxHp);
        hy += 30;

        AddLabel(healthPanel, "Hang if below:", 15, hy + 2);
        _hpHangBelowNum = CreatePercentNumeric(140, hy, settings.HpHangBelowPercent);
        healthPanel.Controls.Add(_hpHangBelowNum);
        _hpHangBelowLabel = CreateValueLabel(210, hy + 2, settings.HpHangBelowPercent, maxHp);
        healthPanel.Controls.Add(_hpHangBelowLabel);
        _hpHangBelowNum.ValueChanged += (s, e) => UpdateHpLabel(_hpHangBelowLabel, (int)_hpHangBelowNum.Value, maxHp);

        tab.Controls.Add(healthPanel);

        // ── Mana/Kai Panel (right) ──
        var manaPanel = new Panel
        {
            Location = new Point(280, 10),
            Size = new Size(265, 190),
            BackColor = Color.FromArgb(40, 40, 40)
        };

        int my = 8;
        string manaTitle = _gameManager.PlayerStateManager.ManaType == "KAI" ? "── Kai ──" : "── Mana ──";
        AddLabel(manaPanel, manaTitle, 10, my);
        my += 28;

        AddLabel(manaPanel, "Rest max:", 15, my + 2);
        _manaRestMaxNum = CreatePercentNumeric(140, my, settings.ManaRestMaxPercent);
        manaPanel.Controls.Add(_manaRestMaxNum);
        _manaRestMaxLabel = CreateValueLabel(210, my + 2, settings.ManaRestMaxPercent, maxMana);
        manaPanel.Controls.Add(_manaRestMaxLabel);
        _manaRestMaxNum.ValueChanged += (s, e) => UpdateHpLabel(_manaRestMaxLabel, (int)_manaRestMaxNum.Value, maxMana);
        my += 30;

        AddLabel(manaPanel, "Rest if below:", 15, my + 2);
        _manaRestBelowNum = CreatePercentNumeric(140, my, settings.ManaRestBelowPercent);
        manaPanel.Controls.Add(_manaRestBelowNum);
        _manaRestBelowLabel = CreateValueLabel(210, my + 2, settings.ManaRestBelowPercent, maxMana);
        manaPanel.Controls.Add(_manaRestBelowLabel);
        _manaRestBelowNum.ValueChanged += (s, e) => UpdateHpLabel(_manaRestBelowLabel, (int)_manaRestBelowNum.Value, maxMana);
        my += 30;

        _useMeditateCheck = new CheckBox
        {
            Text = "Use meditate ability",
            Location = new Point(15, my),
            ForeColor = Color.White,
            AutoSize = true,
            Checked = settings.UseMeditateAbility
        };
        manaPanel.Controls.Add(_useMeditateCheck);
        my += 28;

        _meditateBeforeRestCheck = new CheckBox
        {
            Text = "Meditate before resting",
            Location = new Point(15, my),
            ForeColor = Color.White,
            AutoSize = true,
            Checked = settings.MeditateBeforeResting
        };
        manaPanel.Controls.Add(_meditateBeforeRestCheck);

        // Enable/disable meditate-before-rest based on use-meditate checkbox
        _useMeditateCheck.CheckedChanged += (s, e) =>
        {
            _meditateBeforeRestCheck.Enabled = _useMeditateCheck.Checked;
            _meditateBeforeRestCheck.ForeColor = _useMeditateCheck.Checked ? Color.White : Color.FromArgb(100, 100, 100);
        };
        _meditateBeforeRestCheck.Enabled = settings.UseMeditateAbility;
        _meditateBeforeRestCheck.ForeColor = settings.UseMeditateAbility ? Color.White : Color.FromArgb(100, 100, 100);

        tab.Controls.Add(manaPanel);

        // ── Common Panel (bottom) ──
        var commonPanel = new Panel
        {
            Location = new Point(10, 210),
            Size = new Size(535, 120),
            BackColor = Color.FromArgb(40, 40, 40)
        };

        int cy = 8;
        AddLabel(commonPanel, "── Common ──", 10, cy);
        cy += 28;

        _allowHangAllOffCheck = new CheckBox
        {
            Text = "Allow hang up in All-Off mode?",
            Location = new Point(15, cy),
            ForeColor = Color.White,
            AutoSize = true,
            Checked = settings.AllowHangInAllOff
        };
        commonPanel.Controls.Add(_allowHangAllOffCheck);
        cy += 28;

        _useIntelligentRunCheck = new CheckBox
        {
            Text = "Use intelligent run",
            Location = new Point(15, cy),
            ForeColor = Color.White,
            AutoSize = true,
            Checked = settings.UseIntelligentRun
        };
        commonPanel.Controls.Add(_useIntelligentRunCheck);

        var runDistLabel = new Label
        {
            Text = "Run distance:",
            Location = new Point(220, cy + 2),
            ForeColor = Color.White,
            AutoSize = true
        };
        commonPanel.Controls.Add(runDistLabel);

        _healthRunDistanceNum = new NumericUpDown
        {
            Location = new Point(310, cy),
            Width = 60,
            Minimum = 1,
            Maximum = 20,
            Value = settings.RunDistance,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        commonPanel.Controls.Add(_healthRunDistanceNum);

        // Enable/disable run distance based on intelligent run checkbox
        _useIntelligentRunCheck.CheckedChanged += (s, e) =>
        {
            _healthRunDistanceNum.Enabled = !_useIntelligentRunCheck.Checked;
            _healthRunDistanceNum.BackColor = _useIntelligentRunCheck.Checked
                ? Color.FromArgb(40, 40, 40)
                : Color.FromArgb(60, 60, 60);
            runDistLabel.ForeColor = _useIntelligentRunCheck.Checked
                ? Color.FromArgb(100, 100, 100)
                : Color.White;
        };
        _healthRunDistanceNum.Enabled = !settings.UseIntelligentRun;
        _healthRunDistanceNum.BackColor = settings.UseIntelligentRun
            ? Color.FromArgb(40, 40, 40)
            : Color.FromArgb(60, 60, 60);
        runDistLabel.ForeColor = settings.UseIntelligentRun
            ? Color.FromArgb(100, 100, 100)
            : Color.White;

        tab.Controls.Add(commonPanel);

        // ── Help text ──
        var helpLabel = new Label
        {
            Text = "Set thresholds to 0 to disable. Run and Hang thresholds apply to HP only.\n" +
                   "Players cannot rest or meditate with hostile enemies in the room.",
            Location = new Point(15, 340),
            Size = new Size(535, 35),
            ForeColor = Color.FromArgb(150, 150, 150),
            Font = new Font("Segoe UI", 8.5f)
        };
        tab.Controls.Add(helpLabel);

        return tab;
    }

    private NumericUpDown CreatePercentNumeric(int x, int y, int value)
    {
        return new NumericUpDown
        {
            Location = new Point(x, y),
            Width = 55,
            Minimum = 0,
            Maximum = 100,
            Value = value,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
    }

    private Label CreateValueLabel(int x, int y, int percent, int maxValue)
    {
        int actual = maxValue > 0 ? (percent * maxValue / 100) : 0;
        return new Label
        {
            Text = $"{actual}/{maxValue}",
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = Color.Gray
        };
    }

    private void UpdateHpLabel(Label label, int percent, int maxValue)
    {
        int actual = maxValue > 0 ? (percent * maxValue / 100) : 0;
        label.Text = $"{actual}/{maxValue}";
    }

    private void SaveHealthSettings()
    {
        _gameManager.HealthSettings = new HealthSettings
        {
            HpRestMaxPercent = (int)_hpRestMaxNum.Value,
            HpRestBelowPercent = (int)_hpRestBelowNum.Value,
            HpRunBelowPercent = (int)_hpRunBelowNum.Value,
            HpHangBelowPercent = (int)_hpHangBelowNum.Value,
            ManaRestMaxPercent = (int)_manaRestMaxNum.Value,
            ManaRestBelowPercent = (int)_manaRestBelowNum.Value,
            UseMeditateAbility = _useMeditateCheck.Checked,
            MeditateBeforeResting = _meditateBeforeRestCheck.Checked,
            AllowHangInAllOff = _allowHangAllOffCheck.Checked,
            UseIntelligentRun = _useIntelligentRunCheck.Checked,
            RunDistance = (int)_healthRunDistanceNum.Value
        };
    }

    #endregion

    #region Navigation Tab

    private TabPage CreateNavigationTab()
    {
        var tab = new TabPage("Navigation")
        {
            BackColor = Color.FromArgb(45, 45, 45)
        };
        
        var navSettings = _gameManager.NavigationSettings;
        
        // ── Door Handling Panel ──
        var doorPanel = new Panel
        {
            Location = new Point(10, 10),
            Size = new Size(535, 150),
            BackColor = Color.FromArgb(40, 40, 40)
        };
        
        int dy = 8;
        AddLabel(doorPanel, "── Door Handling ──", 10, dy);
        dy += 28;
        
        _pickLockCheck = new CheckBox
        {
            Text = "Pick locks instead of bashing",
            Location = new Point(15, dy),
            ForeColor = Color.White,
            AutoSize = true,
            Checked = navSettings.UsePicklockInsteadOfBash
        };
        doorPanel.Controls.Add(_pickLockCheck);
        dy += 28;

        AddLabel(doorPanel, "Max attempts (0 = unlimited):", 15, dy + 2);
        _maxDoorAttemptsNum = new NumericUpDown
        {
            Location = new Point(230, dy),
            Width = 60,
            Minimum = 0,
            Maximum = 100,
            Value = navSettings.MaxDoorAttempts,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        doorPanel.Controls.Add(_maxDoorAttemptsNum);
        dy += 30;
        
        var doorHelpLabel = new Label
        {
            Text = "When checked, the auto-walker will attempt to pick locks and then\n" +
                   "open doors. When unchecked, doors will be bashed open.\n" +
                   "All characters can bash. Only characters with the picklock\n" +
                   "skill should enable this option.",
            Location = new Point(15, dy),
            Size = new Size(500, 55),
            ForeColor = Color.FromArgb(180, 180, 180),
            Font = new Font("Segoe UI", 8.5f)
        };
        doorPanel.Controls.Add(doorHelpLabel);
        
        tab.Controls.Add(doorPanel);
        
        // ── Search Handling Panel ──
        var searchPanel = new Panel
        {
            Location = new Point(10, 170),
            Size = new Size(535, 100),
            BackColor = Color.FromArgb(40, 40, 40)
        };
        
        int sy = 8;
        AddLabel(searchPanel, "── Searchable Hidden Exits ──", 10, sy);
        sy += 28;
        
        AddLabel(searchPanel, "Max search attempts (0 = unlimited):", 15, sy + 2);
        _maxSearchAttemptsNum = new NumericUpDown
        {
            Location = new Point(270, sy),
            Width = 60,
            Minimum = 0,
            Maximum = 100,
            Value = navSettings.MaxSearchAttempts,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        searchPanel.Controls.Add(_maxSearchAttemptsNum);
        sy += 30;
        
        var searchHelpLabel = new Label
        {
            Text = "Hidden exits require searching (sea <direction>) to reveal.\n" +
                   "Search success depends on the character's perception.",
            Location = new Point(15, sy),
            Size = new Size(500, 30),
            ForeColor = Color.FromArgb(180, 180, 180),
            Font = new Font("Segoe UI", 8.5f)
        };
        searchPanel.Controls.Add(searchHelpLabel);
        
        tab.Controls.Add(searchPanel);

        // ── Multi-Action Hidden Exits Panel ──
        var multiActionPanel = new Panel
        {
            Location = new Point(10, 280),
            Size = new Size(535, 100),
            BackColor = Color.FromArgb(40, 40, 40)
        };

        int my = 8;
        AddLabel(multiActionPanel, "── Multi-Action Hidden Exits ──", 10, my);
        my += 28;

        AddLabel(multiActionPanel, "Delay between actions (ms):", 15, my + 2);
        _multiActionDelayNum = new NumericUpDown
        {
            Location = new Point(230, my),
            Width = 80,
            Minimum = 500,
            Maximum = 10000,
            Increment = 100,
            Value = navSettings.MultiActionDelayMs,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        multiActionPanel.Controls.Add(_multiActionDelayNum);
        my += 30;

        var multiActionHelpLabel = new Label
        {
            Text = "Some hidden exits require action commands (pull lever, say faith, etc.)\n" +
                   "before they become traversable. This controls the delay between commands.",
            Location = new Point(15, my),
            Size = new Size(500, 30),
            ForeColor = Color.FromArgb(180, 180, 180),
            Font = new Font("Segoe UI", 8.5f)
        };
        multiActionPanel.Controls.Add(multiActionHelpLabel);

        tab.Controls.Add(multiActionPanel);

        // ── Remote Action Exits Panel ──
        var remoteActionPanel = new Panel
        {
            Location = new Point(10, 390),
            Size = new Size(535, 100),
            BackColor = Color.FromArgb(40, 40, 40)
        };

        int ry = 8;
        AddLabel(remoteActionPanel, "── Remote Action Exits ──", 10, ry);
        ry += 28;

        AddLabel(remoteActionPanel, "Max retry attempts when exit doesn't open:", 15, ry + 2);
        _maxRemoteActionRetriesNum = new NumericUpDown
        {
            Location = new Point(330, ry),
            Width = 60,
            Minimum = 0,
            Maximum = 10,
            Value = navSettings.MaxRemoteActionRetries,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        remoteActionPanel.Controls.Add(_maxRemoteActionRetriesNum);
        ry += 30;

        var remoteActionHelpLabel = new Label
        {
            Text = "When an exit requires actions in remote rooms (pull lever in another room),\n" +
                   "the walker will retry the prerequisite sequence up to this many times if the exit doesn't open.",
            Location = new Point(15, ry),
            Size = new Size(500, 30),
            ForeColor = Color.FromArgb(180, 180, 180),
            Font = new Font("Segoe UI", 8.5f)
        };
        remoteActionPanel.Controls.Add(remoteActionHelpLabel);

        tab.Controls.Add(remoteActionPanel);

        // ── Information Panel ──
        var infoPanel = new Panel
        {
            Location = new Point(10, 500),
            Size = new Size(535, 230),
            BackColor = Color.FromArgb(40, 40, 40)
        };

        int iy = 8;
        AddLabel(infoPanel, "── Information ──", 10, iy);
        iy += 28;

        var supportedLabel = new Label
        {
            Text = "The auto-walker automatically handles these special exits:\n\n" +
                   "  •  Text command exits (go path, enter portal, etc.)\n" +
                   "  •  Hidden passages (invisible but always traversable)\n" +
                   "  •  Doors (bashed or picked based on the setting above)\n" +
                   "  •  Searchable hidden exits (searched based on the setting above)\n" +
                   "  •  Multi-action hidden exits (pull lever, say password, etc.)\n" +
                   "  •  Remote-action exits (pull lever in room A to open exit in room B)",
            Location = new Point(15, iy),
            Size = new Size(500, 125),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9)
        };
        infoPanel.Controls.Add(supportedLabel);
        iy += 130;

        var unsupportedLabel = new Label
        {
            Text = "Not yet supported:\n\n" +
                   "  •  Key/item-required exits (requires inventory management)\n" +
                   "  •  Exits requiring tolls, level ranges, or class/race gates",
            Location = new Point(15, iy),
            Size = new Size(500, 65),
            ForeColor = Color.FromArgb(140, 140, 140),
            Font = new Font("Segoe UI", 9)
        };
        infoPanel.Controls.Add(unsupportedLabel);

        tab.Controls.Add(infoPanel);

        return tab;
    }
    
    private void SaveNavigationSettings()
    {
        _gameManager.NavigationSettings = new NavigationSettings
        {
            UsePicklockInsteadOfBash = _pickLockCheck.Checked,
            MaxDoorAttempts = (int)_maxDoorAttemptsNum.Value,
            MaxSearchAttempts = (int)_maxSearchAttemptsNum.Value,
            MultiActionDelayMs = (int)_multiActionDelayNum.Value,
            MaxRemoteActionRetries = (int)_maxRemoteActionRetriesNum.Value
        };
    }
    
    #endregion
    
    #region BBS Tab
    
    private TabPage CreateBbsTab()
    {
        var tab = new TabPage("BBS")
        {
            BackColor = Color.FromArgb(45, 45, 45)
        };
        
        var settings = _gameManager.BbsSettings;
        
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
            Size = new Size(535, 185),
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
        
        // ── Reconnection Section ──
        var reconnectPanel = new Panel
        {
            Location = new Point(10, 285),
            Size = new Size(535, 65),
            BackColor = Color.FromArgb(40, 40, 40)
        };
        
        int ry = 8;
        AddLabel(reconnectPanel, "── Reconnection ──", 10, ry);
        ry += 22;
        
        _reconnectOnFailCheck = new CheckBox
        {
            Text = "Reconnect if connection fails",
            Location = new Point(10, ry),
            AutoSize = true,
            ForeColor = Color.White,
            Checked = settings.ReconnectOnConnectionFail
        };
        reconnectPanel.Controls.Add(_reconnectOnFailCheck);
        
        AddLabel(reconnectPanel, "Max attempts:", 270, ry + 2);
        _maxConnectionAttemptsNum = new NumericUpDown
        {
            Location = new Point(355, ry),
            Width = 50,
            Minimum = 0,
            Maximum = 99,
            Value = settings.MaxConnectionAttempts,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        reconnectPanel.Controls.Add(_maxConnectionAttemptsNum);
        AddLabel(reconnectPanel, "(0 = unlimited)", 410, ry + 2);
        ry += 22;
        
        _reconnectOnLostCheck = new CheckBox
        {
            Text = "Reconnect if connection lost",
            Location = new Point(10, ry),
            AutoSize = true,
            ForeColor = Color.White,
            Checked = settings.ReconnectOnConnectionLost
        };
        reconnectPanel.Controls.Add(_reconnectOnLostCheck);
        
        AddLabel(reconnectPanel, "Retry pause:", 270, ry + 2);
        _connectionRetryPauseNum = new NumericUpDown
        {
            Location = new Point(355, ry),
            Width = 50,
            Minimum = 1,
            Maximum = 300,
            Value = settings.ConnectionRetryPauseSeconds,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        reconnectPanel.Controls.Add(_connectionRetryPauseNum);
        AddLabel(reconnectPanel, "seconds", 410, ry + 2);
        
        tab.Controls.Add(reconnectPanel);
        
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
    
    private void SaveBuffSettings()
    {
        _buffManager.BuffWhileResting = _buffWhileRestingCheckBox.Checked;
        _buffManager.BuffWhileInCombat = _buffWhileInCombatCheckBox.Checked;
        _buffManager.ManaReservePercent = (int)_manaReserveNumeric.Value;
    }
    
    private void SavePartySettings()
    {
        _gameManager.PartyManager.ParAutoEnabled = _parAutoCheckBox.Checked;
        _gameManager.PartyManager.ParAfterCombatTick = _parAfterTickCheckBox.Checked;
        _gameManager.PartyManager.ParFrequencySeconds = (int)_parFrequencyNumeric.Value;
        _gameManager.PartyManager.HealthRequestEnabled = _healthRequestCheckBox.Checked;
        _gameManager.PartyManager.HealthRequestIntervalSeconds = (int)_healthRequestIntervalNumeric.Value;
        _gameManager.PartyManager.IgnorePartyWait = _ignorePartyWaitCheckBox.Checked;
        _gameManager.PartyManager.PartyWaitHealthThreshold = (int)_partyWaitHealthThresholdNumeric.Value;
        _gameManager.PartyManager.PartyWaitTimeoutMinutes = (int)_partyWaitTimeoutNumeric.Value;
    }
    
    private void SaveCurePriorityOrder()
    {
        var newOrder = new List<CastPriorityType>();
        foreach (var item in _priorityListBox.Items)
        {
            newOrder.Add(GetPriorityFromDisplayName(item.ToString() ?? ""));
        }
        _gameManager.CureManager.PriorityOrder = newOrder;
    }
    
    private void SaveBbsSettings()
    {
        var settings = new BbsSettings
        {
            Address = _bbsAddressText.Text.Trim(),
            Port = (int)_bbsPortNum.Value,
            LogoffCommand = _logoffCommandText.Text.Trim(),
            RelogCommand = _relogCommandText.Text.Trim(),
            PvpLevel = (int)_pvpLevelNum.Value,
            ReconnectOnConnectionFail = _reconnectOnFailCheck.Checked,
            ReconnectOnConnectionLost = _reconnectOnLostCheck.Checked,
            MaxConnectionAttempts = (int)_maxConnectionAttemptsNum.Value,
            ConnectionRetryPauseSeconds = (int)_connectionRetryPauseNum.Value
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
        
        _gameManager.UpdateBbsSettings(settings);
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
