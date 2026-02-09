namespace MudProxyViewer;

public class HealingConfigDialog : Form
{
    private readonly HealingManager _healingManager;
    
    private TabControl _tabControl = null!;
    private ListBox _spellsListBox = null!;
    private ListBox _selfCombatRulesListBox = null!;
    private ListBox _selfRestingRulesListBox = null!;
    private ListBox _partyRulesListBox = null!;
    private ListBox _partyWideRulesListBox = null!;
    
    public HealingConfigDialog(HealingManager healingManager)
    {
        _healingManager = healingManager;
        InitializeComponent();
        RefreshAllLists();
    }
    
    private void InitializeComponent()
    {
        this.Text = "Healing Configuration";
        this.Size = new Size(600, 550);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.BackColor = Color.FromArgb(45, 45, 45);
        
        _tabControl = new TabControl
        {
            Location = new Point(10, 10),
            Size = new Size(565, 450),
            BackColor = Color.FromArgb(45, 45, 45)
        };
        
        // Tab 1: Heal Spells
        var spellsTab = new TabPage("Heal Spells") { BackColor = Color.FromArgb(45, 45, 45) };
        CreateSpellsTab(spellsTab);
        _tabControl.TabPages.Add(spellsTab);
        
        // Tab 2: Self Heal Rules (with Combat and Resting sections)
        var selfRulesTab = new TabPage("Self Healing") { BackColor = Color.FromArgb(45, 45, 45) };
        CreateSelfHealingTab(selfRulesTab);
        _tabControl.TabPages.Add(selfRulesTab);
        
        // Tab 3: Party Heal Rules
        var partyRulesTab = new TabPage("Party Healing") { BackColor = Color.FromArgb(45, 45, 45) };
        CreateSimpleRulesTab(partyRulesTab, "Party", out _partyRulesListBox);
        _tabControl.TabPages.Add(partyRulesTab);
        
        // Tab 4: Party-Wide Heal Rules
        var partyWideRulesTab = new TabPage("Party-Wide Healing") { BackColor = Color.FromArgb(45, 45, 45) };
        CreateSimpleRulesTab(partyWideRulesTab, "PartyWide", out _partyWideRulesListBox);
        _tabControl.TabPages.Add(partyWideRulesTab);
        
        this.Controls.Add(_tabControl);
        
        var closeButton = new Button
        {
            Text = "Close",
            Location = new Point(490, 470),
            Size = new Size(80, 30),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.OK
        };
        this.Controls.Add(closeButton);
        this.AcceptButton = closeButton;
    }
    
    private void CreateSpellsTab(TabPage tab)
    {
        var label = new Label
        {
            Text = "Define your heal spells here. These can then be used in healing rules.",
            Location = new Point(10, 10),
            AutoSize = true,
            ForeColor = Color.Gray
        };
        tab.Controls.Add(label);
        
        _spellsListBox = new ListBox
        {
            Location = new Point(10, 35),
            Size = new Size(430, 300),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            Font = new Font("Consolas", 10)
        };
        _spellsListBox.DoubleClick += EditSpell_Click;
        tab.Controls.Add(_spellsListBox);
        
        tab.Controls.Add(CreateButton("Add", 450, 35, AddSpell_Click));
        tab.Controls.Add(CreateButton("Edit", 450, 70, EditSpell_Click));
        var del = CreateButton("Delete", 450, 105, DeleteSpell_Click);
        del.BackColor = Color.FromArgb(150, 0, 0);
        tab.Controls.Add(del);
    }
    
    private void CreateSelfHealingTab(TabPage tab)
    {
        int y = 10;
        
        // Combat Rules Section
        var combatLabel = new Label
        {
            Text = "Combat Rules (active during combat and idle - NOT while resting):",
            Location = new Point(10, y),
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        tab.Controls.Add(combatLabel);
        y += 22;
        
        _selfCombatRulesListBox = new ListBox
        {
            Location = new Point(10, y),
            Size = new Size(430, 130),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            Font = new Font("Consolas", 10)
        };
        _selfCombatRulesListBox.DoubleClick += (s, e) => EditSelfRule_Click(HealRuleType.Combat);
        tab.Controls.Add(_selfCombatRulesListBox);
        
        tab.Controls.Add(CreateButton("Add", 450, y, (s, e) => AddSelfRule_Click(HealRuleType.Combat)));
        tab.Controls.Add(CreateButton("Edit", 450, y + 35, (s, e) => EditSelfRule_Click(HealRuleType.Combat)));
        var delCombat = CreateButton("Delete", 450, y + 70, (s, e) => DeleteSelfRule_Click(HealRuleType.Combat));
        delCombat.BackColor = Color.FromArgb(150, 0, 0);
        tab.Controls.Add(delCombat);
        
        y += 145;
        
        // Resting Rules Section
        var restingLabel = new Label
        {
            Text = "Resting Rules (active ONLY while resting):",
            Location = new Point(10, y),
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        tab.Controls.Add(restingLabel);
        y += 22;
        
        _selfRestingRulesListBox = new ListBox
        {
            Location = new Point(10, y),
            Size = new Size(430, 130),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            Font = new Font("Consolas", 10)
        };
        _selfRestingRulesListBox.DoubleClick += (s, e) => EditSelfRule_Click(HealRuleType.Resting);
        tab.Controls.Add(_selfRestingRulesListBox);
        
        tab.Controls.Add(CreateButton("Add", 450, y, (s, e) => AddSelfRule_Click(HealRuleType.Resting)));
        tab.Controls.Add(CreateButton("Edit", 450, y + 35, (s, e) => EditSelfRule_Click(HealRuleType.Resting)));
        var delResting = CreateButton("Delete", 450, y + 70, (s, e) => DeleteSelfRule_Click(HealRuleType.Resting));
        delResting.BackColor = Color.FromArgb(150, 0, 0);
        tab.Controls.Add(delResting);
    }
    
    private void CreateSimpleRulesTab(TabPage tab, string ruleType, out ListBox listBox)
    {
        var helpText = ruleType switch
        {
            "Party" => "Rules for healing party members (single target). Heals lowest HP party member first.",
            "PartyWide" => "Rules for party-wide heals. Triggers when X% of party is below threshold.",
            _ => ""
        };
        
        tab.Controls.Add(new Label
        {
            Text = helpText,
            Location = new Point(10, 10),
            Size = new Size(530, 30),
            ForeColor = Color.Gray
        });
        
        listBox = new ListBox
        {
            Location = new Point(10, 45),
            Size = new Size(430, 340),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            Font = new Font("Consolas", 10)
        };
        listBox.DoubleClick += (s, e) => EditRule_Click(ruleType);
        tab.Controls.Add(listBox);
        
        tab.Controls.Add(CreateButton("Add", 450, 45, (s, e) => AddRule_Click(ruleType)));
        tab.Controls.Add(CreateButton("Edit", 450, 80, (s, e) => EditRule_Click(ruleType)));
        var del = CreateButton("Delete", 450, 115, (s, e) => DeleteRule_Click(ruleType));
        del.BackColor = Color.FromArgb(150, 0, 0);
        tab.Controls.Add(del);
    }
    
    private Button CreateButton(string text, int x, int y, EventHandler? onClick)
    {
        var button = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(80, 28),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        if (onClick != null) button.Click += onClick;
        return button;
    }
    
    private void RefreshAllLists()
    {
        RefreshSpellsList();
        RefreshSelfRulesList();
        RefreshPartyRulesList();
        RefreshPartyWideRulesList();
    }
    
    private void RefreshSpellsList()
    {
        _spellsListBox.Items.Clear();
        foreach (var spell in _healingManager.Configuration.HealSpells)
        {
            var typeStr = spell.TargetType switch
            {
                HealTargetType.SelfOnly => "Self",
                HealTargetType.SingleTarget => "Single",
                HealTargetType.PartyHeal => "Party",
                _ => "?"
            };
            _spellsListBox.Items.Add(new SpellListItem(spell, 
                $"{spell.DisplayName} ({spell.Command}) [{typeStr}] - {spell.ManaCost} mana"));
        }
    }
    
    private void RefreshSelfRulesList()
    {
        // Combat rules
        _selfCombatRulesListBox.Items.Clear();
        foreach (var rule in _healingManager.Configuration.SelfHealRules
            .Where(r => r.RuleType == HealRuleType.Combat)
            .OrderBy(r => r.HpThresholdPercent))
        {
            var spell = _healingManager.GetHealSpell(rule.HealSpellId);
            _selfCombatRulesListBox.Items.Add(new RuleListItem(rule, 
                $"HP < {rule.HpThresholdPercent}% → {spell?.DisplayName ?? "(Unknown)"}"));
        }
        
        // Resting rules
        _selfRestingRulesListBox.Items.Clear();
        foreach (var rule in _healingManager.Configuration.SelfHealRules
            .Where(r => r.RuleType == HealRuleType.Resting)
            .OrderBy(r => r.HpThresholdPercent))
        {
            var spell = _healingManager.GetHealSpell(rule.HealSpellId);
            _selfRestingRulesListBox.Items.Add(new RuleListItem(rule, 
                $"HP < {rule.HpThresholdPercent}% → {spell?.DisplayName ?? "(Unknown)"}"));
        }
    }
    
    private void RefreshPartyRulesList()
    {
        _partyRulesListBox.Items.Clear();
        foreach (var rule in _healingManager.Configuration.PartyHealRules.OrderBy(r => r.HpThresholdPercent))
        {
            var spell = _healingManager.GetHealSpell(rule.HealSpellId);
            _partyRulesListBox.Items.Add(new RuleListItem(rule, 
                $"HP < {rule.HpThresholdPercent}% → {spell?.DisplayName ?? "(Unknown)"}"));
        }
    }
    
    private void RefreshPartyWideRulesList()
    {
        _partyWideRulesListBox.Items.Clear();
        foreach (var rule in _healingManager.Configuration.PartyWideHealRules.OrderBy(r => r.HpThresholdPercent))
        {
            var spell = _healingManager.GetHealSpell(rule.HealSpellId);
            _partyWideRulesListBox.Items.Add(new RuleListItem(rule, 
                $"{rule.PartyPercentRequired}% of party < {rule.HpThresholdPercent}% → {spell?.DisplayName ?? "(Unknown)"}"));
        }
    }
    
    private void AddSpell_Click(object? sender, EventArgs e)
    {
        using var dialog = new HealSpellConfigDialog();
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _healingManager.AddHealSpell(dialog.Spell);
            RefreshSpellsList();
        }
    }
    
    private void EditSpell_Click(object? sender, EventArgs e)
    {
        if (_spellsListBox.SelectedItem is SpellListItem item)
        {
            using var dialog = new HealSpellConfigDialog(item.Spell);
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _healingManager.UpdateHealSpell(dialog.Spell);
                RefreshAllLists();
            }
        }
    }
    
    private void DeleteSpell_Click(object? sender, EventArgs e)
    {
        if (_spellsListBox.SelectedItem is SpellListItem item)
        {
            if (MessageBox.Show($"Delete '{item.Spell.DisplayName}'?\n\nThis will also remove rules using this spell.",
                "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                _healingManager.RemoveHealSpell(item.Spell.Id);
                RefreshAllLists();
            }
        }
    }
    
    private void AddRule_Click(string ruleType)
    {
        var spells = _healingManager.Configuration.HealSpells;
        if (spells.Count == 0)
        {
            MessageBox.Show("Please add heal spells first.", "No Spells", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        
        var filtered = ruleType switch
        {
            "Party" => spells.Where(s => s.TargetType == HealTargetType.SingleTarget).ToList(),
            "PartyWide" => spells.Where(s => s.TargetType == HealTargetType.PartyHeal).ToList(),
            _ => spells
        };
        
        if (filtered.Count == 0)
        {
            MessageBox.Show($"No applicable spells for this rule type.", "No Spells", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        
        using var dialog = new HealRuleConfigDialog(filtered, null, ruleType == "PartyWide");
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            switch (ruleType)
            {
                case "Party": _healingManager.AddPartyHealRule(dialog.Rule); RefreshPartyRulesList(); break;
                case "PartyWide": _healingManager.AddPartyWideHealRule(dialog.Rule); RefreshPartyWideRulesList(); break;
            }
        }
    }
    
    private void EditRule_Click(string ruleType)
    {
        var listBox = ruleType switch { "Party" => _partyRulesListBox, "PartyWide" => _partyWideRulesListBox, _ => null };
        if (listBox?.SelectedItem is not RuleListItem item) return;
        
        var spells = _healingManager.Configuration.HealSpells;
        var filtered = ruleType switch
        {
            "Party" => spells.Where(s => s.TargetType == HealTargetType.SingleTarget).ToList(),
            "PartyWide" => spells.Where(s => s.TargetType == HealTargetType.PartyHeal).ToList(),
            _ => spells
        };
        
        using var dialog = new HealRuleConfigDialog(filtered, item.Rule, ruleType == "PartyWide");
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            switch (ruleType)
            {
                case "Party": _healingManager.UpdatePartyHealRule(dialog.Rule); RefreshPartyRulesList(); break;
                case "PartyWide": _healingManager.UpdatePartyWideHealRule(dialog.Rule); RefreshPartyWideRulesList(); break;
            }
        }
    }
    
    private void DeleteRule_Click(string ruleType)
    {
        var listBox = ruleType switch { "Party" => _partyRulesListBox, "PartyWide" => _partyWideRulesListBox, _ => null };
        if (listBox?.SelectedItem is not RuleListItem item) return;
        
        var spell = _healingManager.GetHealSpell(item.Rule.HealSpellId);
        if (MessageBox.Show($"Delete rule: HP < {item.Rule.HpThresholdPercent}% → {spell?.DisplayName}?",
            "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
        {
            switch (ruleType)
            {
                case "Party": _healingManager.RemovePartyHealRule(item.Rule.Id); RefreshPartyRulesList(); break;
                case "PartyWide": _healingManager.RemovePartyWideHealRule(item.Rule.Id); RefreshPartyWideRulesList(); break;
            }
        }
    }
    
    // Self heal rule handlers (state-based)
    private void AddSelfRule_Click(HealRuleType ruleType)
    {
        var spells = _healingManager.Configuration.HealSpells;
        if (spells.Count == 0)
        {
            MessageBox.Show("Please add heal spells first.", "No Spells", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        
        var filtered = spells.Where(s => s.TargetType != HealTargetType.PartyHeal).ToList();
        
        if (filtered.Count == 0)
        {
            MessageBox.Show("No applicable spells for self healing.", "No Spells", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        
        using var dialog = new HealRuleConfigDialog(filtered, null, false, ruleType);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            dialog.Rule.RuleType = ruleType;
            _healingManager.AddSelfHealRule(dialog.Rule);
            RefreshSelfRulesList();
        }
    }
    
    private void EditSelfRule_Click(HealRuleType ruleType)
    {
        var listBox = ruleType == HealRuleType.Combat ? _selfCombatRulesListBox : _selfRestingRulesListBox;
        if (listBox?.SelectedItem is not RuleListItem item) return;
        
        var spells = _healingManager.Configuration.HealSpells;
        var filtered = spells.Where(s => s.TargetType != HealTargetType.PartyHeal).ToList();
        
        using var dialog = new HealRuleConfigDialog(filtered, item.Rule, false, ruleType);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _healingManager.UpdateSelfHealRule(dialog.Rule);
            RefreshSelfRulesList();
        }
    }
    
    private void DeleteSelfRule_Click(HealRuleType ruleType)
    {
        var listBox = ruleType == HealRuleType.Combat ? _selfCombatRulesListBox : _selfRestingRulesListBox;
        if (listBox?.SelectedItem is not RuleListItem item) return;
        
        var spell = _healingManager.GetHealSpell(item.Rule.HealSpellId);
        if (MessageBox.Show($"Delete rule: HP < {item.Rule.HpThresholdPercent}% → {spell?.DisplayName}?",
            "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
        {
            _healingManager.RemoveSelfHealRule(item.Rule.Id);
            RefreshSelfRulesList();
        }
    }
    
    private class SpellListItem
    {
        public HealSpellConfiguration Spell { get; }
        public string DisplayText { get; }
        public SpellListItem(HealSpellConfiguration spell, string text) { Spell = spell; DisplayText = text; }
        public override string ToString() => DisplayText;
    }
    
    private class RuleListItem
    {
        public HealRule Rule { get; }
        public string DisplayText { get; }
        public RuleListItem(HealRule rule, string text) { Rule = rule; DisplayText = text; }
        public override string ToString() => DisplayText;
    }
}
