namespace MudProxyViewer;

public class SettingsDialog : Form
{
    private readonly BuffManager _buffManager;
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
    
    public SettingsDialog(BuffManager buffManager)
    {
        _buffManager = buffManager;
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
        var healingTab = CreateHealingTab();
        var curesTab = CreateCuresTab();
        var buffsTab = CreateBuffsTab();
        var partyTab = CreatePartyTab();
        
        _tabControl.TabPages.AddRange(new TabPage[] { generalTab, healingTab, curesTab, buffsTab, partyTab });
        this.Controls.Add(_tabControl);
        
        // Close button
        var closeButton = new Button
        {
            Text = "Close",
            Size = new Size(80, 30),
            Location = new Point(495, 420),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.OK
        };
        this.Controls.Add(closeButton);
        this.AcceptButton = closeButton;
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
        
        // Mana Reserve section
        var manaReserveLabel = new Label
        {
            Text = "Mana Reserve % (buffs only):",
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
        
        var manaReserveHint = new Label
        {
            Text = "Buffs will not cast if mana drops below this percentage.\nHeals and cures are not affected by this setting.",
            Location = new Point(20, y + 30),
            AutoSize = true,
            ForeColor = Color.FromArgb(150, 150, 150),
            Font = new Font("Segoe UI", 8)
        };
        tab.Controls.Add(manaReserveHint);
        
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
        
        var infoLabel = new Label
        {
            Text = "Configure buff spells for auto-casting.\n\n" +
                   "Define buff spells with:\n" +
                   "- Cast command and success message\n" +
                   "- Duration and mana cost\n" +
                   "- Target type (self, melee party, caster party, all party)\n\n" +
                   "Buffs will automatically recast when they expire,\n" +
                   "respecting the mana reserve setting.",
            Location = new Point(20, y),
            Size = new Size(500, 150),
            ForeColor = Color.FromArgb(180, 180, 180),
            Font = new Font("Segoe UI", 9)
        };
        tab.Controls.Add(infoLabel);
        
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
        _parAutoCheckBox.CheckedChanged += (s, e) => _buffManager.ParAutoEnabled = _parAutoCheckBox.Checked;
        tab.Controls.Add(_parAutoCheckBox);
        y += 30;
        
        var freqLabel = new Label
        {
            Text = "Frequency:",
            Location = new Point(40, y + 3),
            AutoSize = true,
            ForeColor = Color.White
        };
        tab.Controls.Add(freqLabel);
        
        _parFrequencyNumeric = new NumericUpDown
        {
            Location = new Point(110, y),
            Size = new Size(60, 25),
            Minimum = 5,
            Maximum = 300,
            Value = _buffManager.ParFrequencySeconds,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        _parFrequencyNumeric.ValueChanged += (s, e) => _buffManager.ParFrequencySeconds = (int)_parFrequencyNumeric.Value;
        tab.Controls.Add(_parFrequencyNumeric);
        
        var secondsLabel = new Label
        {
            Text = "seconds",
            Location = new Point(175, y + 3),
            AutoSize = true,
            ForeColor = Color.White
        };
        tab.Controls.Add(secondsLabel);
        y += 35;
        
        _parAfterTickCheckBox = new CheckBox
        {
            Text = "Send 'par' after each combat tick",
            Location = new Point(20, y),
            AutoSize = true,
            ForeColor = Color.White,
            Checked = _buffManager.ParAfterCombatTick
        };
        _parAfterTickCheckBox.CheckedChanged += (s, e) => _buffManager.ParAfterCombatTick = _parAfterTickCheckBox.Checked;
        tab.Controls.Add(_parAfterTickCheckBox);
        y += 45;
        
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
            CastPriorityType.CriticalHeals => "Critical Heals",
            CastPriorityType.Cures => "Cures",
            CastPriorityType.RegularHeals => "Regular Heals",
            CastPriorityType.Buffs => "Buffs",
            _ => priority.ToString()
        };
    }
    
    private CastPriorityType GetPriorityFromDisplayName(string name)
    {
        return name switch
        {
            "Critical Heals" => CastPriorityType.CriticalHeals,
            "Cures" => CastPriorityType.Cures,
            "Regular Heals" => CastPriorityType.RegularHeals,
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
}
