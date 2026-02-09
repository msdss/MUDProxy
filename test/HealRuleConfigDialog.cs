namespace MudProxyViewer;

public class HealRuleConfigDialog : Form
{
    private HealRule _rule;
    private bool _isNew;
    private bool _isPartyWideRule;
    private HealRuleType? _selfRuleType;  // Only set for self-healing rules
    private readonly List<HealSpellConfiguration> _availableSpells;
    
    private ComboBox _spellComboBox = null!;
    private NumericUpDown _thresholdNumeric = null!;
    private NumericUpDown _partyPercentNumeric = null!;
    private Label _partyPercentLabel = null!;
    
    public HealRule Rule => _rule;
    
    public HealRuleConfigDialog(
        List<HealSpellConfiguration> availableSpells, 
        HealRule? existingRule = null,
        bool isPartyWideRule = false,
        HealRuleType? selfRuleType = null)
    {
        _availableSpells = availableSpells;
        _isNew = existingRule == null;
        _isPartyWideRule = isPartyWideRule;
        _selfRuleType = selfRuleType;
        _rule = existingRule?.Clone() ?? new HealRule { IsPartyHealRule = isPartyWideRule };
        
        if (selfRuleType.HasValue)
        {
            _rule.RuleType = selfRuleType.Value;
        }
        
        InitializeComponent();
        LoadRuleData();
    }
    
    private void InitializeComponent()
    {
        string title;
        if (_selfRuleType.HasValue)
        {
            var typeStr = _selfRuleType.Value == HealRuleType.Combat ? "Combat" : "Resting";
            title = $"{typeStr} Heal Rule";
        }
        else if (_isPartyWideRule)
        {
            title = "Party-Wide Heal Rule";
        }
        else
        {
            title = "Heal Rule";
        }
        
        this.Text = _isNew ? $"Add {title}" : $"Edit {title}";
        this.Size = new Size(420, _isPartyWideRule ? 220 : 180);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.BackColor = Color.FromArgb(45, 45, 45);
        
        var y = 15;
        var controlLeft = 160;
        var rowHeight = 35;
        
        // Spell Selection
        AddLabel("Heal Spell:", 15, y);
        _spellComboBox = new ComboBox
        {
            Location = new Point(controlLeft, y),
            Width = 220,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        foreach (var spell in _availableSpells)
        {
            _spellComboBox.Items.Add(new SpellItem(spell));
        }
        if (_spellComboBox.Items.Count > 0)
            _spellComboBox.SelectedIndex = 0;
        this.Controls.Add(_spellComboBox);
        y += rowHeight;
        
        // HP Threshold
        AddLabel("Cast when HP below:", 15, y);
        _thresholdNumeric = new NumericUpDown
        {
            Location = new Point(controlLeft, y),
            Width = 60,
            Minimum = 1,
            Maximum = 100,
            Value = 70,
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White
        };
        this.Controls.Add(_thresholdNumeric);
        AddLabel("%", controlLeft + 65, y);
        y += rowHeight;
        
        // Party Percent Required (only for party-wide rules)
        if (_isPartyWideRule)
        {
            _partyPercentLabel = AddLabel("Party % below threshold:", 15, y);
            _partyPercentNumeric = new NumericUpDown
            {
                Location = new Point(controlLeft, y),
                Width = 60,
                Minimum = 1,
                Maximum = 100,
                Value = 50,
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White
            };
            this.Controls.Add(_partyPercentNumeric);
            AddLabel("%", controlLeft + 65, y);
            y += rowHeight;
        }
        
        y += 10;
        
        // Buttons
        var saveButton = new Button
        {
            Text = "Save",
            Location = new Point(this.ClientSize.Width - 180, y),
            Size = new Size(75, 30),
            BackColor = Color.FromArgb(0, 120, 0),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.OK
        };
        saveButton.Click += SaveButton_Click;
        this.Controls.Add(saveButton);
        
        var cancelButton = new Button
        {
            Text = "Cancel",
            Location = new Point(this.ClientSize.Width - 95, y),
            Size = new Size(75, 30),
            BackColor = Color.FromArgb(80, 80, 80),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.Cancel
        };
        this.Controls.Add(cancelButton);
        
        this.AcceptButton = saveButton;
        this.CancelButton = cancelButton;
    }
    
    private Label AddLabel(string text, int x, int y)
    {
        var label = new Label
        {
            Text = text,
            Location = new Point(x, y + 3),
            AutoSize = true,
            ForeColor = Color.White
        };
        this.Controls.Add(label);
        return label;
    }
    
    private void AddHelpLabel(string text, int x, int y)
    {
        var label = new Label
        {
            Text = text,
            Location = new Point(x, y + 3),
            AutoSize = true,
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 8)
        };
        this.Controls.Add(label);
    }
    
    private void LoadRuleData()
    {
        // Find and select the spell
        for (int i = 0; i < _spellComboBox.Items.Count; i++)
        {
            if (_spellComboBox.Items[i] is SpellItem item && item.Spell.Id == _rule.HealSpellId)
            {
                _spellComboBox.SelectedIndex = i;
                break;
            }
        }
        
        _thresholdNumeric.Value = Math.Clamp(_rule.HpThresholdPercent, 1, 100);
        
        if (_isPartyWideRule && _partyPercentNumeric != null)
        {
            _partyPercentNumeric.Value = Math.Clamp(_rule.PartyPercentRequired, 1, 100);
        }
    }
    
    private void SaveButton_Click(object? sender, EventArgs e)
    {
        if (_spellComboBox.SelectedItem is not SpellItem selectedSpell)
        {
            MessageBox.Show("Please select a heal spell.", "Validation Error", 
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            this.DialogResult = DialogResult.None;
            return;
        }
        
        _rule.HealSpellId = selectedSpell.Spell.Id;
        _rule.HpThresholdPercent = (int)_thresholdNumeric.Value;
        _rule.IsPartyHealRule = _isPartyWideRule;
        
        if (_selfRuleType.HasValue)
        {
            _rule.RuleType = _selfRuleType.Value;
        }
        
        if (_isPartyWideRule && _partyPercentNumeric != null)
        {
            _rule.PartyPercentRequired = (int)_partyPercentNumeric.Value;
        }
    }
    
    private class SpellItem
    {
        public HealSpellConfiguration Spell { get; }
        
        public SpellItem(HealSpellConfiguration spell)
        {
            Spell = spell;
        }
        
        public override string ToString() => $"{Spell.DisplayName} ({Spell.Command})";
    }
}
