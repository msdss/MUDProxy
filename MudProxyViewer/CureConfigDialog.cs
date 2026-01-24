namespace MudProxyViewer;

public class CureConfigDialog : Form
{
    private readonly CureManager _cureManager;
    
    private TabControl _tabControl = null!;
    private ListBox _ailmentsListBox = null!;
    private ListBox _cureSpellsListBox = null!;
    private ListBox _priorityListBox = null!;
    
    public CureConfigDialog(CureManager cureManager)
    {
        _cureManager = cureManager;
        InitializeComponent();
        RefreshAllLists();
    }
    
    private void InitializeComponent()
    {
        this.Text = "Cure Configuration";
        this.Size = new Size(600, 500);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.BackColor = Color.FromArgb(45, 45, 45);
        
        _tabControl = new TabControl
        {
            Location = new Point(10, 10),
            Size = new Size(565, 400),
            BackColor = Color.FromArgb(45, 45, 45)
        };
        
        // Tab 1: Ailments
        var ailmentsTab = new TabPage("Ailments") { BackColor = Color.FromArgb(45, 45, 45) };
        CreateAilmentsTab(ailmentsTab);
        _tabControl.TabPages.Add(ailmentsTab);
        
        // Tab 2: Cure Spells
        var cureSpellsTab = new TabPage("Cure Spells") { BackColor = Color.FromArgb(45, 45, 45) };
        CreateCureSpellsTab(cureSpellsTab);
        _tabControl.TabPages.Add(cureSpellsTab);
        
        // Tab 3: Priority Order
        var priorityTab = new TabPage("Cast Priority") { BackColor = Color.FromArgb(45, 45, 45) };
        CreatePriorityTab(priorityTab);
        _tabControl.TabPages.Add(priorityTab);
        
        this.Controls.Add(_tabControl);
        
        var closeButton = new Button
        {
            Text = "Close",
            Location = new Point(490, 420),
            Size = new Size(80, 30),
            BackColor = Color.FromArgb(60, 60, 60),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.OK
        };
        this.Controls.Add(closeButton);
        this.AcceptButton = closeButton;
    }
    
    private void CreateAilmentsTab(TabPage tab)
    {
        var label = new Label
        {
            Text = "Define ailments that can afflict characters (poison, paralysis, etc.)",
            Location = new Point(10, 10),
            AutoSize = true,
            ForeColor = Color.Gray
        };
        tab.Controls.Add(label);
        
        _ailmentsListBox = new ListBox
        {
            Location = new Point(10, 35),
            Size = new Size(430, 300),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            Font = new Font("Consolas", 10)
        };
        _ailmentsListBox.DoubleClick += EditAilment_Click;
        tab.Controls.Add(_ailmentsListBox);
        
        tab.Controls.Add(CreateButton("Add", 450, 35, AddAilment_Click));
        tab.Controls.Add(CreateButton("Edit", 450, 70, EditAilment_Click));
        var del = CreateButton("Delete", 450, 105, DeleteAilment_Click);
        del.BackColor = Color.FromArgb(150, 0, 0);
        tab.Controls.Add(del);
    }
    
    private void CreateCureSpellsTab(TabPage tab)
    {
        var label = new Label
        {
            Text = "Define spells that cure ailments.",
            Location = new Point(10, 10),
            AutoSize = true,
            ForeColor = Color.Gray
        };
        tab.Controls.Add(label);
        
        _cureSpellsListBox = new ListBox
        {
            Location = new Point(10, 35),
            Size = new Size(430, 300),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            Font = new Font("Consolas", 10)
        };
        _cureSpellsListBox.DoubleClick += EditCureSpell_Click;
        tab.Controls.Add(_cureSpellsListBox);
        
        tab.Controls.Add(CreateButton("Add", 450, 35, AddCureSpell_Click));
        tab.Controls.Add(CreateButton("Edit", 450, 70, EditCureSpell_Click));
        var del = CreateButton("Delete", 450, 105, DeleteCureSpell_Click);
        del.BackColor = Color.FromArgb(150, 0, 0);
        tab.Controls.Add(del);
    }
    
    private void CreatePriorityTab(TabPage tab)
    {
        var label = new Label
        {
            Text = "Set the order in which actions are performed (top = highest priority).",
            Location = new Point(10, 10),
            Size = new Size(530, 20),
            ForeColor = Color.Gray
        };
        tab.Controls.Add(label);
        
        _priorityListBox = new ListBox
        {
            Location = new Point(10, 35),
            Size = new Size(300, 150),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            Font = new Font("Consolas", 11)
        };
        tab.Controls.Add(_priorityListBox);
        
        var upButton = CreateButton("▲ Up", 320, 35, MoveUp_Click);
        tab.Controls.Add(upButton);
        
        var downButton = CreateButton("▼ Down", 320, 70, MoveDown_Click);
        tab.Controls.Add(downButton);
        
        // Description of each priority type
        var descLabel = new Label
        {
            Text = "Priority Types:\n\n" +
                   "• Critical Heals - Heal rules marked as 'Critical'\n" +
                   "• Cures - Cure ailments (poison, paralysis, etc.)\n" +
                   "• Regular Heals - Heal rules NOT marked as 'Critical'\n" +
                   "• Buffs - Buff spells with auto-recast enabled",
            Location = new Point(10, 200),
            Size = new Size(400, 120),
            ForeColor = Color.LightGray
        };
        tab.Controls.Add(descLabel);
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
        RefreshAilmentsList();
        RefreshCureSpellsList();
        RefreshPriorityList();
    }
    
    private void RefreshAilmentsList()
    {
        _ailmentsListBox.Items.Clear();
        foreach (var ailment in _cureManager.Configuration.Ailments)
        {
            var indicators = new List<string>();
            if (!string.IsNullOrEmpty(ailment.PartyIndicator))
                indicators.Add($"Party: {ailment.PartyIndicator}");
            if (!string.IsNullOrEmpty(ailment.TelepathRequest))
                indicators.Add($"Telepath: {ailment.TelepathRequest}");
            
            var indicatorStr = indicators.Count > 0 ? $" [{string.Join(", ", indicators)}]" : "";
            _ailmentsListBox.Items.Add(new AilmentListItem(ailment, 
                $"{ailment.DisplayName}{indicatorStr} - {ailment.DetectionMessages.Count} msg(s)"));
        }
    }
    
    private void RefreshCureSpellsList()
    {
        _cureSpellsListBox.Items.Clear();
        foreach (var spell in _cureManager.Configuration.CureSpells.OrderBy(s => s.Priority))
        {
            var ailment = _cureManager.GetAilment(spell.AilmentId);
            _cureSpellsListBox.Items.Add(new CureSpellListItem(spell, 
                $"[P{spell.Priority}] {spell.DisplayName} ({spell.Command}) - cures {ailment?.DisplayName ?? "?"}"));
        }
    }
    
    private void RefreshPriorityList()
    {
        _priorityListBox.Items.Clear();
        foreach (var priority in _cureManager.PriorityOrder)
        {
            _priorityListBox.Items.Add(new PriorityListItem(priority, GetPriorityDisplayName(priority)));
        }
    }
    
    private string GetPriorityDisplayName(CastPriorityType type) => type switch
    {
        CastPriorityType.CriticalHeals => "🚨 Critical Heals",
        CastPriorityType.Cures => "💊 Cures",
        CastPriorityType.RegularHeals => "💚 Regular Heals",
        CastPriorityType.Buffs => "✨ Buffs",
        _ => type.ToString()
    };
    
    // Ailment CRUD
    private void AddAilment_Click(object? sender, EventArgs e)
    {
        using var dialog = new AilmentConfigDialog();
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _cureManager.AddAilment(dialog.Ailment);
            RefreshAilmentsList();
        }
    }
    
    private void EditAilment_Click(object? sender, EventArgs e)
    {
        if (_ailmentsListBox.SelectedItem is AilmentListItem item)
        {
            using var dialog = new AilmentConfigDialog(item.Ailment);
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _cureManager.UpdateAilment(dialog.Ailment);
                RefreshAllLists();
            }
        }
    }
    
    private void DeleteAilment_Click(object? sender, EventArgs e)
    {
        if (_ailmentsListBox.SelectedItem is AilmentListItem item)
        {
            if (MessageBox.Show($"Delete '{item.Ailment.DisplayName}'?\n\nThis will also remove cure spells for this ailment.",
                "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                _cureManager.RemoveAilment(item.Ailment.Id);
                RefreshAllLists();
            }
        }
    }
    
    // Cure Spell CRUD
    private void AddCureSpell_Click(object? sender, EventArgs e)
    {
        if (_cureManager.Configuration.Ailments.Count == 0)
        {
            MessageBox.Show("Please add ailments first.", "No Ailments", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        
        using var dialog = new CureSpellConfigDialog(_cureManager.Configuration.Ailments);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _cureManager.AddCureSpell(dialog.Spell);
            RefreshCureSpellsList();
        }
    }
    
    private void EditCureSpell_Click(object? sender, EventArgs e)
    {
        if (_cureSpellsListBox.SelectedItem is CureSpellListItem item)
        {
            using var dialog = new CureSpellConfigDialog(_cureManager.Configuration.Ailments, item.Spell);
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _cureManager.UpdateCureSpell(dialog.Spell);
                RefreshCureSpellsList();
            }
        }
    }
    
    private void DeleteCureSpell_Click(object? sender, EventArgs e)
    {
        if (_cureSpellsListBox.SelectedItem is CureSpellListItem item)
        {
            if (MessageBox.Show($"Delete '{item.Spell.DisplayName}'?",
                "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                _cureManager.RemoveCureSpell(item.Spell.Id);
                RefreshCureSpellsList();
            }
        }
    }
    
    // Priority reordering
    private void MoveUp_Click(object? sender, EventArgs e)
    {
        var index = _priorityListBox.SelectedIndex;
        if (index > 0)
        {
            var order = _cureManager.PriorityOrder;
            (order[index], order[index - 1]) = (order[index - 1], order[index]);
            _cureManager.PriorityOrder = order;
            RefreshPriorityList();
            _priorityListBox.SelectedIndex = index - 1;
        }
    }
    
    private void MoveDown_Click(object? sender, EventArgs e)
    {
        var index = _priorityListBox.SelectedIndex;
        if (index >= 0 && index < _priorityListBox.Items.Count - 1)
        {
            var order = _cureManager.PriorityOrder;
            (order[index], order[index + 1]) = (order[index + 1], order[index]);
            _cureManager.PriorityOrder = order;
            RefreshPriorityList();
            _priorityListBox.SelectedIndex = index + 1;
        }
    }
    
    private class AilmentListItem
    {
        public AilmentConfiguration Ailment { get; }
        public string DisplayText { get; }
        public AilmentListItem(AilmentConfiguration ailment, string text) { Ailment = ailment; DisplayText = text; }
        public override string ToString() => DisplayText;
    }
    
    private class CureSpellListItem
    {
        public CureSpellConfiguration Spell { get; }
        public string DisplayText { get; }
        public CureSpellListItem(CureSpellConfiguration spell, string text) { Spell = spell; DisplayText = text; }
        public override string ToString() => DisplayText;
    }
    
    private class PriorityListItem
    {
        public CastPriorityType Priority { get; }
        public string DisplayText { get; }
        public PriorityListItem(CastPriorityType priority, string text) { Priority = priority; DisplayText = text; }
        public override string ToString() => DisplayText;
    }
}
